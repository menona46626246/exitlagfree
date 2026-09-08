using System.Text.Json;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Privileged;

/// <summary>Tipos de operación privilegiada soportados (lista cerrada, auditable en logs).</summary>
public enum PrivilegedOpKind
{
    // Túnel WireGuard (requiere la instalación oficial: wireguard.exe).
    InstallWireGuardTunnel,
    UninstallWireGuardTunnel,
    QueryWireGuardTunnel,

    // Espera (dentro del proceso elevado) a que la interfaz del túnel esté operativa.
    WaitWireGuardTunnel,

    // Rutas y DNS.
    ApplyRoutes,
    RestoreRoutes,
    SetInterfaceDns,
    RestoreInterfaceDns,

    // Kill switch (firewall: bloqueo de salida por defecto + excepciones del túnel/endpoint).
    KillSwitchEnable,
    KillSwitchDisable,
    KillSwitchStatus,

    // Secuencia de operaciones en UNA sola elevación (evita un aviso UAC por paso).
    Sequence,

    // Utilidades.
    PingProbe, // probe ICMP con bajo nivel (no usado en v1; reservado)
}

/// <summary>Petición de operación privilegiada.</summary>
public sealed class PrivilegedOp
{
    public required PrivilegedOpKind Kind { get; init; }

    /// <summary>Payload JSON según el tipo de operación.</summary>
    public string? PayloadJson { get; init; }

    /// <summary>Motivo legible (auditoría en logs).</summary>
    public string? Reason { get; init; }

    public static PrivilegedOp WithPayload(PrivilegedOpKind kind, object payload, string? reason = null) =>
        new()
        {
            Kind = kind,
            PayloadJson = JsonSerializer.Serialize(payload),
            Reason = reason,
        };
}

/// <summary>Resultado de una operación privilegiada.</summary>
public sealed class PrivilegedOpResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? StandardOutput { get; set; }
    public string? Detail { get; set; }

    public static PrivilegedOpResult Success(string? output = null, string? detail = null) =>
        new() { Ok = true, StandardOutput = output, Detail = detail };

    public static PrivilegedOpResult Failure(string error, string? detail = null) =>
        new() { Ok = false, Error = error, Detail = detail };
}

/// <summary>Ejecutor de operaciones privilegiadas (abstracción; ver implementaciones).</summary>
public interface IPrivilegedOps
{
    bool IsAvailable { get; }

    Task<PrivilegedOpResult> RunAsync(PrivilegedOp op, CancellationToken ct);
}

// ------- payloads -------

public sealed class WireGuardTunnelPayload
{
    /// <summary>Texto completo del .conf temporal (la clave viaja solo por el pipe local/argv).</summary>
    public string? ConfigText { get; set; }

    /// <summary>Nombre lógico del túnel (nombre del servicio WireGuard).</summary>
    public string? TunnelName { get; set; }

    /// <summary>Segundos máximos de espera (op WaitWireGuardTunnel; 0 = 25 por defecto).</summary>
    public int TimeoutSeconds { get; set; }
}

/// <summary>
/// Varias operaciones ejecutadas en una sola sesión elevada, en orden y deteniéndose en el
/// primer fallo. Reduce los avisos UAC a uno por acción compuesta (activar/detener túnel).
/// </summary>
public sealed class SequencePayload
{
    public List<PrivilegedOp> Steps { get; set; } = new();
}

public sealed class RoutesPayload
{
    public List<RoutePlanEntry> Entries { get; set; } = new();
}

/// <summary>Una ruta a añadir/eliminar (modelo simple: netsh interface ipv4 add/delete route).</summary>
public sealed class RoutePlanEntry
{
    public string Prefix { get; set; } = string.Empty;      // "10.0.0.0/8"
    public string InterfaceName { get; set; } = string.Empty;
    public string? Gateway { get; set; }                    // opcional (on-link si se omite)
    public int Metric { get; set; } = 1;
}

public sealed class DnsPayload
{
    public string InterfaceName { get; set; } = string.Empty;
    public List<string> Servers { get; set; } = new();
}

public sealed class KillSwitchPayload
{
    /// <summary>Interfaz del túnel que queda permitida (todo su tráfico).</summary>
    public string? TunnelInterfaceName { get; set; }

    /// <summary>IPs del endpoint del relay (el handshake de WireGuard sale por la interfaz física).</summary>
    public List<string> EndpointIps { get; set; } = new();

    /// <summary>Puerto UDP del endpoint del relay.</summary>
    public int EndpointPort { get; set; }
}
