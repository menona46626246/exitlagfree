using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Cálculo de estadísticas de una serie de probes: media, p95/p99, jitter (variación
/// media entre muestras consecutivas), desviación y pérdida. Lógica pura y testeable.
/// </summary>
public static class ProbeSummaryCalculator
{
    public static ProbeSummary Compute(
        string targetLabel,
        ProbeKind kind,
        IReadOnlyList<ProbeReply> attempts,
        DateTimeOffset startedUtc,
        DateTimeOffset finishedUtc,
        bool icmpReliable = true,
        string? unavailableReason = null,
        string? note = null)
    {
        var summary = new ProbeSummary
        {
            TargetLabel = targetLabel,
            Kind = kind,
            StartedUtc = startedUtc,
            FinishedUtc = finishedUtc,
            IcmpReliable = icmpReliable,
            UnavailableReason = unavailableReason,
            Note = note,
            Attempts = attempts.Count,
        };

        var rtts = attempts.Where(a => a.Success && a.RttMs.HasValue)
            .Select(a => a.RttMs!.Value)
            .OrderBy(v => v)
            .ToList();

        summary.Successes = rtts.Count;
        summary.Failures = attempts.Count - rtts.Count;

        if (rtts.Count == 0)
        {
            summary.LossPercent = attempts.Count > 0 ? 100 : null;
            if (unavailableReason is null && attempts.Count > 0)
            {
                var first = attempts[0];
                summary.UnavailableReason = first.Failure switch
                {
                    ProbeFailureReason.IcmpBlocked => "ICMP bloqueado",
                    ProbeFailureReason.Timeout => "sin respuesta",
                    ProbeFailureReason.Refused => "conexión rechazada",
                    ProbeFailureReason.Unreachable => "destino inalcanzable",
                    ProbeFailureReason.DnsFailure => "no se pudo resolver el dominio",
                    _ => "error de red",
                };
            }

            return summary;
        }

        summary.MinMs = rtts[0];
        summary.MaxMs = rtts[^1];
        summary.AvgMs = rtts.Average();
        summary.P95Ms = Percentile(rtts, 0.95);
        summary.P99Ms = Percentile(rtts, 0.99);
        summary.StdDevMs = StdDev(rtts);
        summary.JitterMs = Jitter(rtts);
        summary.LossPercent = attempts.Count > 0 ? (double)summary.Failures / attempts.Count * 100.0 : 0;
        return summary;
    }

    /// <summary>Percentil aproximado (método del rango más cercano).</summary>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 0)
        {
            return double.NaN;
        }

        if (sortedAscending.Count == 1)
        {
            return sortedAscending[0];
        }

        var rank = (sortedAscending.Count - 1) * percentile;
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sortedAscending[lower];
        }

        var frac = rank - lower;
        return sortedAscending[lower] * (1 - frac) + sortedAscending[upper] * frac;
    }

    /// <summary>Desviación típica muestral.</summary>
    public static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var avg = values.Average();
        var sum = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sum / (values.Count - 1));
    }

    /// <summary>Jitter: media de las diferencias absolutas entre muestras consecutivas.</summary>
    public static double Jitter(IReadOnlyList<double> orderedByTime)
    {
        if (orderedByTime.Count < 2)
        {
            return 0;
        }

        double total = 0;
        for (var i = 1; i < orderedByTime.Count; i++)
        {
            total += Math.Abs(orderedByTime[i] - orderedByTime[i - 1]);
        }

        return total / (orderedByTime.Count - 1);
    }
}
