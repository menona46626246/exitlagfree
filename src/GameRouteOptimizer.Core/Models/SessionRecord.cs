namespace GameRouteOptimizer.Core.Models;

/// <summary>Evento de sesión (cambios de ruta, caídas de túnel, errores…).</summary>
public sealed class SessionEvent
{
    public long Id { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public EventSeverity Severity { get; set; } = EventSeverity.Info;

    public EventCategory Category { get; set; } = EventCategory.System;

    /// <summary>Texto legible en español.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Datos estructurales adicionales (JSON, opcional).</summary>
    public string? Data { get; set; }
}

/// <summary>Registro de una sesión de optimización (antes/después).</summary>
public sealed class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ProfileId { get; set; } = string.Empty;

    public string ProfileName { get; set; } = string.Empty;

    public string GameServerTarget { get; set; } = string.Empty;

    /// <summary>"Manual" | "Automatic" | "Diagnostic".</summary>
    public string Mode { get; set; } = "Manual";

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndedUtc { get; set; }

    public string FinalStatus { get; set; } = "Running";

    /// <summary>Métricas de la ruta directa antes de optimizar (JSON).</summary>
    public ProbeSummary? DirectBefore { get; set; }

    /// <summary>Métricas con la ruta optimizada (JSON).</summary>
    public ProbeSummary? Optimized { get; set; }

    /// <summary>Candidato elegido (id).</summary>
    public string SelectedCandidateId { get; set; } = string.Empty;

    public string SelectedRelayName { get; set; } = string.Empty;

    /// <summary>Nº de cambios de ruta durante la sesión.</summary>
    public int RouteSwitchCount { get; set; }

    /// <summary>Nº de caídas del túnel durante la sesión.</summary>
    public int TunnelDropCount { get; set; }

    /// <summary>Error estimado de mejora (ms) = directo - optimizado (positivo = mejora).</summary>
    public double? ImprovementMs { get; set; }

    public List<SessionEvent> Events { get; set; } = new();

    /// <summary>Mejora estimada formateada (ej. "-18.4 ms (−37 %)").</summary>
    public string ImprovementText
    {
        get
        {
            if (DirectBefore is { IsReachable: true } d && Optimized is { IsReachable: true } o)
            {
                var delta = d.MeanMs - o.MeanMs;
                var pct = d.MeanMs > 0 ? Math.Abs(delta) / d.MeanMs * 100 : 0;
                return delta >= 0
                    ? $"{delta:F1} ms más rápido ({pct:F0} %)"
                    : $"{Math.Abs(delta):F1} ms más lento ({pct:F0} %)";
            }
            return "Sin datos comparables";
        }
    }
}
