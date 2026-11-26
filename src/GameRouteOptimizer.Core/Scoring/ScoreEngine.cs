using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Scoring;

/// <summary>Pesos del score (configurables).</summary>
public sealed class ScoreWeights
{
    public double Latency { get; set; } = 0.50;
    public double Loss { get; set; } = 0.25;
    public double Jitter { get; set; } = 0.15;
    public double Stability { get; set; } = 0.10;

    /// <summary>Referencias absolutas para normalizar (1.0 = perfecto, 0.0 = inaceptable).</summary>
    public double MaxAcceptableLoss { get; set; } = 0.10;
    public double MaxAcceptableJitterMs { get; set; } = 30.0;

    /// <summary>Penalización por relay→juego desconocido.</summary>
    public double UnknownRelayPathPenalty { get; set; } = 10;

    /// <summary>Penalización por carga del relay (por punto de carga sobre 60).</summary>
    public double LoadPenaltyFactor { get; set; } = 0.2;

    /// <summary>Penalización por saltos extra (por salto adicional sobre el mínimo).</summary>
    public double ExtraHopPenalty { get; set; } = 1.0;
}

/// <summary>
/// Motor de scoring con referencias absolutas:
/// - latencia: ratio contra el mejor candidato (más lento que el mejor = proporcional);
/// - pérdida: 10 % = 0 puntos; jitter: 30 ms = 0 puntos (referencias absolutas);
/// - estabilidad: 0-100 directo.
/// Así, una ruta con 6 % de pérdida puede ganar a una 2 ms más rápida: la pérdida manda.
/// </summary>
public sealed class ScoreEngine
{
    private readonly ScoreWeights _weights;
    private readonly double _extremeLossThreshold;

    public ScoreEngine(ScoreWeights? weights = null, double extremeLossThreshold = 0.20)
    {
        _weights = weights ?? new ScoreWeights();
        _extremeLossThreshold = extremeLossThreshold;
    }

    /// <summary>
    /// Evalúa todos los candidatos: excluye inalcanzables y pérdida extrema, puntúa
    /// y selecciona el mejor. Devuelve la recomendación con explicación en español.
    /// </summary>
    public RouteRecommendation EvaluateAll(IEnumerable<RouteCandidate> candidatesRaw)
    {
        var candidates = candidatesRaw.Where(c => c is not null).ToList();
        var scores = new List<RouteScore>();

        var reachable = candidates.Where(c => !c.Unreachable && c.LossRatio < _extremeLossThreshold && c.SampleCount > 0).ToList();

        foreach (var candidate in candidates)
        {
            if (candidate.Unreachable || candidate.SampleCount == 0)
            {
                scores.Add(new RouteScore
                {
                    CandidateId = candidate.CandidateId,
                    Value = 0,
                    Excluded = true,
                    ExclusionReason = "Inalcanzable o sin mediciones.",
                    Explanation = "No se pudo medir esta ruta.",
                    Confidence = 0
                });
                continue;
            }
            if (candidate.LossRatio >= _extremeLossThreshold)
            {
                scores.Add(new RouteScore
                {
                    CandidateId = candidate.CandidateId,
                    Value = 0,
                    Excluded = true,
                    ExclusionReason = $"Pérdida extrema ({candidate.LossRatio:P0}).",
                    Explanation = $"Descartado por pérdida extrema ({candidate.LossRatio:P0}); usaría esta ruta de forma intermitente.",
                    Confidence = candidate.SampleCount >= 5 ? 0.9 : 0.5
                });
                continue;
            }
            scores.Add(Score(candidate, reachable));
        }

        var bestCandidate = (RouteCandidate?)null;
        var bestScore = (RouteScore?)null;
        foreach (var candidate in candidates)
        {
            var s = scores.First(x => x.CandidateId == candidate.CandidateId);
            if (s.Excluded)
            {
                continue;
            }

            if (bestScore is null || s.Value > bestScore.Value)
            {
                bestScore = s;
                bestCandidate = candidate;
            }
        }

        var recommendation = new RouteRecommendation
        {
            Candidates = candidates,
            AllScores = scores,
            Best = bestCandidate,
            BestScore = bestScore ?? new RouteScore { Excluded = true, Explanation = "Ningún candidato es viable." },
            Confidence = bestScore?.Confidence ?? 0
        };

        if (bestCandidate is null || bestScore is null)
        {
            recommendation.SimpleExplanation = "Ninguna ruta es viable ahora mismo. Revisa tu conexión.";
            recommendation.RecommendDirect = false;
        }
        else
        {
            recommendation.RecommendDirect = bestCandidate.IsDirect;
            recommendation.SimpleExplanation = DominantReason(bestCandidate, bestScore.Value, candidates);
        }

        return recommendation;
    }

