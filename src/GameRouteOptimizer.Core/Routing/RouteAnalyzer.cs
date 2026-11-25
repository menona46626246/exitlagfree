using GameRouteOptimizer.Core.AutoSwitch;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Scoring;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Routing;

/// <summary>
/// Analizador de rutas: construye candidatos (directa + relays), los mide y produce
/// una recomendación. HONESTIDAD (D5): la latencia de un relay se compone de
/// usuario→relay (medida) + relay→juego (estimación del usuario, historial o desconocido).
/// </summary>
public sealed class RouteAnalyzer
{
    private readonly ProbeEngine _probeEngine;
    private readonly ScoreEngine _scoreEngine;
    private readonly SessionRecorder? _sessions;

    public RouteAnalyzer(ProbeEngine probeEngine, ScoreEngine? scoreEngine = null, SessionRecorder? sessions = null)
    {
        _probeEngine = probeEngine;
        _scoreEngine = scoreEngine ?? new ScoreEngine();
        _sessions = sessions;
    }

    /// <summary>Mide la ruta directa hacia el servidor objetivo.</summary>
    public async Task<RouteCandidate> ProbeDirectAsync(GameServer server, ProbeMode mode, CancellationToken ct = default)
    {
        var summary = await _probeEngine.ProbeAsync(server, mode, ct);
        return new RouteCandidate
        {
            CandidateId = RouteCandidate.DirectId,
            DisplayName = "Ruta directa",
            LatencyMs = summary.IsReachable ? summary.MeanMs : double.MaxValue,
            LossRatio = summary.LossRatio,
            JitterMs = summary.JitterMs,
            StabilityScore = summary.StabilityScore,
            SampleCount = summary.SuccessCount,
            Unreachable = !summary.IsReachable,
            IcmpUnreliable = summary.IcmpUnreliable
        };
    }

    /// <summary>
    /// Mide un relay y construye su candidato con estimación total:
    /// total ≈ usuario→relay (medida TCP/ICMP al endpoint) + relay→juego (estimado).
    /// </summary>
    public async Task<RouteCandidate> ProbeRelayAsync(
        RelayNode relay, GameServer server, ProbeMode mode, CancellationToken ct = default)
    {
        var relayTarget = new GameServer
        {
            Name = relay.Name,
            Ip = System.Net.IPAddress.TryParse(relay.EndpointHost, out _)
                ? relay.EndpointHost : string.Empty,
            Domain = System.Net.IPAddress.TryParse(relay.EndpointHost, out _)
                ? string.Empty : relay.EndpointHost
        };

        var summary = await _probeEngine.ProbeAsync(relayTarget, mode, ct);

        // Relay→juego: estimación explícita del relay, o historial de sesiones, o desconocido.
        double? relayToGame = relay.RelayToGameMs;
        var unknown = relayToGame is null;
        if (unknown && _sessions is not null)
        {
            var historical = _sessions.GetHistoricalOptimizedMean(
                server.GetHashCode().ToString(), relay.Id);
            if (historical is { } h && h > 0)
            {
                relayToGame = h;
                unknown = false;
            }
        }

        var userToRelay = summary.IsReachable ? summary.MeanMs : double.MaxValue;
        var total = summary.IsReachable ? userToRelay + (relayToGame ?? 0) : double.MaxValue;

        var candidate = new RouteCandidate
        {
            CandidateId = relay.Id,
            DisplayName = relay.Name,
            Relay = relay,
            UserToRelayMs = summary.IsReachable ? userToRelay : null,
            RelayToGameComponentMs = relayToGame,
            RelayToGameUnknown = unknown,
            LatencyMs = total,
            LossRatio = summary.LossRatio,
            JitterMs = summary.JitterMs,
            StabilityScore = summary.StabilityScore,
            SampleCount = summary.SuccessCount,
            Unreachable = !summary.IsReachable
        };

        if (summary.IsReachable && unknown)
            candidate.LatencyMs = total + 0; // el desconocido se penaliza en el score, no en la suma
        return candidate;
    }

    /// <summary>
    /// Comparación completa: directa + todos los relays habilitados (no bloqueados) →
    /// recomendación puntuada con explicación en español.
    /// </summary>
    public async Task<RouteRecommendation> CompareAsync(
        GameProfile profile, GameServer server, IReadOnlyList<RelayNode> relays,
        ProbeMode mode, CancellationToken ct = default)
    {
        var candidates = new List<RouteCandidate>();

        var direct = await ProbeDirectAsync(server, mode, ct);
        candidates.Add(direct);

        foreach (var relay in relays.Where(r => r.IsEnabled && !profile.BlockedRelayIds.Contains(r.Id)))
        {
            ct.ThrowIfCancellationRequested();
            var candidate = await ProbeRelayAsync(relay, server, mode, ct);
            candidates.Add(candidate);
        }

        return _scoreEngine.EvaluateAll(candidates);
    }
}
