using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Scoring;
using Xunit;

namespace GameRouteOptimizer.Tests;

/// <summary>Tests del motor de scoring: mejor ruta, penalizaciones y exclusiones.</summary>
public class ScoreEngineTests
{
    private static RouteCandidate Direct(double ms = 50, double loss = 0, double jitter = 2, int samples = 20) =>
        new()
        {
            CandidateId = RouteCandidate.DirectId,
            DisplayName = "Directa",
            LatencyMs = ms,
            LossRatio = loss,
            JitterMs = jitter,
            StabilityScore = 90,
            SampleCount = samples
        };

    private static RouteCandidate Relay(string id, string name, double ms = 40, double loss = 0, double jitter = 2,
        int samples = 20, bool unknownPath = false, double? load = null)
        => new()
        {
            CandidateId = id,
            DisplayName = name,
            Relay = new RelayNode { Id = id, Name = name, LoadPercent = load },
            LatencyMs = ms,
            LossRatio = loss,
            JitterMs = jitter,
            StabilityScore = 90,
            SampleCount = samples,
            RelayToGameUnknown = unknownPath
        };

    [Fact]
    public void Scoring_EligeLaMejorRutaPorLatencia()
    {
        var engine = new ScoreEngine();
        var rec = engine.EvaluateAll(new[] { Direct(ms: 60), Relay("r1", "Relay rápido", ms: 35) });

        Assert.NotNull(rec.Best);
        Assert.Equal("r1", rec.Best!.CandidateId);
        Assert.False(rec.RecommendDirect);
        Assert.Contains("latencia", rec.SimpleExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scoring_MantieneDirectaSiEsMejor()
    {
        var engine = new ScoreEngine();
        var rec = engine.EvaluateAll(new[] { Direct(ms: 30), Relay("r1", "Relay lento", ms: 80) });

        Assert.True(rec.RecommendDirect);
        Assert.Equal(RouteCandidate.DirectId, rec.Best!.CandidateId);
    }

    [Fact]
    public void Scoring_PenalizaPerdidaExtrema_YLaExcluye()
    {
        var engine = new ScoreEngine(extremeLossThreshold: 0.20);
        var rec = engine.EvaluateAll(new[] { Direct(ms: 60), Relay("r1", "Con pérdida", ms: 30, loss: 0.35) });

        var excluded = rec.AllScores.First(s => s.CandidateId == "r1");
        Assert.True(excluded.Excluded);
        Assert.Contains("Pérdida extrema", excluded.ExclusionReason);
        Assert.Equal(RouteCandidate.DirectId, rec.Best!.CandidateId);
    }

    [Fact]
    public void Scoring_ExcluyeCandidatosInalcanzables()
    {
        var engine = new ScoreEngine();
        var dead = Relay("r2", "Muerto", ms: double.MaxValue);
        dead.Unreachable = true;
        dead.SampleCount = 0;
        var rec = engine.EvaluateAll(new[] { Direct(ms: 60), dead });

        var deadScore = rec.AllScores.First(s => s.CandidateId == "r2");
        Assert.True(deadScore.Excluded);
        Assert.Contains("Inalcanzable", deadScore.ExclusionReason);
        Assert.NotEqual("r2", rec.Best!.CandidateId);
    }

    [Fact]
    public void Scoring_PenalizaRelayConTramoDesconocido()
    {
        var engine = new ScoreEngine();
        // Mismo tiempo total: el desconocido debe perder por penalización.
        var known = Relay("r1", "Conocido", ms: 40, unknownPath: false);
        var unknown = Relay("r2", "Desconocido", ms: 40, unknownPath: true);
        var rec = engine.EvaluateAll(new[] { Direct(ms: 60), known, unknown });

        var knownScore = rec.AllScores.First(s => s.CandidateId == "r1");
        var unknownScore = rec.AllScores.First(s => s.CandidateId == "r2");
        Assert.True(knownScore.Value > unknownScore.Value);
    }

    [Fact]
    public void Scoring_PenalizaRelaySaturado()
    {
        var engine = new ScoreEngine();
        var normal = Relay("r1", "Normal", ms: 40);
        var loaded = Relay("r2", "Saturado", ms: 40, load: 95);
        var rec = engine.EvaluateAll(new[] { Direct(ms: 60), normal, loaded });

        var normalScore = rec.AllScores.First(s => s.CandidateId == "r1");
        var loadedScore = rec.AllScores.First(s => s.CandidateId == "r2");
        Assert.True(normalScore.Value > loadedScore.Value);
        Assert.Contains("carga", loadedScore.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scoring_MenosPerdidaGanaConLatenciaSimilar()
    {
        var engine = new ScoreEngine();
        var rec = engine.EvaluateAll(new[]
        {
            Direct(ms: 50, loss: 0.06, jitter: 3),
            Relay("r1", "Estable", ms: 52, loss: 0.0, jitter: 2)
        });

        Assert.Equal("r1", rec.Best!.CandidateId);
        Assert.Contains("pérdida", rec.SimpleExplanation, StringComparison.OrdinalIgnoreCase);
    }
}
