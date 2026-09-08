using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.Storage;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Gestión de relays WireGuard del usuario: CRUD, claves privadas cifradas (DPAPI en Windows),
/// importación desde configuración .conf y exportación saneada (sin secretos).
/// </summary>
public sealed class RelayManager
{
    private readonly ConfigStore _store;
    private readonly ISecretProtector _protector;
    private readonly object _gate = new();
    private List<RelayNode> _relays;

    public event EventHandler? RelaysChanged;

    public RelayManager(ConfigStore store, ISecretProtector protector)
    {
        _store = store;
        _protector = protector;
        _relays = store.LoadRelays();
    }

    public IReadOnlyList<RelayNode> GetAll() => _relays;

    public IReadOnlyList<RelayNode> GetEnabled() => _relays.Where(r => r.Enabled).ToList();

    public RelayNode? GetById(string? id) => _relays.FirstOrDefault(r => r.Id == id);

    public RelayNode? GetByEndpoint(string host, int port) =>
        _relays.FirstOrDefault(r =>
            string.Equals(r.EndpointHost.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase) &&
            r.EndpointPort == port);

    public string Validate(RelayNode relay)
    {
        if (string.IsNullOrWhiteSpace(relay.Name))
        {
            return "El nombre del relay es obligatorio.";
        }

        // Se permite guardar relays "en progreso" (sin endpoint/claves todavía) para
        // poder completarlos luego; medir o conectar exige los campos necesarios.
        if (!string.IsNullOrWhiteSpace(relay.EndpointHost) &&
            relay.EndpointPort is < 1 or > 65535)
        {
            return "El puerto del endpoint debe estar entre 1 y 65535.";
        }

        if (!string.IsNullOrWhiteSpace(relay.EndpointHost) &&
            !Tunneling.WireGuardConfigParser.IsValidEndpointHost(relay.EndpointHost))
        {
            return $"El host del endpoint «{relay.EndpointHost}» no parece una IP o dominio válidos.";
        }

        // La clave pública NO es obligatoria para guardar: un relay sin claves puede
        // medirse (ping al endpoint). Solo se exige al conectar el túnel.
        if (!string.IsNullOrWhiteSpace(relay.PublicKey) &&
            !Tunneling.WireGuardConfigParser.IsValidKey(relay.PublicKey))
        {
            return "La clave pública no parece una clave WireGuard válida (44 caracteres base64).";
        }

        var ips = relay.AllowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ips.Length > 0 && ips.Any(ip => !Tunneling.WireGuardConfigParser.IsValidCidr(ip)))
        {
            return "AllowedIPs contiene entradas CIDR inválidas (p. ej. 0.0.0.0/0).";
        }

        if (relay.EstimatedLoadPercent is < 0 or > 100)
        {
            return "La carga estimada debe estar entre 0 y 100.";
        }

        if (relay.Availability is < 0 or > 1)
        {
            return "La disponibilidad debe estar entre 0 y 1.";
        }

