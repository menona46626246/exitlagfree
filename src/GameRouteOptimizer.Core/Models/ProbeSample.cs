namespace GameRouteOptimizer.Core.Models;

/// <summary>Una muestra individual de probe (una medición).</summary>
public readonly record struct ProbeSample(
    bool Success,
    double RttMs,
    DateTimeOffset Timestamp,
    string Protocol,
    string? Detail = null)
{
    public static ProbeSample Fail(string protocol, string detail) =>
        new(false, 0, DateTimeOffset.UtcNow, protocol, detail);
}

/// <summary>
/// Resumen estadístico de una serie de probes. Calculado por <see cref="Probing.ProbeSummaryCalculator"/>.
/// </summary>
public sealed class ProbeSummary
{
    public string Target { get; set; } = string.Empty;

    public ProbeProtocol ProtocolUsed { get; set; }

    public int TotalProbes { get; set; }

    public int FailedProbes { get; set; }

    /// <summary>Pérdida estimada 0-1.</summary>
    public double LossRatio { get; set; }

    /// <summary>Media de RTT sobre probes exitosas (ms).</summary>
    public double MeanMs { get; set; }

    public double P50Ms { get; set; }

    public double P95Ms { get; set; }

    public double P99Ms { get; set; }

    public double MinMs { get; set; }

    public double MaxMs { get; set; }

    /// <summary>Jitter = media de diferencias absolutas consecutivas (ms).</summary>
    public double JitterMs { get; set; }

    /// <summary>Estabilidad 0-100 (mayor = más estable), derivada de pérdida, jitter y tendencia.</summary>
    public double StabilityScore { get; set; }

    /// <summary>Nº de muestras exitosas usadas para las estadísticas.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Indica si ICMP no respondió (bloqueado/filtrado) y se usó un protocolo alternativo.</summary>
    public bool IcmpUnreliable { get; set; }

    /// <summary>Notas para el usuario (en español).</summary>
    public string Notes { get; set; } = string.Empty;

    public bool IsReachable => SuccessCount > 0;

    public static ProbeSummary Unreachable(string target, int total, string note) => new()
    {
        Target = target,
        TotalProbes = total,
        FailedProbes = total,
        LossRatio = 1.0,
        StabilityScore = 0,
        Notes = note
    };
}
