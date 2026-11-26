using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Traceroute simplificado estilo mtr: ping con TTL incremental, 2 muestras por salto.
/// Fallback: `tracert` (Windows) o `traceroute -n` (Linux) si ICMP por TTL no está disponible.
/// </summary>
public sealed class TracerouteEngine
{
    private readonly int _timeoutMs;
    private readonly int _maxHops;
    private readonly int _samplesPerHop;

    public TracerouteEngine(int timeoutMs = 1500, int maxHops = 20, int samplesPerHop = 2)
    {
        _timeoutMs = timeoutMs;
        _maxHops = maxHops;
        _samplesPerHop = Math.Max(1, samplesPerHop);
    }

    public async Task<TracerouteResult> TraceAsync(string host, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var resolved = Resolve(host);
        if (resolved is null)
        {
            return new TracerouteResult { Target = host, Method = "none", Duration = sw.Elapsed };
        }

        try
        {
            var result = await TraceWithTtlAsync(host, resolved, ct);
            result.Duration = sw.Elapsed;
            return result;
        }
        catch (ProbeUnsupportedException)
        {
            Log.Information("ICMP por TTL no disponible; usando herramienta del sistema (tracert/traceroute).");
            var result = await TraceWithSystemToolAsync(host, ct);
            result.Duration = sw.Elapsed;
            return result;
        }
    }

    private async Task<TracerouteResult> TraceWithTtlAsync(string host, string ip, CancellationToken ct)
    {
        var result = new TracerouteResult { Target = host, ResolvedIp = ip, Method = "icmp-ttl" };
        var limiter = new ProbeRateLimiter(10);

        for (var ttl = 1; ttl <= _maxHops; ttl++)
        {
            ct.ThrowIfCancellationRequested();
            var hop = new TracerouteHop { Hop = ttl };
            for (var s = 0; s < _samplesPerHop; s++)
            {
                await limiter.WaitTurnAsync(ct);
                var sample = await IcmpWithTtlAsync(ip, ttl, ct);
                if (sample.Success || sample.Detail == "ttl-expired")
                {
                    hop.Address = sample.Address ?? "*";
                    if (sample.Success && sample.RttMs > 0)
                    {
                        hop.RttSamplesMs.Add(sample.RttMs);
                    }
                }
                else
                {
                    hop.LostProbes++;
                }
            }

            result.Hops.Add(hop);
            if (hop.Responded && hop.Address == ip)
            {
                result.Completed = true;
                break;
            }
        }
        DetectBottlenecks(result);
        return result;
    }

    private async Task<(bool Success, double RttMs, string? Address, string? Detail)> IcmpWithTtlAsync(string ip, int ttl, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var reply = await ping.SendPingAsync(ip, _timeoutMs, "gameroute-trace"u8.ToArray(), new PingOptions(ttl, true));
            return reply.Status switch
            {
                IPStatus.Success => (true, reply.RoundtripTime, reply.Address?.ToString(), "ok"),
                IPStatus.TtlExpired => (false, 0, reply.Address?.ToString(), "ttl-expired"),
                IPStatus.TimedOut => (false, 0, null, "timeout"),
                _ => (false, 0, null, reply.Status.ToString())
            };
        }
        catch (Exception ex) when (ex is PingException or NotSupportedException or System.Net.Sockets.SocketException)
        {
            throw new ProbeUnsupportedException($"ICMP TTL no disponible ({ex.GetType().Name}).");
        }
    }

    private async Task<TracerouteResult> TraceWithSystemToolAsync(string host, CancellationToken ct)
    {
        var result = new TracerouteResult { Target = host, Method = PlatformInfo.IsWindows ? "tracert" : "traceroute" };

        var (file, args) = PlatformInfo.IsWindows
            ? ("tracert", new[] { "-d", "-w", "1000", "-h", _maxHops.ToString(), host })
            : ("traceroute", new[] { "-n", "-w", "1", "-q", "1", "-m", _maxHops.ToString(), host });

        var output = await Task.Run(() => CommandRunner.Run(file, args, 120_000), ct);
        if (!output.Success && string.IsNullOrWhiteSpace(output.StdOut))
        {
            result.Hops.Add(new TracerouteHop { Hop = 1, Address = "*", LostProbes = 1 });
            return result;
        }

        var hopIndex = 0;
        foreach (var line in output.StdOut.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            // Líneas de tracert/traceroute: "  1    1 ms   1 ms   2 ms  192.168.1.1" o " 1  192.168.1.1  1.2 ms"
            var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"^\s*(\d+)[\s.]+(.*)$");
            if (!match.Success || match.Groups[1].Value == "1" && trimmed.StartsWith("Tracing"))
            {
                continue;
            }

            hopIndex++;
            var hop = new TracerouteHop { Hop = hopIndex };
            var ips = System.Text.RegularExpressions.Regex.Matches(trimmed, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            if (ips.Count > 0)
            {
                hop.Address = ips[^1].Value;
            }
            else
            {
                hop.Address = "*";
            }

            var times = System.Text.RegularExpressions.Regex.Matches(trimmed, @"(\d+(?:\.\d+)?)\s*ms");
            foreach (var t in times)
            {
                if (double.TryParse(((System.Text.RegularExpressions.Match)t).Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture, out var ms))
                {
                    hop.RttSamplesMs.Add(ms);
                }
            }

            if (hop.Address == "*")
            {
                hop.LostProbes = _samplesPerHop;
            }

            result.Hops.Add(hop);
        }

        // IP final = destino si el último salto respondió.
        result.Completed = result.Hops.Count > 0 && result.Hops[^1].Responded;
        DetectBottlenecks(result);
        await Task.CompletedTask;
        return result;
    }

    /// <summary>Marca saltos con incremento fuerte de latencia respecto al salto previo.</summary>
    public static void DetectBottlenecks(TracerouteResult result)
    {
        double? prevMean = null;
        foreach (var hop in result.Hops)
        {
            if (!hop.Responded)
            {
                continue;
            }

            var mean = hop.MeanMs;
            if (prevMean.HasValue && mean > prevMean + 25 && mean > 60)
            {
                hop.SuspectedBottleneck = true;
            }

            prevMean = mean;
        }
    }

    private static string? Resolve(string host)
    {
        if (System.Net.IPAddress.TryParse(host, out var direct))
        {
            return direct.ToString();
        }

        try
        {
            var entries = System.Net.Dns.GetHostAddresses(host);
            return (entries.FirstOrDefault(e => e.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?? entries.FirstOrDefault())?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
