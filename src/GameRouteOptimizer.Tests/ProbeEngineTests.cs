using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using Xunit;

namespace GameRouteOptimizer.Tests;

/// <summary>Transporte de probes simulado para tests deterministas (sin red externa).</summary>
internal sealed class FakeProbeTransport : IProbeTransport
{
    /// <summary>Configuración por host: latencia simulada o fallo.</summary>
    public Dictionary<string, double> IcmpLatency { get; } = new();

    public Dictionary<string, double> TcpLatency { get; } = new();

    public bool IcmpSupported { get; set; } = true;

    public int IcmpCalls { get; private set; }

    public int TcpCalls { get; private set; }

    public Task<ProbeSample> IcmpAsync(string host, int timeoutMs, CancellationToken ct, int ttl = 64)
    {
        IcmpCalls++;
        if (!IcmpSupported)
        {
            return Task.FromException<ProbeSample>(new ProbeUnsupportedException("ICMP no disponible (fake)."));
        }

        if (IcmpLatency.TryGetValue(host, out var ms))
        {
            return Task.FromResult(new ProbeSample(true, ms, DateTimeOffset.UtcNow, "icmp"));
        }

        return Task.FromResult(ProbeSample.Fail("icmp", "timeout"));
    }

    public Task<ProbeSample> TcpConnectAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        TcpCalls++;
        if (TcpLatency.TryGetValue(host, out var ms))
        {
            return Task.FromResult(new ProbeSample(true, ms, DateTimeOffset.UtcNow, "tcp"));
        }

        return Task.FromResult(ProbeSample.Fail("tcp", "timeout"));
    }

    public Task<ProbeSample> UdpEchoAsync(string host, int port, int timeoutMs, CancellationToken ct)
        => Task.FromResult(ProbeSample.Fail("udp", "timeout"));

    public Task<ProbeSample> HttpGetAsync(string url, int timeoutMs, CancellationToken ct)
        => Task.FromResult(ProbeSample.Fail("http", "timeout"));
}

/// <summary>ProbeEngine: timeouts, ICMP bloqueado y fallbacks; nunca lanza por falta de respuesta.</summary>
public class ProbeEngineTests
{
    [Fact]
    public async Task ProbeEngine_ManejaTimeoutsSinLanzar()
    {
        // Host inesperado (no está en los diccionarios del fake) → todos los probes fallan.
        var transport = new FakeProbeTransport { IcmpSupported = true };
        var engine = new ProbeEngine(new ProbeOptions { QuickSamples = 4, IntervalMs = 1, MaxProbesPerSecond = 100 }, transport);
        var target = new GameServer { Name = "x", Ip = "203.0.113.1" };

        var summary = await engine.ProbeAsync(target, ProbeMode.Quick);

        Assert.False(summary.IsReachable);
        Assert.Equal(1, summary.LossRatio);
        Assert.Equal(4, summary.TotalProbes);
        Assert.Equal(4, summary.FailedProbes);
    }

    [Fact]
    public async Task ProbeEngine_SinIcmpDisponible_CaenATcpSinExcepcion()
    {
        var transport = new FakeProbeTransport
        {
            IcmpSupported = false, // simula entorno sin permisos ICMP
            TcpLatency = { ["203.0.113.5"] = 42 }
        };
        var engine = new ProbeEngine(new ProbeOptions { QuickSamples = 5, IntervalMs = 1, MaxProbesPerSecond = 100 }, transport);
        var target = new GameServer { Name = "x", Ip = "203.0.113.5", Ports = [7777] };

        var summary = await engine.ProbeAsync(target, ProbeMode.Quick);

        Assert.True(summary.IsReachable);
        Assert.Equal(ProbeProtocol.Tcp, summary.ProtocolUsed);
        Assert.Equal(42, summary.MeanMs, 1);
        Assert.Equal(0, summary.LossRatio);
        Assert.True(transport.TcpCalls > 0);
    }

