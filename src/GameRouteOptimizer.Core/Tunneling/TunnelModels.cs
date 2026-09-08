namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>Resultado de una operación de túnel/red privilegiada.</summary>
public sealed class TunnelOpResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Detail { get; set; }

    public static TunnelOpResult Success(string? detail = null) =>
        new() { Ok = true, Detail = detail };

    public static TunnelOpResult Failure(string error, string? detail = null) =>
        new() { Ok = false, Error = error, Detail = detail };
}

/// <summary>Estado operativo del túnel reportado por el proveedor.</summary>
public enum TunnelProviderState
{
    NotInstalled,
    Connecting,
    Active,
    Degraded,
    Stopped,
    Error,
}

/// <summary>Estado consultable de un túnel.</summary>
public sealed class TunnelStatus
{
    public TunnelProviderState State { get; set; } = TunnelProviderState.NotInstalled;
    public string? InterfaceName { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool HasHandshake => State is TunnelProviderState.Active or TunnelProviderState.Degraded;
}
