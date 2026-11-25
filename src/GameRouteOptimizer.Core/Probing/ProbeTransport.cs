using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Transporte de probes (abstraído para tests). Implementación por defecto usa APIs del
/// sistema: ICMP (Ping), TCP connect, UDP con eco y HTTP GET.
/// </summary>
public interface IProbeTransport
{
    Task<ProbeSample> IcmpAsync(string host, int timeoutMs, CancellationToken ct, int ttl = 64);

    Task<ProbeSample> TcpConnectAsync(string host, int port, int timeoutMs, CancellationToken ct);

    Task<ProbeSample> UdpEchoAsync(string host, int port, int timeoutMs, CancellationToken ct);

    Task<ProbeSample> HttpGetAsync(string url, int timeoutMs, CancellationToken ct);
}

/// <summary>ICMP no disponible en esta plataforma/permisos (se usa fallback).</summary>
public sealed class ProbeUnsupportedException(string message) : Exception(message) { }

/// <summary>Implementación real de transporte.</summary>
public sealed class DefaultProbeTransport : IProbeTransport
{
    private static readonly byte[] IcmpPayload = "gameroute-probe"u8.ToArray();
    private static readonly byte[] UdpPayload = "GR"u8.ToArray();

    private HttpClient? _httpClient;

    public async Task<ProbeSample> IcmpAsync(string host, int timeoutMs, CancellationToken ct, int ttl = 64)
    {
        try
        {
            using var ping = new Ping();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var options = new PingOptions(ttl, dontFragment: true);
            var reply = await ping.SendPingAsync(host, timeoutMs, IcmpPayload, options);
            return reply.Status switch
            {
                IPStatus.Success => new ProbeSample(true, reply.RoundtripTime, DateTimeOffset.UtcNow, "icmp"),
                IPStatus.TimedOut => ProbeSample.Fail("icmp", "timeout"),
                IPStatus.DestinationHostUnreachable => ProbeSample.Fail("icmp", "host-unreachable"),
                IPStatus.DestinationNetworkUnreachable => ProbeSample.Fail("icmp", "network-unreachable"),
                _ => ProbeSample.Fail("icmp", reply.Status.ToString())
            };
        }
        catch (Exception ex) when (ex is PingException or SocketException or NotSupportedException)
        {
            throw new ProbeUnsupportedException($"ICMP no disponible en este entorno ({ex.GetType().Name}).");
        }
    }

    public async Task<ProbeSample> TcpConnectAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        if (port is < 1 or > 65535) return ProbeSample.Fail("tcp", "puerto inválido");
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return new ProbeSample(true, sw.Elapsed.TotalMilliseconds, DateTimeOffset.UtcNow, "tcp");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProbeSample.Fail("tcp", "timeout");
        }
        catch (SocketException ex)
        {
            return ProbeSample.Fail("tcp", $"socket:{ex.SocketErrorCode}");
        }
        catch (Exception)
        {
            return ProbeSample.Fail("tcp", "error");
        }
    }

    public async Task<ProbeSample> UdpEchoAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        using var client = new UdpClient();
        client.Client.ReceiveTimeout = timeoutMs;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            await client.SendAsync(UdpPayload, host, port);
            var res = await client.ReceiveAsync(cts.Token);
            sw.Stop();
            return res.Buffer.Length > 0
                ? new ProbeSample(true, sw.Elapsed.TotalMilliseconds, DateTimeOffset.UtcNow, "udp")
                : ProbeSample.Fail("udp", "respuesta vacía");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProbeSample.Fail("udp", "timeout");
        }
        catch (SocketException ex)
        {
            return ProbeSample.Fail("udp", $"socket:{ex.SocketErrorCode}");
        }
    }

    public async Task<ProbeSample> HttpGetAsync(string url, int timeoutMs, CancellationToken ct)
    {
        _httpClient ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("GameRouteOptimizer/1.0");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();
            // Cualquier respuesta HTTP (incluye 4xx/5xx) prueba que hay conectividad; se mide TTFB.
            return new ProbeSample(true, sw.Elapsed.TotalMilliseconds, DateTimeOffset.UtcNow, "http",
                ((int)response.StatusCode).ToString());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProbeSample.Fail("http", "timeout");
        }
        catch (HttpRequestException ex)
        {
            return ProbeSample.Fail("http", ex.Message.Length > 80 ? "http-error" : ex.Message);
        }
    }
}

/// <summary>Rate limiter simple (token bucket) para respetar MaxProbesPerSecond.</summary>
public sealed class ProbeRateLimiter
{
    private readonly double _minIntervalMs;
    private DateTime _lastProbe = DateTime.MinValue;
    private readonly object _gate = new();

    public ProbeRateLimiter(double maxProbesPerSecond)
    {
        _minIntervalMs = maxProbesPerSecond <= 0 ? 0 : 1000.0 / maxProbesPerSecond;
    }

    public async Task WaitTurnAsync(CancellationToken ct)
    {
        while (true)
        {
            int delay;
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastProbe).TotalMilliseconds;
                if (elapsed >= _minIntervalMs)
                {
                    _lastProbe = now;
                    return;
                }
                delay = (int)(_minIntervalMs - elapsed);
            }
            await Task.Delay(Math.Max(delay, 1), ct);
        }
    }
}
