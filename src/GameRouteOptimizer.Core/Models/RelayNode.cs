namespace GameRouteOptimizer.Core.Models;

/// <summary>
/// Nodo relay (servidor WireGuard propio del usuario). Nunca contiene la clave privada en claro:
/// si se importa desde una configuración WireGuard, la clave se guarda cifrada (SecretProtector)
/// y aquí solo existe la referencia.
/// </summary>
public sealed class RelayNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Proveedor del relay (opcional, texto libre; sin vínculo con servicios de pago).</summary>
    public string Provider { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    /// <summary>Host o IP del endpoint WireGuard.</summary>
    public string EndpointHost { get; set; } = string.Empty;

    public int EndpointPort { get; set; } = 51820;

    /// <summary>Clave pública del peer (servidor relay) en base64.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Prefijos CIDR enroutados por el relay (AllowedIPs).</summary>
    public List<string> AllowedIps { get; set; } = new();

    /// <summary>DNS interno del relay (opcional).</summary>
    public string InternalDns { get; set; } = string.Empty;

    /// <summary>Latencia estimada relay→servidor de juego en ms (opcional; la informa el usuario o se aprende del historial).</summary>
    public double? RelayToGameMs { get; set; }

    /// <summary>Carga estimada 0-100 (opcional).</summary>
    public double? LoadPercent { get; set; }

    /// <summary>Costo opcional (0-100, mayor = peor); no representa pagos dentro de la app.</summary>
    public double? Cost { get; set; }

    public int Priority { get; set; } = 100;

    public bool IsEnabled { get; set; } = true;

    public RelayHealth Health { get; set; } = RelayHealth.Unknown;

    /// <summary>Referencia cifrada a la clave privada, si se importó una configuración completa.</summary>
    public string PrivateKeyRef { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public RelayNode Clone()
    {
        var copy = (RelayNode)MemberwiseClone();
        copy.AllowedIps = new List<string>(AllowedIps);
        return copy;
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("El nombre del relay es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(EndpointHost))
        {
            errors.Add("El endpoint (host/IP) es obligatorio.");
        }

        if (EndpointPort is < 1 or > 65535)
        {
            errors.Add("El puerto debe estar entre 1 y 65535.");
        }

        if (!string.IsNullOrWhiteSpace(PublicKey) && !WireGuardKey.IsValidPublicKey(PublicKey))
        {
            errors.Add("La clave pública no es un valor base64 de 32 bytes válido.");
        }

        return errors;
    }
}

/// <summary>Utilidades para claves WireGuard (base64 de 32 bytes, 44 caracteres con '=').</summary>
public static class WireGuardKey
{
    public static bool IsValidPublicKey(string? key)
        => TryValidate(key, out _);

    public static bool TryValidate(string? key, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) { error = "Clave vacía."; return false; }
        if (key.Length != 44 || !key.EndsWith('=')) { error = "La clave debe tener 44 caracteres base64 (32 bytes)."; return false; }
        try
        {
            var bytes = Convert.FromBase64String(key);
            if (bytes.Length != 32) { error = "La clave decodificada no tiene 32 bytes."; return false; }
            return true;
        }
        catch (FormatException)
        {
            error = "La clave no es base64 válido.";
            return false;
        }
    }
}
