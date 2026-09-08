using System.Text.Json.Serialization;

namespace GameRouteOptimizer.Core.Models;

/// <summary>Resumen estadístico de una serie de probes hacia un destino.</summary>
public sealed class ProbeSummary
{
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }

    /// <summary>Etiqueta legible del destino medido.</summary>
    public string TargetLabel { get; set; } = string.Empty;

    public ProbeKind Kind { get; set; }

    public int Attempts { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }

    public double? MinMs { get; set; }
    public double? AvgMs { get; set; }
    public double? MaxMs { get; set; }
    public double? P95Ms { get; set; }
    public double? P99Ms { get; set; }
    public double? StdDevMs { get; set; }
    public double? JitterMs { get; set; }
    public double? LossPercent { get; set; }

    /// <summary>false cuando la prueba con ICMP no es fiable (bloqueado/denegado).</summary>
    public bool IcmpReliable { get; set; } = true;

    /// <summary>Razón de no disponibilidad cuando no hubo medición útil.</summary>
    public string? UnavailableReason { get; set; }

    /// <summary>Nota adicional (p. ej. "ICMP bloqueado; se usó TCP").</summary>
    public string? Note { get; set; }

    [JsonIgnore]
    public bool Usable => Successes > 0 && AvgMs.HasValue && AvgMs > 0;

    [JsonIgnore]
    public bool HasData => Attempts > 0;

    public override string ToString()
    {
        if (!HasData)
        {
            return $"{TargetLabel}: sin datos";
        }

        var loss = LossPercent.HasValue ? $"{LossPercent.Value:F1}%" : "?";
        var avg = AvgMs.HasValue ? $"{AvgMs.Value:F1} ms" : "?";
        var jit = JitterMs.HasValue ? $"{JitterMs.Value:F1} ms" : "?";
        var p95 = P95Ms.HasValue ? $"{P95Ms.Value:F1} ms" : "?";
        return $"{TargetLabel}: {avg} media | p95 {p95} | pérdida {loss} | jitter {jit} | {Successes}/{Attempts} ok";
    }
}

/// <summary>Resultado de una única prueba.</summary>
public sealed class ProbeAttemptResult
{
    public bool Success { get; init; }
    public double? RttMs { get; init; }
    public ProbeFailureReason Failure { get; init; }
    public string? Detail { get; init; }
}

public enum ProbeFailureReason
{
    None,
    Timeout,
    IcmpBlocked,
    DnsFailure,
    Unreachable,
    Refused,
    ProtocolNotSupported,
    Cancelled,
    Error,
}

/// <summary>Evento de una sesión (historial).</summary>
public sealed class SessionEvent
{
    public DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
    public SessionEventCategory Category { get; set; } = SessionEventCategory.Info;
    public string Message { get; set; } = string.Empty;

    /// <summary>Estado del programa en el momento del evento (opcional).</summary>
    public string? State { get; set; }
}

/// <summary>Registro de una sesión de optimización/diagnóstico con su historial.</summary>
public sealed class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedUtc { get; set; }

    public string? GameProfileId { get; set; }
    public string GameProfileName { get; set; } = string.Empty;
    public string TargetDisplay { get; set; } = string.Empty;

    public RouteMode Mode { get; set; } = RouteMode.Direct;
    public string? RelayId { get; set; }
    public string? RelayName { get; set; }

    /// <summary>Métricas por la ruta directa (antes/durante).</summary>
    public ProbeSummary? DirectMetrics { get; set; }

    /// <summary>Métricas efectivas con el túnel activo (cuando se midieron).</summary>
    public ProbeSummary? OptimizedMetrics { get; set; }

    /// <summary>Mejora estimada (ms, positivo = el túnel fue mejor; puede ser negativo).</summary>
    public double? EstimatedImprovementMs { get; set; }

    public string? EndedReason { get; set; }

    /// <summary>Eventos en orden cronológico (acotado a los últimos N para no crecer sin límite).</summary>
    public List<SessionEvent> Events { get; set; } = new();

    public const int MaxEvents = 4000;

    public void AddEvent(SessionEventCategory category, string message, string? state = null)
    {
        Events.Add(new SessionEvent { Category = category, Message = message, State = state });
        if (Events.Count > MaxEvents)
        {
            Events.RemoveRange(0, Events.Count - MaxEvents);
        }
    }
}

/// <summary>Entrada del registro de aprendizaje relay→región (tramo final medido con túnel activo).</summary>
public sealed class RouteLearning
{
    public string RelayId { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;

    /// <summary>Media estimada del tramo relay→servidor (ms), aprendida de sesiones reales.</summary>
    public double TailAvgMs { get; set; }

    public int Samples { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
