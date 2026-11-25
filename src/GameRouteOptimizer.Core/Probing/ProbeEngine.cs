using System.Net;
using System.Net.Sockets;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>Opciones del motor de probes.</summary>
public sealed class ProbeOptions
{
    public int TimeoutMs { get; set; } = 2000;

    public int QuickSamples { get; set; } = 8;

    public int DeepSamples { get; set; } = 40;

    public int IntervalMs { get; set; } = 1000;

    public double MaxProbesPerSecond { get; set; } = 5;

    public ProbeOptions() { }

    public ProbeOptions(AppSettings settings)
    {
        TimeoutMs = settings.ProbeTimeoutMs;
        QuickSamples = settings.QuickSamples;
        DeepSamples = settings.DeepSamples;
        IntervalMs = settings.ProbeIntervalMs;
        MaxProbesPerSecond = settings.MaxProbesPerSecond;
    }
}

/// <summary>Motor de pruebas de red (modo 1: diagnóstico). Respeta endpoints autorizados por el usuario.</summary>
public sealed class ProbeEngine
{
    private readonly ProbeOptions _options;
    private readonly IProbeTransport _transport;
    private readonly DnsResolver _dns = new();

    public ProbeEngine(ProbeOptions? options = null, IProbeTransport? transport = null)
    {
        _options = options ?? new ProbeOptions();
        _transport = transport ?? new DefaultProbeTransport();
    }

    public ProbeOptions Options => _options;

    /// <summary>
    /// Ejecuta una serie de probes hacia el servidor objetivo y devuelve el resumen.
    /// Con protocolo Auto: prueba ICMP primero; si ICMP no responde cambia a TCP connect
    /// (primer puerto definido) y, si no hay puertos, a HTTP/S health si existe.
    /// Nunca lanza por falta de respuesta: devuelve resumen con LossRatio=1.
    /// </summary>
    public async Task<ProbeSummary> ProbeAsync(GameServer target, ProbeMode mode, CancellationToken ct = default)
    {
        var (samples, protocolUsed) = await RunSeriesAsync(target, mode, ct);
        return ProbeSummaryCalculator.Compute(ResolveTargetName(target), protocolUsed, samples);
    }

