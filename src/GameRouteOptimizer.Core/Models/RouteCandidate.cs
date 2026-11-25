namespace GameRouteOptimizer.Core.Models;

/// <summary>Candidato de ruta: la ruta directa o un relay.</summary>
public sealed class RouteCandidate
{
    public const string DirectId = "direct";

    public string CandidateId { get; set; } = DirectId;

    public string DisplayName { get; set; } = "Ruta directa";

    /// <summary>Es la ruta directa (sin relay).</summary>
    public bool IsDirect => CandidateId == DirectId;

    /// <summary>Relay asociado (null para ruta directa).</summary>
    public RelayNode? Relay { get; set; }

    /// <summary>Latencia medida o estimada total usuario→destino (ms).</summary>
    public double LatencyMs { get; set; }

    /// <summary>Pérdida medida 0-1.</summary>
    public double LossRatio { get; set; }

    /// <summary>Jitter (ms).</summary>
    public double JitterMs { get; set; }

    /// <summary>Estabilidad 0-100.</summary>
    public double StabilityScore { get; set; }

    /// <summary>Nº de saltos estimados (si se conoce).</summary>
    public int? HopCount { get; set; }

    /// <summary>Muestras exitosas que sustentan la medición.</summary>
    public int SampleCount { get; set; }

    /// <summary>Candidato inalcanzable (no se pudo medir).</summary>
    public bool Unreachable { get; set; }

    /// <summary>La medición relay→juego es desconocida (estimación no disponible).</summary>
    public bool RelayToGameUnknown { get; set; }

    /// <summary>Componente medida usuario→relay (ms), si aplica.</summary>
    public double? UserToRelayMs { get; set; }

    /// <summary>Componente relay→juego (ms) medida/estimada, si aplica.</summary>
    public double? RelayToGameComponentMs { get; set; }

    public bool IcmpUnreliable { get; set; }
}

/// <summary>Score calculado para un candidato.</summary>
public sealed class RouteScore
{
    public string CandidateId { get; set; } = string.Empty;

    /// <summary>Score 0-100 (0 = excluido/inalcanzable).</summary>
    public double Value { get; set; }

    /// <summary>Razón textual en español del resultado.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>Confianza 0-1 según muestras, fiabilidad y datos desconocidos.</summary>
    public double Confidence { get; set; }

    public bool Excluded { get; set; }

    public string ExclusionReason { get; set; } = string.Empty;
}

/// <summary>Recomendación final tras puntuar todos los candidatos.</summary>
public sealed class RouteRecommendation
{
    public RouteCandidate? Best { get; set; }

    public RouteScore BestScore { get; set; } = new();

    public List<RouteCandidate> Candidates { get; set; } = new();

    public List<RouteScore> AllScores { get; set; } = new();

    /// <summary>Texto simple para el usuario: "mejor latencia", "menos pérdida", etc.</summary>
    public string SimpleExplanation { get; set; } = string.Empty;

    /// <summary>Confianza global de la recomendación 0-1.</summary>
    public double Confidence { get; set; }

    /// <summary>Recomendación de usar ruta directa (no conviene relay).</summary>
    public bool RecommendDirect { get; set; }
}
