namespace GameRouteOptimizer.Core.Models;

/// <summary>
/// Servidor objetivo de un juego: dominio/IP, puertos, región y protocolos de prueba.
/// Solo se realizan pruebas activas a endpoints definidos explícitamente por el usuario.
/// </summary>
public sealed class GameServer
{
    /// <summary>Nombre descriptivo del servidor (p. ej. "LATAM Sur - Ranked").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dominio del servidor (opcional si se especifica IP).</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>IP del servidor (opcional si se especifica dominio; se resuelve DNS una vez autorizado).</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Puertos TCP/UDP usados por el juego (informativo y para TCP connect).</summary>
    public List<int> Ports { get; set; } = new();

    /// <summary>Región declarada (p. ej. "SA-East").</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Protocolo preferido de prueba para este servidor.</summary>
    public ProbeProtocol PreferredProtocol { get; set; } = ProbeProtocol.Auto;

    /// <summary>URL de health check HTTP/S expuesta públicamente (opcional).</summary>
    public string HealthUrl { get; set; } = string.Empty;

    /// <summary>Si ICMP está bloqueado para este endpoint (aprendido o configurado).</summary>
    public bool IcmpBlocked { get; set; }

    /// <summary>IP resuelta (cache); vacía si no se resolvió aún.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ResolvedIp { get; set; }

    public string Endpoint => !string.IsNullOrWhiteSpace(ResolvedIp) ? ResolvedIp
        : !string.IsNullOrWhiteSpace(Ip) ? Ip
        : Domain;

    public bool IsValid => !string.IsNullOrWhiteSpace(Domain) || !string.IsNullOrWhiteSpace(Ip);

    public GameServer Clone() => (GameServer)MemberwiseClone();
}
