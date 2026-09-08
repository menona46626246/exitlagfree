namespace GameRouteOptimizer.Core.Models;

/// <summary>Configuración WireGuard normalizada (equivalente a un archivo .conf).</summary>
public sealed class WireGuardConfig
{
    /// <summary>Direcciones del lado local (Address = …).</summary>
    public List<string> InterfaceAddresses { get; set; } = new();

    public int? ListenPort { get; set; }

    /// <summary>Clave privada local (solo en memoria).</summary>
    public string? PrivateKey { get; set; }

    public string? Dns { get; set; }
    public int? Mtu { get; set; }

    public List<WireGuardPeer> Peers { get; set; } = new();
}

/// <summary>Sección [Peer] de WireGuard.</summary>
public sealed class WireGuardPeer
{
    public string PublicKey { get; set; } = string.Empty;
    public string? PresharedKey { get; set; }

    /// <summary>AllowedIPs como lista CIDR.</summary>
    public List<string> AllowedIps { get; set; } = new();

    public string? EndpointHost { get; set; }
    public int EndpointPort { get; set; }
    public int? PersistentKeepalive { get; set; }
}

/// <summary>Resultado del parseo/validación de una configuración WireGuard.</summary>
public sealed class WireGuardParseResult
{
    public bool Ok { get; set; }
    public WireGuardConfig? Config { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Notificación mostrada en la UI (tipo toast/banner).</summary>
public sealed class AppNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public EventLevel Level { get; set; } = EventLevel.Information;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>Entrada del registro visible en la pantalla de Logs.</summary>
public sealed class LogEntry
{
    public DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
    public EventLevel Level { get; set; } = EventLevel.Information;
    public string Message { get; set; } = string.Empty;
    public string? Source { get; set; }
}
