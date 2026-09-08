using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Services;

/// <summary>Decisión de cambio de ruta del controlador automático.</summary>
public enum SwitchDecisionKind
{
    NoChange,
    SwitchToWinner,
    WaitStability,
    WaitCooldown,
}

public sealed record SwitchDecision(SwitchDecisionKind Kind, string ReasonEs, double WaitSeconds = 0);

/// <summary>
/// Política de cambio automático de ruta con histéresis, ventana de estabilidad y cooldown.
/// Lógica pura, sin reloj de red: recibe el instante actual para poder testearse.
/// </summary>
public static class AutoSwitchPolicy
{
    /// <summary>
    /// Decide si conviene cambiar a la ruta ganadora.
    /// </summary>
    /// <param name="recommendation">Salida del ScoreEngine (una pasada de medición).</param>
    /// <param name="now">Instante actual.</param>
    /// <param name="lastSwitchUtc">Último cambio de ruta (null = nunca).</param>
    /// <param name="sustainedTicks">Veces consecutivas que la recomendación pide el mismo cambio.</param>
    /// <param name="settings">Configuración de auto-switch.</param>
    /// <param name="healthCheckIntervalSeconds">Cada cuántos segundos se evalúa (para la ventana de estabilidad).</param>
    public static SwitchDecision ShouldSwitch(
        ScoringOutput recommendation,
        DateTimeOffset now,
        DateTimeOffset? lastSwitchUtc,
        int sustainedTicks,
        AutoSwitchSettings settings,
        double healthCheckIntervalSeconds = 10)
    {
        if (!settings.Enabled)
        {
            return new SwitchDecision(SwitchDecisionKind.NoChange, "El cambio automático está desactivado.");
        }

        if (recommendation.WinnerId is null || recommendation.WinnerId == recommendation.CurrentId)
        {
            return new SwitchDecision(SwitchDecisionKind.NoChange, "La ruta actual ya es la mejor.");
        }

        if (!recommendation.ChangeRecommended)
        {
            return new SwitchDecision(SwitchDecisionKind.NoChange, recommendation.ExplanationEs);
        }

        if (lastSwitchUtc is { } last &&
            (now - last).TotalSeconds < settings.CooldownSeconds)
        {
            var wait = settings.CooldownSeconds - (now - last).TotalSeconds;
            return new SwitchDecision(SwitchDecisionKind.WaitCooldown,
                $"Cooldown tras el último cambio: espera {Math.Ceiling(wait)} s más.", wait);
        }

        var stabilityTicks = Math.Max(1,
            (int)Math.Ceiling(settings.StabilityWindowSeconds / Math.Max(1, healthCheckIntervalSeconds)));
        if (sustainedTicks < stabilityTicks)
        {
            return new SwitchDecision(SwitchDecisionKind.WaitStability,
                $"La mejora debe mantenerse {settings.StabilityWindowSeconds} s antes de cambiar " +
                $"(confirmación {sustainedTicks}/{stabilityTicks}).");
        }

        return new SwitchDecision(SwitchDecisionKind.SwitchToWinner,
            $"Cambio a la mejor ruta: {recommendation.ExplanationEs}");
    }
}

/// <summary>Decisión de failback (volver a ruta directa).</summary>
public enum FailbackKind
{
    None,
    TunnelDown,
    TunnelWorse,
}

public sealed record FailbackDecision(FailbackKind Kind, string ReasonEs);

/// <summary>Política de failback: cuándo abandonar el túnel y volver a la ruta directa.</summary>
public static class FailbackPolicy
{
    /// <summary>
    /// Evalúa el failback.
    /// </summary>
    /// <param name="tunnelHealthy">¿El túnel responde (handshake/estado)?</param>
    /// <param name="targetViaTunnelOk">¿El destino del juego responde a través del túnel?</param>
    /// <param name="consecutiveTunnelFailures">Fallos consecutivos de salud del túnel.</param>
    /// <param name="consecutiveTargetFailures">Fallos consecutivos del destino vía túnel.</param>
    /// <param name="directSummary">Métrica directa reciente (para comparar si el túnel empeora).</param>
    /// <param name="optimizedSummary">Métrica vía túnel reciente.</param>
    /// <param name="settings">Configuración.</param>
    /// <param name="now">Instante actual (para registrar cuándo se midió).</param>
    public static FailbackDecision ShouldFailback(
        bool tunnelHealthy,
        bool targetViaTunnelOk,
        int consecutiveTunnelFailures,
        int consecutiveTargetFailures,
        ProbeSummary? directSummary,
        ProbeSummary? optimizedSummary,
        AutoSwitchSettings settings,
        DateTimeOffset now)
    {
        if (settings.AutoFailback == false)
        {
            // Aun con auto-failback apagado, un túnel caído no puede quedarse "activo".
            return new FailbackDecision(
                consecutiveTunnelFailures >= 1 || consecutiveTargetFailures >= 1
                    ? FailbackKind.TunnelDown
                    : FailbackKind.None,
                consecutiveTunnelFailures >= 1 || consecutiveTargetFailures >= 1
                    ? "El túnel no responde; se requiere failback por seguridad."
                    : "Auto-failback desactivado.");
        }

        if (!tunnelHealthy &&
            consecutiveTunnelFailures >= settings.FailbackAfterConsecutiveFailures)
        {
            return new FailbackDecision(FailbackKind.TunnelDown,
                $"El túnel dejó de responder ({consecutiveTunnelFailures} comprobaciones fallidas).");
        }

        if (!targetViaTunnelOk &&
            consecutiveTargetFailures >= settings.FailbackAfterConsecutiveFailures)
        {
            return new FailbackDecision(FailbackKind.TunnelDown,
                $"El servidor del juego no responde a través del túnel ({consecutiveTargetFailures} fallos).");
        }

        if (directSummary?.AvgMs is { } direct &&
            optimizedSummary?.AvgMs is { } optimized &&
            direct > 0)
        {
            // El túnel empeora la conexión de forma clara y sostenida (margen de histéresis).
            var worseBy = optimized - direct;
            var hysteresisMs = Math.Max(settings.MinImprovementMs * 2.0, 15.0);
            if (worseBy > hysteresisMs && optimized > direct * 1.25)
            {
                return new FailbackDecision(FailbackKind.TunnelWorse,
                    $"El túnel empeora la latencia en {worseBy:F0} ms frente a la ruta directa.");
            }
        }

        return new FailbackDecision(FailbackKind.None, string.Empty);
    }
}
