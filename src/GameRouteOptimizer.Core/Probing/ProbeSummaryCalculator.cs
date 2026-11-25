using System.Linq;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Cálculo de estadísticas de una serie de probes:
/// - media/percentiles solo sobre muestras exitosas;
/// - pérdida = fallidas / total;
/// - jitter = media de diferencias absolutas entre RTT consecutivos exitosos (RFC 3550 simplificado);
/// - estabilidad 0-100 combinando pérdida, jitter y tendencia.
/// </summary>
public static class ProbeSummaryCalculator
{
    public static ProbeSummary Compute(string target, ProbeProtocol protocol, IReadOnlyList<ProbeSample> samples)
    {
        var total = samples.Count;
        if (total == 0)
            return ProbeSummary.Unreachable(target, 0, "No se ejecutaron probes.");

        var successes = samples.Where(s => s.Success).Select(s => s.RttMs).OrderBy(v => v).ToList();
        var failures = total - successes.Count;
        var summary = new ProbeSummary
        {
            Target = target,
            ProtocolUsed = protocol,
            TotalProbes = total,
            FailedProbes = failures,
            LossRatio = (double)failures / total,
            SuccessCount = successes.Count
        };

        if (successes.Count == 0)
        {
            summary.Notes = "Sin respuesta: el endpoint no responde a este protocolo o está bloqueado.";
            return summary;
        }

        summary.MeanMs = successes.Average();
        summary.MinMs = successes[0];
        summary.MaxMs = successes[^1];
        summary.P50Ms = Percentile(successes, 0.50);
        summary.P95Ms = Percentile(successes, 0.95);
        summary.P99Ms = Percentile(successes, 0.99);
        summary.JitterMs = ComputeJitter(samples);
        summary.StabilityScore = ComputeStability(summary.LossRatio, summary.JitterMs, successes);
        summary.IcmpUnreliable = protocol == ProbeProtocol.Icmp &&
                                 successes.Count > 0 && failures > total / 2;
        if (summary.IcmpUnreliable)
            summary.Notes = "ICMP con alta pérdida: puede estar limitado por el servidor o la red; las métricas pueden no ser fiables.";
        else if (protocol != ProbeProtocol.Icmp)
            summary.Notes = $"Medido con {protocol} (TCP connect mide el handshake; el RTT de juego puede diferir).";

        return summary;
    }

    /// <summary>Percentil por interpolación lineal sobre lista ya ordenada.</summary>
    public static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return double.NaN;
        if (sorted.Count == 1) return sorted[0];
        var idx = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(idx);
        var hi = (int)Math.Ceiling(idx);
        return Math.Abs(lo - hi) < 0.5 ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    public static double ComputeJitter(IReadOnlyList<ProbeSample> samples)
    {
        var consecutive = samples.Where(s => s.Success).Select(s => s.RttMs).ToList();
        if (consecutive.Count < 2) return 0;
        var diffs = new List<double>();
        for (var i = 1; i < consecutive.Count; i++)
            diffs.Add(Math.Abs(consecutive[i] - consecutive[i - 1]));
        return diffs.Average();
    }

    /// <summary>
    /// Estabilidad 0-100: base 100, resta por pérdida (fuerte) y jitter (moderado),
    /// y por tendencia creciente de latencia (degradación).
    /// </summary>
    public static double ComputeStability(double lossRatio, double jitterMs, List<double> sortedSuccesses)
    {
        var score = 100.0;
        score -= lossRatio * 100.0 * 9.0;                    // 10 % de pérdida => -90
        score -= Math.Min(jitterMs, 50.0) * 0.8;             // jitter hasta 50 ms => hasta -40
        var trend = ComputeTrendPerMinute(sortedSuccesses);
        score -= Math.Min(Math.Max(trend, 0), 15.0) * 0.6;   // tendencia al alza penaliza poco
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>Tendencia de latencia: regresión lineal simple sobre la secuencia (ms por minuto).</summary>
    public static double ComputeTrendPerMinute(List<double> valuesInOrder)
    {
        var n = valuesInOrder.Count;
        if (n < 5) return 0;
        var xs = Enumerable.Range(0, n).Select(i => (double)i).ToList();
        var meanX = xs.Average();
        var meanY = valuesInOrder.Average();
        var cov = 0.0;
        var varX = 0.0;
        for (var i = 0; i < n; i++)
        {
            cov += (xs[i] - meanX) * (valuesInOrder[i] - meanY);
            varX += Math.Pow(xs[i] - meanX, 2);
        }
        if (varX == 0) return 0;
        var slopePerSample = cov / varX;
        return slopePerSample * 60; // aproximación: 1 muestra ≈ 1 s
    }
}
