using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Scoring;

/// <summary>Opciones del scoring (se alimentan desde AutoSwitchSettings/ProbingSettings).</summary>
public sealed class ScoringOptions
{
    /// <summary>Mejora mínima (ms equivalentes) para recomendar un cambio: histéresis anti-flapping.</summary>
    public double MinImprovementMs { get; set; } = 10;

    /// <summary>Pérdida (%) que excluye a un candidato por extrema.</summary>
    public double LossExtremePct { get; set; } = 25;

    /// <summary>Pérdida (%) máxima del ganador para recomendar cambio.</summary>
    public double MaxLossForSwitchPct { get; set; } = 5;

    /// <summary>Jitter (ms) máximo del ganador para recomendar cambio.</summary>
    public double MaxJitterForSwitchMs { get; set; } = 30;

    /// <summary>Penalización por relay sin datos aprendidos del tramo final (ms).</summary>
    public double UnknownTailPenaltyMs { get; set; } = 12;

    /// <summary>Penalización por relay que nunca se ha medido (ms).</summary>
    public double UnmeasuredPenaltyMs { get; set; } = 35;

    public static ScoringOptions FromSettings(AutoSwitchSettings auto, ProbingSettings probing) => new()
    {
        MinImprovementMs = auto.MinImprovementMs,
        MaxLossForSwitchPct = auto.MaxLossForSwitchPct,
        MaxJitterForSwitchMs = auto.MaxJitterForSwitchMs,
        LossExtremePct = probing.LossExtremePct,
    };
}

/// <summary>
/// Motor de puntuación de rutas. Lógica pura (sin red ni IO) para poder probarla a fondo.
/// Menor puntuación = mejor. La puntuación está en "ms equivalentes" y combina:
/// latencia efectiva + pérdida (penaliza mucho) + jitter + inestabilidad + saltos extra
/// (poco, solo si no compensan) + relay saturado + relay sin métricas/desconocido.
/// </summary>
public static class ScoreEngine
{
    public static ScoringOutput Evaluate(
        IReadOnlyList<CandidateMeasurement> candidates,
        ScoringOptions options,
        string? currentId = null,
        bool isTunnelActive = false)
    {
        var scored = new List<ScoredCandidate>();

        foreach (var candidate in candidates)
        {
            scored.Add(ScoreCandidate(candidate, options, isTunnelActive));
        }

        // Orden: primero los no excluidos por score, luego los excluidos al final.
        scored.Sort((a, b) =>
        {
            var aEx = a.Excluded ? 1 : 0;
            var bEx = b.Excluded ? 1 : 0;
            if (aEx != bEx)
            {
                return aEx - bEx;
            }

            var byScore = a.ScoreMs.CompareTo(b.ScoreMs);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.CandidateId, b.CandidateId);
        });

        var usable = scored.Where(s => !s.Excluded).ToList();
        var winner = usable.FirstOrDefault();
        var current = scored.FirstOrDefault(s => s.CandidateId == currentId) ?? usable.FirstOrDefault();

        var output = new ScoringOutput
        {
            Ranked = scored,
            WinnerId = winner?.CandidateId,
            CurrentId = currentId ?? current?.CandidateId,
        };

        if (winner is null || current is null)
        {
            output.ExplanationEs = "No hay rutas utilizables con las métricas actuales.";
            return output;
        }