    [Fact]
    public async Task ProbeEngine_IcmpFuncionaCuandoDisponible()
    {
        var transport = new FakeProbeTransport
        {
            IcmpLatency = { ["203.0.113.9"] = 25 }
        };
        var engine = new ProbeEngine(new ProbeOptions { QuickSamples = 6, IntervalMs = 1, MaxProbesPerSecond = 100 }, transport);
        var target = new GameServer { Name = "x", Ip = "203.0.113.9" };

        var summary = await engine.ProbeAsync(target, ProbeMode.Quick);

        Assert.True(summary.IsReachable);
        Assert.Equal(ProbeProtocol.Icmp, summary.ProtocolUsed);
        Assert.Equal(6, summary.TotalProbes);
    }

    [Fact]
    public async Task ProbeEngine_DnsInexistente_DevuelveInalcanzable()
    {
        var transport = new FakeProbeTransport();
        var engine = new ProbeEngine(new ProbeOptions { QuickSamples = 3, IntervalMs = 1, MaxProbesPerSecond = 100 }, transport);
        var target = new GameServer { Name = "x", Domain = "dominio-que-no-existe-invalido.invalid" };

        var summary = await engine.ProbeAsync(target, ProbeMode.Quick);

        Assert.False(summary.IsReachable);
        Assert.Equal(1, summary.LossRatio);
    }
}

/// <summary>Matemática del resumen de probes (percentiles, jitter, estabilidad).</summary>
public class ProbeSummaryCalculatorTests
{
    private static List<ProbeSample> Samples(params double[] rtts) =>
        rtts.Select(r => new ProbeSample(true, r, DateTimeOffset.UtcNow, "icmp")).ToList();

    [Fact]
    public void Summary_CalculaEstadisticasBasicas()
    {
        var summary = ProbeSummaryCalculator.Compute("1.2.3.4", ProbeProtocol.Icmp, Samples(10, 20, 30, 40, 50));

        Assert.Equal(5, summary.SuccessCount);
        Assert.Equal(0, summary.LossRatio);
        Assert.Equal(30, summary.MeanMs, 1);
        Assert.Equal(10, summary.MinMs, 1);
        Assert.Equal(50, summary.MaxMs, 1);
        Assert.Equal(10, summary.JitterMs, 1); // diffs: 10,10,10,10 → media 10
    }

    [Fact]
    public void Summary_CalculaPerdida()
    {
        var samples = Samples(10, 20, 30);
        samples.Add(ProbeSample.Fail("icmp", "timeout"));
        samples.Add(ProbeSample.Fail("icmp", "timeout"));

        var summary = ProbeSummaryCalculator.Compute("1.2.3.4", ProbeProtocol.Icmp, samples);
        Assert.Equal(0.4, summary.LossRatio, 3);
        Assert.Equal(3, summary.SuccessCount);
    }

    [Fact]
    public void Summary_PercentilesCorrectos()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        var summary = ProbeSummaryCalculator.Compute("x", ProbeProtocol.Icmp, Samples(values));

        Assert.Equal(50.5, summary.P50Ms, 1);
        Assert.Equal(95.05, summary.P95Ms, 1);
        Assert.Equal(99.01, summary.P99Ms, 1);
    }

    [Fact]
    public void Summary_SerieVacia_Inalcanzable()
    {
        var summary = ProbeSummaryCalculator.Compute("x", ProbeProtocol.Icmp, new List<ProbeSample>());
        Assert.False(summary.IsReachable);
        Assert.Equal(1, summary.LossRatio);
    }

    [Fact]
    public void Stability_PenalizaPerdidaYJitter()
    {
        var estable = ProbeSummaryCalculator.ComputeStability(0, 0, [50]);
        var conPerdida = ProbeSummaryCalculator.ComputeStability(0.10, 0, [50]);
        var conJitter = ProbeSummaryCalculator.ComputeStability(0, 40, [50]);

        Assert.True(estable > conPerdida);
        Assert.True(estable > conJitter);
        Assert.True(conPerdida < 20); // 10 % de pérdida es grave
    }
}
