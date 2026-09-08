using System.Text.Json.Serialization;

namespace GameRouteOptimizer.Core.Models;

/// <summary>Mediciones de un candidato de ruta (directa o vía relay) listas para puntuar.</summary>
public sealed class CandidateMeasurement
{
    public string CandidateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public RouteCandidateKind Kind { get; set; }

    /// <summary>Id del relay cuando Kind == Relay.</summary>
    public string? RelayId { get; set; }

    /// <summary>Latencia medida hacia el endpoint (directa: al juego; relay: al relay).</summary>
    public ProbeSummary? Measured { get; set; }

    /// <summary>Tramo relay→servidor estimado (ms). Solo aplica a relays con datos aprendidos.</summary>
    public double? LearnedTailMs { get; set; }

    /// <summary>Número de saltos aproximado (opcional; directo del traceroute).</summary>
    public int? Hops { get; set; }

    /// <summary>Carga estimada 0-100 (opcional).</summary>
    public double? LoadPercent { get; set; }

    /// <summary>Descripción de costo (opcional, informativo).</summary>
    public string? CostNote { get; set; }

    /// <summary>Disponibilidad histórica 0-1 (opcional).</summary>
    public double? Availability { get; set; }

    /// <summary>Prioridad declarada por el usuario (menor = mejor) para desempates.</summary>
    public int Priority { get; set; }

    /// <summary>Si el relay está bloqueado para el juego actual.</summary>
    public bool BlockedForGame { get; set; }
}

/// <summary>Candidato puntuado con su explicación.</summary>
public sealed class ScoredCandidate
{
    public string CandidateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public RouteCandidateKind Kind { get; set; }
    public string? RelayId { get; set; }

    /// <summary>Puntuación compuesta en "ms equivalentes": menor = mejor.</summary>
    public double ScoreMs { get; set; }

    public double? EffectiveLatencyMs { get; set; }
    public double? LossPercent { get; set; }
    public double? JitterMs { get; set; }

    /// <summary>0-1: cuánta confianza hay en esta medición/puntuación.</summary>
    public double Confidence { get; set; }

    public bool Excluded { get; set; }
    public string? ExcludeReason { get; set; }

    /// <summary>Razones legibles (p. ej. "mejor latencia", "menos pérdida", "ruta más estable").</summary>
    public List<string> Strengths { get; set; } = new();

    public string[] Reasons => Strengths.ToArray();
}

/// <summary>Resultado completo de la evaluación de rutas.</summary>
public sealed class ScoringOutput
{
    public List<ScoredCandidate> Ranked { get; set; } = new();

    /// <summary>Id del candidato ganador o null si ninguno es utilizable.</summary>
    public string? WinnerId { get; set; }

    /// <summary>Id del candidato actualmente en uso (directo o relay activo).</summary>
    public string? CurrentId { get; set; }

    /// <summary>true si el ganador es distinto del actual Y la mejora supera la histéresis.</summary>
    public bool ChangeRecommended { get; set; }

    /// <summary>Explicación simple en español para la UI.</summary>
    public string ExplanationEs { get; set; } = string.Empty;

    public double Confidence { get; set; }
}
