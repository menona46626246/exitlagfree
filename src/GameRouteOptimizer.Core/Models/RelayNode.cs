using System.Text.Json.Serialization;

namespace GameRouteOptimizer.Core.Models;

/// <summary>Relay WireGuard gestionado por el usuario (el programa nunca aporta relays propios).</summary>
public sealed class RelayNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Proveedor/operador del relay (opcional, informativo).</summary>
    public string? Provider { get; set; }

    public string Country { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    // --- Parámetros WireGuard del relay (servidor) ---

    /// <summary>Host del endpoint del relay (IP o dominio).</summary>
    public string EndpointHost { get; set; } = string.Empty;

    public int EndpointPort { get; set; } = 51820;

    /// <summary>Clave pública del relay (base64 estándar WireGuard).</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>AllowedIPs a encaminar por el túnel (p. ej. "0.0.0.0/0, ::/0").</summary>
    public string AllowedIps { get; set; } = "0.0.0.0/0, ::/0";

    /// <summary>Direcciones de interfaz local sugeridas (Address = …), p. ej. "10.66.0.2/32".</summary>
    public List<string> TunnelAddresses { get; set; } = new();

    /// <summary>DNS interno opcional del túnel.</summary>
    public string? DnsInternal { get; set; }

    /// <summary>PersistentKeepalive (segundos; 0 = desactivado).</summary>
    public int PersistentKeepalive { get; set; } = 25;

    /// <summary>MTU sugerido (null = por defecto).</summary>
    public int? Mtu { get; set; }

    // --- Estado / preferencias ---

    public bool Enabled { get; set; } = true;

    /// <summary>Menor valor = mayor prioridad.</summary>
    public int Priority { get; set; }

    /// <summary>Costo opcional (descripción libre, p. ej. "5 USD/mes").</summary>
    public string? CostDescription { get; set; }

    /// <summary>Carga estimada opcional 0-100 (null = desconocida).</summary>
    public int? EstimatedLoadPercent { get; set; }

    /// <summary>Disponibilidad histórica 0-1 (null = desconocida).</summary>
    public double? Availability { get; set; }

    /// <summary>Último resultado de medición del relay.</summary>
    public RelayHealth? Health { get; set; }

    public string? Notes { get; set; }

    /// <summary>Clave privada SOLO en memoria durante la sesión. Nunca se serializa.</summary>
    [JsonIgnore]
    public string? PrivateKeyPlain { get; set; }

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? $"{EndpointHost}:{EndpointPort}"
            : $"{Name} ({City}, {Country})".Trim();

    [JsonIgnore]
    public bool HasEndpoint =>
        !string.IsNullOrWhiteSpace(EndpointHost) && EndpointPort is > 0 and <= 65535;

    [JsonIgnore]
    public string Endpoint => HasEndpoint ? $"{EndpointHost}:{EndpointPort}" : "(sin endpoint)";
}

/// <summary>Resultado resumido de la última medición de un relay.</summary>
public sealed class RelayHealth
{
    public DateTimeOffset MeasuredUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Latencia media hacia el endpoint del relay, en ms.</summary>
    public double? AvgLatencyMs { get; set; }

    /// <summary>Pérdida estimada 0-100.</summary>
    public double? LossPercent { get; set; }

    /// <summary>Jitter en ms.</summary>
    public double? JitterMs { get; set; }

    public string? Note { get; set; }

    [JsonIgnore]
    public bool Healthy =>
        AvgLatencyMs.HasValue && AvgLatencyMs > 0 && LossPercent is not null && LossPercent < 100;
}
