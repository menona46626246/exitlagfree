using System.Net;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Abstracción del transporte de probes para poder probar la lógica sin red real
/// (tests usan una implementación simulada; la app usa <see cref="SystemProbeTransport"/>).
/// </summary>
public interface IProbeTransport
{
    string Description { get; }

    Task<ProbeReply> IcmpProbeAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken ct);

    Task<ProbeReply> TcpConnectAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct);

    /// <summary>Envía un datagrama benigno y espera cualquier respuesta (o ICMP de error).</summary>
    Task<ProbeReply> UdpProbeAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct);

    Task<ProbeReply> HttpProbeAsync(Uri url, int timeoutMs, CancellationToken ct);
}