    /// <summary>Ejecuta la serie de probes y devuelve las muestras + protocolo efectivamente usado.</summary>
    public async Task<(List<ProbeSample> Samples, ProbeProtocol ProtocolUsed)> RunSeriesAsync(GameServer target, ProbeMode mode, CancellationToken ct = default)
    {
        var sampleCount = mode == ProbeMode.Quick ? _options.QuickSamples : _options.DeepSamples;
        var limiter = new ProbeRateLimiter(_options.MaxProbesPerSecond);
        var results = new List<ProbeSample>();

        // Resolución DNS una vez (el usuario definió el dominio explícitamente).
        var host = await _dns.ResolveAsync(target, ct);
        if (host is null)
        {
            results.Add(ProbeSample.Fail("dns", "No se pudo resolver el dominio."));
            return (results, target.PreferredProtocol == ProbeProtocol.Auto ? ProbeProtocol.Icmp : target.PreferredProtocol);
        }
        target.ResolvedIp = host;

        var protocol = target.PreferredProtocol;
        if (protocol == ProbeProtocol.Auto)
            protocol = await DetectBestProtocolAsync(host, target, limiter, ct);

        for (var i = 0; i < sampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            await limiter.WaitTurnAsync(ct);
            var sample = await SingleProbeAsync(host, target, protocol, ct);
            results.Add(sample);
            if (i < sampleCount - 1)
            {
                try { await Task.Delay(_options.IntervalMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        return (results, protocol);
    }

    /// <summary>Detecta el mejor protocolo disponible con 2 probes de cortesía (sin saturar).</summary>
    private async Task<ProbeProtocol> DetectBestProtocolAsync(string host, GameServer target, ProbeRateLimiter limiter, CancellationToken ct)
    {
        try
        {
            await limiter.WaitTurnAsync(ct);
            var icmp = await _transport.IcmpAsync(host, _options.TimeoutMs, ct);
            if (icmp.Success) return ProbeProtocol.Icmp;
            // Un único fallo ICMP puede ser bloqueo o ruido; prueba una segunda vez.
            await limiter.WaitTurnAsync(ct);
            var icmp2 = await _transport.IcmpAsync(host, _options.TimeoutMs, ct);
            if (icmp2.Success) return ProbeProtocol.Icmp;
        }
        catch (ProbeUnsupportedException)
        {
            // ICMP sin permisos en el entorno → seguir con TCP.
        }

        if (target.Ports.Count > 0)
            return ProbeProtocol.Tcp;

        if (!string.IsNullOrWhiteSpace(target.HealthUrl))
            return ProbeProtocol.Http;

        // Sin puertos ni health URL: se queda con ICMP aunque no responda (métricas no fiables).
        return ProbeProtocol.Icmp;
    }

    private async Task<ProbeSample> SingleProbeAsync(string host, GameServer target, ProbeProtocol protocol, CancellationToken ct)
    {
        try
        {
            return protocol switch
            {
                ProbeProtocol.Icmp => await _transport.IcmpAsync(host, _options.TimeoutMs, ct),
                ProbeProtocol.Tcp => await _transport.TcpConnectAsync(host, target.Ports.Count > 0 ? target.Ports[0] : 80, _options.TimeoutMs, ct),
                ProbeProtocol.Udp => await _transport.UdpEchoAsync(host, target.Ports.Count > 0 ? target.Ports[0] : 51820, _options.TimeoutMs, ct),
                ProbeProtocol.Http => await _transport.HttpGetAsync(
                    string.IsNullOrWhiteSpace(target.HealthUrl) ? $"http://{host}/" : target.HealthUrl, _options.TimeoutMs, ct),
                _ => await _transport.TcpConnectAsync(host, target.Ports.Count > 0 ? target.Ports[0] : 80, _options.TimeoutMs, ct)
            };
        }
        catch (ProbeUnsupportedException ex)
        {
            return ProbeSample.Fail(protocol.ToString().ToLowerInvariant(), ex.Message);
        }
        catch (Exception ex)
        {
            return ProbeSample.Fail(protocol.ToString().ToLowerInvariant(), ex.GetType().Name);
        }
    }

    private static string ResolveTargetName(GameServer target)
        => !string.IsNullOrWhiteSpace(target.Ip) ? target.Ip : target.Domain;

    /// <summary>Probe rápida de una sola muestra (para monitoreo continuo ligero).</summary>
    public async Task<ProbeSample> SingleSampleAsync(string host, int port, ProbeProtocol protocol, CancellationToken ct = default)
    {
        try
        {
            return protocol switch
            {
                ProbeProtocol.Icmp => await _transport.IcmpAsync(host, _options.TimeoutMs, ct),
                ProbeProtocol.Tcp => await _transport.TcpConnectAsync(host, port, _options.TimeoutMs, ct),
                ProbeProtocol.Http => await _transport.HttpGetAsync($"http://{host}/", _options.TimeoutMs, ct),
                _ => await _transport.TcpConnectAsync(host, port, _options.TimeoutMs, ct)
            };
        }
        catch (ProbeUnsupportedException ex)
        {
            return ProbeSample.Fail(protocol.ToString().ToLowerInvariant(), ex.Message);
        }
        catch (Exception ex)
        {
            return ProbeSample.Fail(protocol.ToString().ToLowerInvariant(), ex.GetType().Name);
        }
    }
}

/// <summary>Resolución DNS controlada (solo dominios definidos por el usuario).</summary>
public sealed class DnsResolver
{
    public async Task<string?> ResolveAsync(GameServer target, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(target.Ip) && IPAddress.TryParse(target.Ip, out _))
            return target.Ip;

        if (string.IsNullOrWhiteSpace(target.Domain)) return null;
        try
        {
            var host = target.Domain.Trim();
            // Si el dominio ya es una IP, devolverla directo.
            if (IPAddress.TryParse(host, out var ip)) return ip.ToString();
            var entries = await Dns.GetHostAddressesAsync(host, ct);
            var v4 = entries.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? entries.FirstOrDefault();
            return v4?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