    private RouteScore Score(RouteCandidate candidate, List<RouteCandidate> reachable)
    {
        var minLatency = reachable.Min(c => c.LatencyMs);

        // Latencia: ratio contra el mejor (1.0 = eres el mejor).
        var latencyScore = Math.Clamp(minLatency / Math.Max(candidate.LatencyMs, 0.001), 0, 1);

        // Pérdida absoluta: 0 % = 1.0; MaxAcceptableLoss (10 %) = 0.0.
        var lossScore = Math.Clamp(1 - candidate.LossRatio / _weights.MaxAcceptableLoss, 0, 1);

        // Jitter absoluto: 0 ms = 1.0; 30 ms = 0.0.
        var jitterScore = Math.Clamp(1 - candidate.JitterMs / _weights.MaxAcceptableJitterMs, 0, 1);

        var stabilityScore = Math.Clamp(candidate.StabilityScore / 100.0, 0, 1);

        var score = 100.0 * (
            _weights.Latency * latencyScore +
            _weights.Loss * lossScore +
            _weights.Jitter * jitterScore +
            _weights.Stability * stabilityScore);

        var penalties = new List<string>();

        if (!candidate.IsDirect && candidate.RelayToGameUnknown)
        {
            score -= _weights.UnknownRelayPathPenalty;
            penalties.Add("tramo relay→juego desconocido (−" + _weights.UnknownRelayPathPenalty.ToString("0") + ")");
        }

        if (!candidate.IsDirect && candidate.Relay is { LoadPercent: > 60 } relay)
        {
            var p = (relay.LoadPercent.Value - 60) * _weights.LoadPenaltyFactor;
            score -= p;
            penalties.Add($"relay con carga {relay.LoadPercent:0}% (−{p:0})");
        }

        if (!candidate.IsDirect && candidate.Relay is { Cost: > 0 } costed)
        {
            var p = Math.Min(costed.Cost.Value, 100) * 0.05;
            score -= p;
            penalties.Add($"costo declarado del relay (−{p:0})");
        }

        // Saltos extra penalizan poco.
        if (candidate.HopCount is int hops)
        {
            var minHops = reachable.Where(c => c.HopCount is int).Select(c => c.HopCount!.Value).DefaultIfEmpty(hops).Min();
            var extra = Math.Max(0, hops - minHops);
            if (extra > 0)
            {
                var p = extra * _weights.ExtraHopPenalty;
                score -= p;
                penalties.Add($"{extra} saltos extra (−{p:0})");
            }
        }

        score = Math.Clamp(score, 0, 100);

        // Confianza: muestras + fiabilidad ICMP + datos desconocidos.
        var confidence = 0.4 + Math.Min(candidate.SampleCount / 40.0, 1.0) * 0.6;
        if (candidate.IcmpUnreliable)
        {
            confidence -= 0.2;
        }

        if (candidate.RelayToGameUnknown)
        {
            confidence -= 0.15;
        }

        confidence = Math.Clamp(confidence, 0, 1);

        return new RouteScore
        {
            CandidateId = candidate.CandidateId,
            Value = score,
            Confidence = confidence,
            Explanation = BuildExplanation(candidate, score, penalties)
        };
    }

    private static string BuildExplanation(RouteCandidate candidate, double score, List<string> penalties)
    {
        var baseText = candidate.IsDirect
            ? $"Ruta directa con {candidate.LatencyMs:0.0} ms, pérdida {candidate.LossRatio:P1}, jitter {candidate.JitterMs:0.0} ms."
            : $"Relay '{candidate.DisplayName}': {candidate.LatencyMs:0.0} ms totales estimados, pérdida {candidate.LossRatio:P1}, jitter {candidate.JitterMs:0.0} ms.";
        if (penalties.Count > 0)
        {
            baseText += " Penalizaciones: " + string.Join("; ", penalties) + ".";
        }

        baseText += $" Score {score:0.0}/100.";
        return baseText;
    }

    /// <summary>Explicación simple del factor dominante del ganador (para la UI).</summary>
    private static string DominantReason(RouteCandidate best, double bestScore, List<RouteCandidate> all)
    {
        var viable = all.Where(c => !c.Unreachable && c.SampleCount > 0).ToList();
        var others = viable.Where(c => c.CandidateId != best.CandidateId).ToList();
        if (others.Count == 0)
        {
            return best.IsDirect ? "Única ruta disponible: usar directa." : $"Única ruta viable: relay {best.DisplayName}.";
        }

        var minLat = viable.Min(c => c.LatencyMs);
        var minLoss = viable.Min(c => c.LossRatio);
        var maxStab = viable.Max(c => c.StabilityScore);

        if (best.LatencyMs == minLat)
        {
            return "Mejor latencia disponible.";
        }

        if (best.LossRatio == minLoss)
        {
            return "Menos pérdida de paquetes.";
        }

        if (best.StabilityScore == maxStab)
        {
            return "Ruta más estable.";
        }

        return $"Mejor opción disponible (score {bestScore:0}/100).";
    }
}
