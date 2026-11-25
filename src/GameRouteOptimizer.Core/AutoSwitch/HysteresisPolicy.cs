using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.AutoSwitch;

/// <summary>Contexto de decisión para cambiar de ruta.</summary>
public sealed class AutoSwitchContext
{
    public RouteCandidate? CurrentCandidate { get; set; }

    public RouteCandidate? BestCandidate { get; set; }

    public RouteScore? CurrentScore { get; set; }

    public RouteScore? BestScore { get; set; }

    /// <summary>Túnel caído / no conectable.</summary>
    public bool TunnelDown { get; set; }

    /// <summary>Segundos desde el último cambio de ruta.</summary>
    public double SecondsSinceLastSwitch { get; set; } = double.MaxValue;

    /// <summary>Cambios realizados en la última hora.</summary>
    public int SwitchesLastHour { get; set; }

    /// <summary>Tiempo (s) que el mejor candidato lleva siendo consistentemente mejor.</summary>
    public double SecondsBestConsistent { get; set; }
}

/// <summary>Resultado de la evaluación con razón en español.</summary>
public sealed record AutoSwitchResult(AutoSwitchDecision Decision, string Reason);

/// <summary>Parámetros anti-flapping.</summary>
public sealed class HysteresisSettings
{
    public double MinImprovementScore { get; set; } = 8;
    public double MinImprovementLatencyMs { get; set; } = 10;
    public int SwitchCooldownSeconds { get; set; } = 300;
    public int MinSamplesToSwitch { get; set; } = 5;
    public int MaxSwitchesPerHour { get; set; } = 4;
    public int StabilityWindowSeconds { get; set; } = 30;
    public bool FailbackOnTunnelDown { get; set; } = true;
    public bool FailbackOnWorseQuality { get; set; } = true;

    public static HysteresisSettings FromSettings(AppSettings s) => new()
    {
        MinImprovementScore = s.MinImprovementScore,
        MinImprovementLatencyMs = s.MinImprovementLatencyMs,
        SwitchCooldownSeconds = s.SwitchCooldownSeconds,
        MinSamplesToSwitch = s.MinSamplesToSwitch,
        MaxSwitchesPerHour = s.MaxSwitchesPerHour,
        FailbackOnTunnelDown = s.FailbackOnTunnelDown,
        FailbackOnWorseQuality = s.FailbackOnWorseQuality
    };
}

/// <summary>
/// Política de auto-switch con histéresis, cooldown y límites. Evita cambios constantes:
/// solo cambia si la mejora es clara, estable y no se excedió el cooldown o el límite horario.
/// </summary>
public sealed class HysteresisPolicy
{
    private readonly HysteresisSettings _settings;

    public HysteresisPolicy(HysteresisSettings? settings = null)
    {
        _settings = settings ?? new HysteresisSettings();
    }

    public AutoSwitchResult Evaluate(AutoSwitchContext ctx)
    {
        // 1) Túnel caído → failback inmediato a directa (prioridad máxima).
        if (ctx.TunnelDown)
        {
            return _settings.FailbackOnTunnelDown
                ? new AutoSwitchResult(AutoSwitchDecision.Failback, "El túnel cayó: regreso a la ruta directa.")
                : new AutoSwitchResult(AutoSwitchDecision.NoChange, "El túnel cayó, pero el failback automático está desactivado.");
        }

        if (ctx.CurrentCandidate is null || ctx.BestCandidate is null || ctx.BestScore is null)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange, "Sin datos suficientes para decidir.");

        // 2) Mismo candidato → nada que hacer.
        if (ctx.CurrentCandidate.CandidateId == ctx.BestCandidate.CandidateId)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange, "Ya se está usando la mejor ruta.");

        // 3) Mejor candidato excluido (p. ej. pérdida extrema) → no cambiar.
        if (ctx.BestScore.Excluded)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange, "La ruta candidata está excluida por mala calidad.");

        // 4) Cooldown.
        if (ctx.SecondsSinceLastSwitch < _settings.SwitchCooldownSeconds)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange,
                $"En cooldown: faltan {_settings.SwitchCooldownSeconds - ctx.SecondsSinceLastSwitch:0} s para poder cambiar de ruta.");

        // 5) Límite horario.
        if (ctx.SwitchesLastHour >= _settings.MaxSwitchesPerHour)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange, "Límite de cambios por hora alcanzado (anti-flapping).");

        // 6) Muestras mínimas.
        if (ctx.BestCandidate.SampleCount < _settings.MinSamplesToSwitch)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange, "Pocas muestras del candidato aún; esperando datos estables.");

        // 7) Consistencia temporal del mejor candidato.
        if (ctx.SecondsBestConsistent < _settings.StabilityWindowSeconds)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange,
                $"El candidato lleva {ctx.SecondsBestConsistent:0} s siendo mejor; se exigen {_settings.StabilityWindowSeconds} s estables.");

        // 8) Mejora mínima (score o latencia).
        var scoreImprovement = ctx.CurrentScore!.Value - ctx.BestScore.Value;
        var latencyImprovement = ctx.CurrentCandidate.LatencyMs - ctx.BestCandidate.LatencyMs;
        var isBetterScore = scoreImprovement >= _settings.MinImprovementScore;
        var isBetterLatency = latencyImprovement >= _settings.MinImprovementLatencyMs;

        if (!isBetterScore && !isBetterLatency)
            return new AutoSwitchResult(AutoSwitchDecision.NoChange,
                $"Mejora insuficiente (Δscore {scoreImprovement:0.0} / Δms {latencyImprovement:0.0}); no conviene cambiar.");

        // 9) Calidad empeorada respecto a directa → failback.
        if (_settings.FailbackOnWorseQuality && ctx.BestCandidate.IsDirect &&
            ctx.CurrentCandidate.LossRatio > 0.05 && ctx.BestCandidate.LossRatio < ctx.CurrentCandidate.LossRatio * 0.5)
            return new AutoSwitchResult(AutoSwitchDecision.Failback,
                $"El túnel empeora la conexión (pérdida {ctx.CurrentCandidate.LossRatio:P1} vs {ctx.BestCandidate.LossRatio:P1}): regreso a directa.");

        return new AutoSwitchResult(AutoSwitchDecision.Switch,
            $"Mejora clara: Δscore {scoreImprovement:0.0}, Δms {latencyImprovement:0.0}. Cambiando a {ctx.BestCandidate.DisplayName}.");
    }
}