        return string.Empty;
    }

    public bool Save(RelayNode relay, out string error)
    {
        error = Validate(relay);
        if (error.Length > 0)
        {
            return false;
        }

        lock (_gate)
        {
            _store.SaveRelay(relay);
            PersistSecret(relay);
            _relays = _store.LoadRelays();
            // Restaurar secretos en memoria tras recargar.
            foreach (var r in _relays)
            {
                r.PrivateKeyPlain ??= TryLoadSecret(r.Id);
            }
        }

        RelaysChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            _store.DeleteRelay(id);
            _store.DeleteProtectedSecret(id);
            _relays.RemoveAll(r => r.Id == id);
        }

        RelaysChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Establece la clave privada en memoria y la persiste cifrada si es posible.</summary>
    /// <returns>true si se pudo cifrar/persistir; false si solo queda en memoria (aviso en UI).</returns>
    public bool SetPrivateKey(RelayNode relay, string privateKey)
    {
        if (!Tunneling.WireGuardConfigParser.IsValidKey(privateKey))
        {
            return false;
        }

        relay.PrivateKeyPlain = privateKey;
        var persisted = PersistSecret(relay);
        if (!persisted)
        {
            _store.DeleteProtectedSecret(relay.Id);
        }

        return persisted;
    }

    public bool HasUsablePrivateKey(RelayNode relay)
    {
        if (!string.IsNullOrWhiteSpace(relay.PrivateKeyPlain))
        {
            return true;
        }

        var stored = TryLoadSecret(relay.Id);
        if (stored is null)
        {
            return false;
        }

        relay.PrivateKeyPlain = stored;
        return true;
    }

    private bool PersistSecret(RelayNode relay)
    {
        if (string.IsNullOrWhiteSpace(relay.PrivateKeyPlain))
        {
            return false;
        }

        if (!_protector.IsAvailable)
        {
            return false;
        }

        try
        {
            var protectedKey = _protector.ProtectToBase64(relay.PrivateKeyPlain);
            _store.SaveProtectedSecret(relay.Id, protectedKey);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string? TryLoadSecret(string relayId)
    {
        var stored = _store.LoadProtectedSecret(relayId);
        if (stored is null)
        {
            return null;
        }

        try
        {
            return _protector.IsAvailable ? _protector.UnprotectFromBase64(stored) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Importa un archivo de configuración WireGuard (.conf) como relay.</summary>
    public RelayNode? ImportFromConfigText(string confText, string? nameOverride, out List<string> warnings)
    {
        warnings = new List<string>();
        var parsed = Tunneling.WireGuardConfigParser.Parse(confText);
        if (!parsed.Ok || parsed.Config is null)
        {
            warnings.AddRange(parsed.Errors);
            return null;
        }

        var config = parsed.Config;
        if (config.Peers.Count == 0)
        {
            warnings.Add("La configuración no tiene [Peer].");
            return null;
        }

        var peer = config.Peers[0];
        if (string.IsNullOrWhiteSpace(peer.EndpointHost))
        {
            warnings.Add("El [Peer] no tiene Endpoint: no se puede probar ni conectar.");
        }

        var relay = new RelayNode
        {
            Name = string.IsNullOrWhiteSpace(nameOverride)
                ? peer.EndpointHost ?? "Relay importado"
                : nameOverride!,
            EndpointHost = peer.EndpointHost ?? string.Empty,
            EndpointPort = peer.EndpointPort > 0 ? peer.EndpointPort : 51820,
            PublicKey = peer.PublicKey,
            AllowedIps = peer.AllowedIps.Count > 0 ? string.Join(", ", peer.AllowedIps) : "0.0.0.0/0, ::/0",
            DnsInternal = config.Dns,
            Mtu = config.Mtu,
            TunnelAddresses = config.InterfaceAddresses,
            PersistentKeepalive = peer.PersistentKeepalive ?? 25,
        };

        if (!string.IsNullOrWhiteSpace(config.PrivateKey))
        {
            if (SetPrivateKey(relay, config.PrivateKey))
            {
                warnings.Add("Clave privada importada y cifrada localmente.");
            }
            else
            {
                relay.PrivateKeyPlain = config.PrivateKey;
                warnings.Add(
                    _protector.IsAvailable
                        ? "No se pudo cifrar la clave privada; se mantiene solo en memoria."
                        : "Sin DPAPI disponible: la clave privada se mantiene SOLO en memoria (se pedirá de nuevo al conectar).");
            }
        }
        else
        {
            warnings.Add("La configuración no incluye PrivateKey: solo se podrá medir el relay, no conectar.");
        }

        warnings.AddRange(parsed.Warnings);
        return relay;
    }

    /// <summary>Exporta relays a JSON SIN claves privadas ni secretos.</summary>
    public static string ExportToJson(IEnumerable<RelayNode> relays, bool indented = true)
    {
        var dto = relays.Select(r => new
        {
            r.Id,
            r.Name,
            r.Provider,
            r.Country,
            r.City,
            r.EndpointHost,
            r.EndpointPort,
            r.PublicKey,
            r.AllowedIps,
            r.TunnelAddresses,
            r.DnsInternal,
            r.PersistentKeepalive,
            r.Mtu,
            r.Enabled,
            r.Priority,
            r.CostDescription,
            r.EstimatedLoadPercent,
            r.Availability,
            r.Notes,
            Health = r.Health,
        });
        return System.Text.Json.JsonSerializer.Serialize(dto,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = indented });
    }

    public static List<RelayNode> ImportFromJson(string json, out List<string> warnings)
    {
        warnings = new List<string>();
        try
        {
            var relays = System.Text.Json.JsonSerializer.Deserialize<List<RelayNode>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (relays is null)
            {
                warnings.Add("JSON vacío o no es una lista de relays.");
                return new List<RelayNode>();
            }

            foreach (var relay in relays)
            {
                relay.PrivateKeyPlain = null;
                relay.Health = null;
                if (string.IsNullOrWhiteSpace(relay.Id))
                {
                    relay.Id = Guid.NewGuid().ToString("N");
                }
            }

            return relays;
        }
        catch (System.Text.Json.JsonException ex)
        {
            warnings.Add("JSON inválido: " + ex.Message);
            return new List<RelayNode>();
        }
    }
}
