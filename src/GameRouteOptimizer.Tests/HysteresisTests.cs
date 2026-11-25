using GameRouteOptimizer.Core.AutoSwitch;
using GameRouteOptimizer.Core.Models;
using Xunit;

namespace GameRouteOptimizer.Tests;

/// <summary>Tests de histéresis: anti-flapping, cooldown, límites y failback.</summary>
public class HysteresisTests
{
    private static RouteCandidate Candidate(string id, double ms, double loss = 0, int samples = 20) =>
        new()
        {
            CandidateId = id,
            DisplayName = id,
            LatencyMs = ms,
            LossRatio = loss,
            JitterMs = 2,
            StabilityScore = 90,
            SampleCount = samples
        };

    private static HysteresisPolicy Policy(int cooldown = 300, int maxPerHour = 4) =>
        new(new HysteresisSettings
        {
            MinImprovementScore = 8,
            MinImprovementLatencyMs = 10,
            SwitchCooldownSeconds = cooldown,
            MinSamplesToSwitch = 5,
            MaxSwitchesPerHour = maxPerHour,
            StabilityWindowSeconds = 30,
            FailbackOnTunnelDown = true
        });

    [Fact]
    public void AutoSwitch_EvitaFlappingPorMejoraMinima()
    {
        var policy = Policy();
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("direct", 50),
            BestCandidate = Candidate("r1", 48), // solo 2 ms mejor: bajo el umbral de 10
            CurrentScore = new RouteScore { Value = 60 },
            BestScore = new RouteScore { Value = 63 },
            SecondsSinceLastSwitch = double.MaxValue,
            SecondsBestConsistent = 120
        });

        Assert.Equal(AutoSwitchDecision.NoChange, result.Decision);
        Assert.Contains("Mejora insuficiente", result.Reason);
    }

    [Fact]
    public void AutoSwitch_RespetaCooldown()
    {
        var policy = Policy(cooldown: 300);
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("direct", 80),
            BestCandidate = Candidate("r1", 30), // mejora clara
            CurrentScore = new RouteScore { Value = 50 },
            BestScore = new RouteScore { Value = 90 },
            SecondsSinceLastSwitch = 60, // hace 60 s hubo cambio: cooldown activo
            SecondsBestConsistent = 120
        });

        Assert.Equal(AutoSwitchDecision.NoChange, result.Decision);
        Assert.Contains("cooldown", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutoSwitch_CambiaConMejoraClaraYEstable()
    {
        var policy = Policy();
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("direct", 80),
            BestCandidate = Candidate("r1", 30),
            CurrentScore = new RouteScore { Value = 50 },
            BestScore = new RouteScore { Value = 90 },
            SecondsSinceLastSwitch = 4000,
            SecondsBestConsistent = 60
        });

        Assert.Equal(AutoSwitchDecision.Switch, result.Decision);
    }

    [Fact]
    public void AutoSwitch_NoCambiaSiLosDatosSonInestables()
    {
        var policy = Policy();
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("direct", 80),
            BestCandidate = Candidate("r1", 30, samples: 2), // pocas muestras
            CurrentScore = new RouteScore { Value = 50 },
            BestScore = new RouteScore { Value = 90 },
            SecondsSinceLastSwitch = 4000,
            SecondsBestConsistent = 60
        });

        Assert.Equal(AutoSwitchDecision.NoChange, result.Decision);
        Assert.Contains("Pocas muestras", result.Reason);
    }

    [Fact]
    public void AutoSwitch_RespetaLimiteHorario()
    {
        var policy = Policy(maxPerHour: 4);
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("direct", 80),
            BestCandidate = Candidate("r1", 30),
            CurrentScore = new RouteScore { Value = 50 },
            BestScore = new RouteScore { Value = 90 },
            SecondsSinceLastSwitch = 4000,
            SecondsBestConsistent = 60,
            SwitchesLastHour = 4 // ya cambió 4 veces
        });

        Assert.Equal(AutoSwitchDecision.NoChange, result.Decision);
        Assert.Contains("Límite", result.Reason);
    }

    [Fact]
    public void Failback_OcurreSiElTunelCae()
    {
        var policy = Policy();
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("r1", 40),
            BestCandidate = Candidate("direct", 60),
            TunnelDown = true
        });

        Assert.Equal(AutoSwitchDecision.Failback, result.Decision);
        Assert.Contains("directa", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failback_OcurreSiElTunelEmpeoraLaPerdida()
    {
        var policy = Policy();
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("r1", 40, loss: 0.12), // túnel con 12 % de pérdida
            BestCandidate = Candidate("direct", 60, loss: 0.01),
            CurrentScore = new RouteScore { Value = 70 },
            BestScore = new RouteScore { Value = 60 },
            SecondsSinceLastSwitch = 4000,
            SecondsBestConsistent = 60
        });

        Assert.Equal(AutoSwitchDecision.Failback, result.Decision);
        Assert.Contains("empeora", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AutoSwitch_NoCambiaSiElCandidatoEstaExcluido()
    {
        var policy = Policy();
        var result = policy.Evaluate(new AutoSwitchContext
        {
            CurrentCandidate = Candidate("direct", 80),
            BestCandidate = Candidate("r1", 30),
            CurrentScore = new RouteScore { Value = 50 },
            BestScore = new RouteScore { Value = 90, Excluded = true },
            SecondsSinceLastSwitch = 4000,
            SecondsBestConsistent = 60
        });

        Assert.Equal(AutoSwitchDecision.NoChange, result.Decision);
        Assert.Contains("excluida", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