        BuildExplanation(output, winner, current, options);
        return output;
    }

    private static ScoredCandidate ScoreCandidate(
        CandidateMeasurement candidate,
        ScoringOptions options,
        bool isTunnelActive)
    {
        var scored = new ScoredCandidate
        {
            CandidateId = candidate.CandidateId,
            DisplayName = candidate.DisplayName,
            Kind = candidate.Kind,
            RelayId = candidate.RelayId,
        };

        if (candidate.BlockedForGame)
        {
            scored.Excluded = true;
            scored.ExcludeReason = "Este relay está bloqueado para el juego actual.";
            scored.Confidence = 0;
            scored.ScoreMs = double.MaxValue;
            return scored;
        }

        if (candidate.Measured is not { } measured || !measured.Usable || !measured.AvgMs.HasValue)
        {
            scored.Excluded = true;
            scored.ExcludeReason = candidate.Measured is { HasData: true }
                ? $"Inalcanzable: {candidate.Measured.UnavailableReason ?? "sin respuesta"}"
                : candidate.Kind == RouteCandidateKind.Relay
                    ? "Sin mediciones: prueba el relay primero."
                    : "Sin mediciones de la ruta directa.";
            scored.Confidence = 0;
            scored.ScoreMs = double.MaxValue;
            return scored;
        }

        var avgMs = measured.AvgMs!.Value;

        var loss = measured.LossPercent ?? 0;
        var jitter = measured.JitterMs ?? 0;
        var stdDev = measured.StdDevMs ?? 0;

        if (loss >= options.LossExtremePct)
        {
            scored.Excluded = true;
            scored.ExcludeReason = $"Pérdida extrema ({loss:F0} %).";
            scored.ScoreMs = double.MaxValue;
            scored.LossPercent = loss;
            return scored;
        }

        // Latencia efectiva: media ponderada con p95 (robusta ante picos).
        var robustLatency = avgMs * 0.6 + (measured.P95Ms ?? avgMs) * 0.4;

        var penalty = 0.0;
        var penalties = new List<string>();

        // Pérdida: penaliza mucho (1 % ≈ 8 ms equivalentes).
        if (loss > 1)
        {
            penalty += (loss - 1) * 8;
            penalties.Add($"pérdida {loss:F1} %");
        }

        // Jitter: penalización moderada a partir de 15 ms.
        if (jitter > 15)
        {
            penalty += (jitter - 15) * 0.4;
            penalties.Add($"jitter {jitter:F0} ms");
        }

        // Inestabilidad (desviación): penaliza a partir de 15 ms.
        if (stdDev > 15)
        {
            penalty += (stdDev - 15) * 0.3;
            penalties.Add($"inestable (σ {stdDev:F0} ms)");
        }

        // Saltos extra: penalizan poco; solo si no hay datos que lo justifiquen.
        if (candidate.Hops is { } hops && hops > 20)
        {
            penalty += (hops - 20) * 0.3;
        }

        // Relay saturado (carga estimada opcional).
        if (candidate.LoadPercent is { } load && load > 70)
        {
            penalty += (load - 70) * 0.5;
            penalties.Add($"relay con carga {load:F0} %");
        }

        // Disponibilidad histórica baja.
        if (candidate.Availability is { } availability and >= 0 and < 1)
        {
            penalty += (1 - availability) * 40;
        }

        // Tramo final relay→servidor desconocido: penaliza y baja la confianza.
        var hasLearnedTail = candidate.Kind == RouteCandidateKind.Relay &&
                             candidate.LearnedTailMs is { } tail && tail >= 0;
        var unknownTail = candidate.Kind == RouteCandidateKind.Relay && !hasLearnedTail;
        var unmeasuredRelay = candidate.Kind == RouteCandidateKind.Relay &&
                              (candidate.Measured is null || candidate.Measured.Successes == 0);

        if (unmeasuredRelay)
        {
            penalty += options.UnmeasuredPenaltyMs;
        }
        else if (unknownTail && !isTunnelActive)
        {
            penalty += options.UnknownTailPenaltyMs;
        }

        var score = robustLatency + penalty;

        // Confianza.
        var confidence = 0.9;
        if (measured.Attempts < 5)
        {
            confidence -= 0.1;
        }

        if (loss > 5)
        {
            confidence -= 0.15;
        }

        if (stdDev > 25)
        {
            confidence -= 0.15;
        }

        if (!measured.IcmpReliable)
        {
            confidence -= 0.1;
        }

        if (candidate.Kind == RouteCandidateKind.Relay)
        {
            confidence -= 0.1;
            if (unknownTail)
            {
                confidence -= 0.2;
            }
        }

        confidence = Math.Clamp(confidence, 0.15, 0.98);

        scored.ScoreMs = score;
        scored.EffectiveLatencyMs = robustLatency;
        scored.LossPercent = loss;
        scored.JitterMs = jitter;
        scored.Confidence = confidence;
        scored.Strengths = penalties.Count == 0
            ? new List<string> { "métricas limpias" }
            : penalties;

        if (candidate.Kind == RouteCandidateKind.Relay && unknownTail && !isTunnelActive)
        {
            scored.Strengths.Add("tramo relay→servidor aún sin medir");
        }

        return scored;
    }

    private static void BuildExplanation(
        ScoringOutput output,
        ScoredCandidate winner,
        ScoredCandidate current,
        ScoringOptions options)
    {
        var reasons = new List<string>();

        var latencyGain = (current.EffectiveLatencyMs ?? 0) - (winner.EffectiveLatencyMs ?? 0);
        var lossGain = (current.LossPercent ?? 0) - (winner.LossPercent ?? 0);
        var jitterGain = (current.JitterMs ?? 0) - (winner.JitterMs ?? 0);

        if (winner.CandidateId == current.CandidateId)
        {
            output.ChangeRecommended = false;
            reasons.Add(winner.Kind == RouteCandidateKind.Relay
                ? "La ruta actual por el relay sigue siendo la mejor opción."
                : "La ruta directa sigue siendo la mejor opción.");
            output.ExplanationEs = string.Join(" ", reasons);
            output.Confidence = winner.Confidence;
            return;
        }

        if (latencyGain >= 5)
        {
            reasons.Add($"mejor latencia (≈{latencyGain:F0} ms menos)");
        }

        if (lossGain >= 2)
        {
            reasons.Add("menos pérdida");
        }

        if (jitterGain >= 5 && winner.JitterMs <= current.JitterMs * 0.8)
        {
            reasons.Add("ruta más estable");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("sin diferencia clara");
        }

        var currentDead = current.Excluded ||
                          current.LossPercent >= options.LossExtremePct ||
                          current.EffectiveLatencyMs is null;

        var improvement = (current.ScoreMs == double.MaxValue ? double.MaxValue : current.ScoreMs) - winner.ScoreMs;
        var clearGain = improvement >= options.MinImprovementMs || currentDead;
        var stableEnough = winner.LossPercent <= options.MaxLossForSwitchPct &&
                           winner.JitterMs <= options.MaxJitterForSwitchMs;

        output.ChangeRecommended = clearGain && (stableEnough || currentDead);
        output.Confidence = Math.Min(winner.Confidence, currentDead ? 0.9 : winner.Confidence + 0.05);

        var prefix = currentDead
            ? "La ruta actual no es utilizable."
            : !clearGain
                ? $"La mejora ({improvement:F0} ms) no supera el umbral de {options.MinImprovementMs:F0} ms."
                : !stableEnough
                    ? "El candidato mejora pero aún no es suficientemente estable."
                    : string.Empty;

        output.ExplanationEs = string.Join(" ", new[] { prefix }.Concat(reasons).Where(s => s.Length > 0)) + ".";
    }
}
