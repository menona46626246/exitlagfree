using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.Core.Relay;

/// <summary>Modelo de importación/exportación de relays (JSON).</summary>
public sealed class RelayExportModel
{
    public const string FormatMarker = "gameroute-relays";

    public string Format { get; set; } = FormatMarker;

    public int Version { get; set; } = 1;

    public string ExportedUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public List<RelayNode> Relays { get; set; } = new();
}

/// <summary>Gestor de relays: CRUD, import/export, medición de salud.</summary>
public sealed class RelayManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly DataStore _store;
    private readonly ISecretProtector _secrets;
    private List<RelayNode> _relays;

    public event Action<RelayNode>? RelayAdded;
    public event Action<RelayNode>? RelayUpdated;
    public event Action<string>? RelayRemoved;

    public RelayManager(DataStore store, ISecretProtector secrets)
    {
        _store = store;
        _secrets = secrets;
        _relays = store.LoadRelays();
    }

    public IReadOnlyList<RelayNode> Relays => _relays.AsReadOnly();

    public RelayNode? GetById(string id) => _relays.FirstOrDefault(r => r.Id == id);

    public void Add(RelayNode relay)
    {
        var errors = relay.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        if (_relays.Any(r => r.Id == relay.Id))
            throw new ArgumentException("Ya existe un relay con ese id.");
        _relays.Add(relay);
        Persist();
        RelayAdded?.Invoke(relay);
    }

    public void Update(RelayNode relay)
    {
        var existing = GetById(relay.Id) ?? throw new KeyNotFoundException("Relay no encontrado.");
        var index = _relays.IndexOf(existing);
        _relays[index] = relay;
        Persist();
        RelayUpdated?.Invoke(relay);
    }

    public bool Remove(string id)
    {
        var removed = _relays.RemoveAll(r => r.Id == id) > 0;
        if (removed) { Persist(); RelayRemoved?.Invoke(id); }
        return removed;
    }

    public void SetEnabled(string id, bool enabled)
    {
        var relay = GetById(id) ?? throw new KeyNotFoundException("Relay no encontrado.");
        relay.IsEnabled = enabled;
        Update(relay);
    }

    private void Persist() => _store.SaveRelays(_relays);

    // ---------- Import / Export ----------

    /// <summary>Exporta relays. Los secretos (referencias a claves privadas) se excluyen salvo consentimiento explícito.</summary>
    public string ExportRelaysJson(bool includeSecretRefs = false)
    {
        var list = includeSecretRefs ? _relays : _relays.Select(r => { var c = r.Clone(); c.PrivateKeyRef = string.Empty; return c; }).ToList();
        var model = new RelayExportModel { Relays = list };
        return JsonSerializer.Serialize(model, JsonOpts);
    }

    /// <summary>Importa relays desde JSON (acepta lista plana o modelo exportado).</summary>
    public int ImportRelaysJson(string json)
    {
        List<RelayNode>? relays;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            relays = root.TryGetProperty("relays", out var inner)
                ? JsonSerializer.Deserialize<List<RelayNode>>(inner.GetRawText(), JsonOpts)
                : JsonSerializer.Deserialize<List<RelayNode>>(root.GetRawText(), JsonOpts);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("El archivo no es un JSON válido de relays.");
        }
        if (relays is null || relays.Count == 0) return 0;
        var imported = 0;
        foreach (var relay in relays)
        {
            if (string.IsNullOrEmpty(relay.Id) || _relays.Any(r => r.Id == relay.Id))
                relay.Id = Guid.NewGuid().ToString("N");
            // Las referencias a secretos de otra máquina no son válidas aquí.
            relay.PrivateKeyRef = string.Empty;
            var errors = relay.Validate();
            if (errors.Count > 0) continue; // se omiten inválidos; el llamador informa el conteo
            _relays.Add(relay);
            RelayAdded?.Invoke(relay);
            imported++;
        }
        if (imported > 0) Persist();
        return imported;
    }

    /// <summary>
    /// Importa un archivo .conf de WireGuard como relay. La clave privada se cifra con
    /// SecretProtector y NO se guarda en claro en ningún archivo.
    /// </summary>
    public RelayNode ImportWireGuardConfig(string confContent, string? displayName = null)
    {
        var tunnel = WireGuardConfigParser.Parse(confContent);
        var errors = WireGuardConfigParser.Validate(tunnel);
        if (errors.Count > 0)
            throw new InvalidDataException("Configuración WireGuard inválida: " + string.Join(" ", errors));

        string keyRef = string.Empty;
        if (!string.IsNullOrWhiteSpace(tunnel.PrivateKey))
            keyRef = _secrets.Protect(tunnel.PrivateKey);

        var relay = new RelayNode
        {
            Name = displayName ?? tunnel.Name ?? $"Relay {tunnel.Endpoint}",
            EndpointHost = ExtractHost(tunnel.Endpoint),
            EndpointPort = ExtractPort(tunnel.Endpoint),
            PublicKey = tunnel.PublicKey,
            AllowedIps = new List<string>(tunnel.AllowedIps),
            InternalDns = tunnel.Dns,
            PrivateKeyRef = keyRef,
            IsEnabled = true
        };
        Add(relay);
        return relay;
    }

    /// <summary>Recupera la clave privada cifrada de un relay (solo para construir el túnel).</summary>
    public string? GetDecryptedPrivateKey(RelayNode relay)
    {
        if (string.IsNullOrWhiteSpace(relay.PrivateKeyRef)) return null;
        try { return _secrets.Unprotect(relay.PrivateKeyRef); }
        catch
        {
            Log.Warning($"No se pudo descifrar la clave privada del relay '{relay.Name}' (¿importada de otra máquina?).");
            return null;
        }
    }

    private static string ExtractHost(string endpoint)
    {
        var idx = endpoint.LastIndexOf(':');
        return idx <= 0 ? endpoint : endpoint[..idx];
    }

    private static int ExtractPort(string endpoint)
    {
        var idx = endpoint.LastIndexOf(':');
        return idx > 0 && int.TryParse(endpoint[(idx + 1)..], out var port) ? port : 51820;
    }
}
