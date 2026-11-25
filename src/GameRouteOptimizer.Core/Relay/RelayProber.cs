using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;

namespace GameRouteOptimizer.Core.Relay;

/// <summary>Resultado de probar un relay (latencia TCP hacia el endpoint UDP no es posible sin handshake WG; se mide TCP/ICMP).</summary>
public sealed record RelayTestResult(string RelayId, bool Reachable, double MeanMs, double LossRatio, string Note);

/// <summary>
/// Mide la salud de los relays. Como el endpoint WireGuard es UDP, la medida práctica
/// es ICMP ping al host o TCP connect a un puerto de gestión si el usuario define uno.
/// Se registra como medida aproximada y honesta en la UI.
/// </summary>
public sealed class RelayProber
{
    private readonly ProbeEngine _probeEngine;

    public RelayProber(ProbeEngine probeEngine) => _probeEngine = probeEngine;

    public async Task<RelayTestResult> TestRelayAsync(RelayNode relay, ProbeMode mode = ProbeMode.Quick, CancellationToken ct = default)
    {
        var target = new GameServer
        {
            Name = relay.Name,
            Ip = IsIpAddress(relay.EndpointHost) ? relay.EndpointHost : string.Empty,
            Domain = IsIpAddress(relay.EndpointHost) ? string.Empty : relay.EndpointHost,
            PreferredProtocol = ProbeProtocol.Auto
        };
        try
        {
            var summary = await _probeEngine.ProbeAsync(target, mode, ct);
            relay.Health = summary.IsReachable
                ? (summary.LossRatio > 0.05 || summary.JitterMs > 15 ? RelayHealth.Degraded : RelayHealth.Up)
                : RelayHealth.Down;
            return new RelayTestResult(relay.Id, summary.IsReachable, summary.MeanMs, summary.LossRatio,
                summary.IsReachable
                    ? "Medición aproximada al endpoint del relay (no mide el handshake WireGuard)."
                    : "El relay no respondió a ICMP ni TCP (posible firewall UDP-only). Los endpoints WireGuard no responden necesariamente a otras sondas.");
        }
        catch (Exception ex)
        {
            relay.Health = RelayHealth.Down;
            return new RelayTestResult(relay.Id, false, 0, 1, $"Error al medir: {ex.Message}");
        }
    }

    private static bool IsIpAddress(string text) => System.Net.IPAddress.TryParse(text, out _);
}
