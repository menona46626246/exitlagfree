using System.Net;
using GameRouteOptimizer.Core.Probing;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Traceroute/tracert simplificado (estilo mtr-lite) usando solo ICMP con TTL creciente.
/// Rate limit bajo y máximo 30 saltos. No requiere administrador.
/// </summary>
public sealed class TracerouteEngine
{
    public const int DefaultMaxHops = 30;
    private const int SilentHopsToStop = 4;

    private readonly IProbeTransport _transport;
    private readonly EndpointResolver _resolver;

    public TracerouteEngine(IProbeTransport transport, EndpointResolver? resolver = null)
    {
        _transport = transport;
        _resolver = resolver ?? new EndpointResolver();
    }

    public async Task<TracerouteResult> TraceAsync(
        string host,
        CancellationToken ct,
        int maxHops = DefaultMaxHops,
        int attemptsPerHop = 2,
        int timeoutMs = 900)
    {
        var hops = new List<TracerouteHop>();
        var observations = new List<string>();

        var ip = IPAddress.TryParse(host, out var parsed)
            ? parsed
            : _resolver.PreferIpv4(await _resolver.ResolveAsync(host, ct).ConfigureAwait(false));

        if (ip is null)
        {
            return new TracerouteResult
            {
                Target = host,
                Hops = hops,
                Completed = false,
                Note = "No se pudo resolver el dominio.",
                Observations = observations,
            };
        }

        var silent = 0;
        var completed = false;
        var minRtt = double.MaxValue;

        for (var ttl = 1; ttl <= maxHops; ttl++)
        {
            ct.ThrowIfCancellationRequested();

            var rtts = new List<double?>();
            IPAddress? responder = null;
            var timeouts = 0;

            for (var attempt = 0; attempt < attemptsPerHop; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }

                var reply = await _transport.IcmpProbeAsync(ip, ttl, timeoutMs, ct).ConfigureAwait(false);
                if (reply.Success && reply.RttMs.HasValue)
                {
                    rtts.Add(reply.RttMs.Value);
                    responder ??= reply.Sender;
                    completed = true;
                    break; // destino alcanzado
                }

                if (reply.TtlExpired)
                {
                    rtts.Add(reply.RttMs);
                    responder ??= reply.Sender;
                    continue;
                }

                if (reply.Failure == ProbeFailureReason.IcmpBlocked)
                {
                    return new TracerouteResult
                    {
                        Target = host,
                        Hops = hops,
                        Completed = false,
                        Note = "ICMP está bloqueado o no permitido; el traceroute no puede completarse.",
                        Observations = observations,
                    };
                }

                timeouts++;
            }

            var hop = new TracerouteHop
            {
                Ttl = ttl,
                Address = responder?.ToString(),
                RttMs1 = rtts.Count > 0 ? rtts[0] : null,
                RttMs2 = rtts.Count > 1 ? rtts[1] : null,
                Timeouts = timeouts,
            };
            hops.Add(hop);

            if (hop.RttMs1.HasValue && hop.RttMs1.Value > 0)
            {
                minRtt = Math.Min(minRtt, hop.RttMs1.Value);
            }

            if (completed)
            {
                break;
            }

            silent = hop.Responded ? 0 : silent + 1;
            if (silent >= SilentHopsToStop)
            {
                observations.Add($"Sin respuesta desde el salto {ttl - SilentHopsToStop + 1} al {ttl}; se detiene la búsqueda.");
                break;
            }
        }

        Analyze(hops, minRtt, observations);
        return new TracerouteResult
        {
            Target = host,
            Hops = hops,
            Completed = completed,
            Note = completed ? $"Destino alcanzado en {hops.Count} saltos." : "No se alcanzó el destino.",
            Observations = observations,
        };
    }

    /// <summary>Detecta saltos problemáticos: latencia anómala o pérdida alta.</summary>
    public static void Analyze(IReadOnlyList<TracerouteHop> hops, double minRtt, List<string> observations)
    {
        for (var i = 0; i < hops.Count; i++)
        {
            var hop = hops[i];
            var best = Math.Min(hop.RttMs1 ?? double.MaxValue, hop.RttMs2 ?? double.MaxValue);
            if (best >= double.MaxValue)
            {
                if (hop.Timeouts > 0)
                {
                    observations.Add($"Salto {hop.Ttl}: sin respuesta (los saltos intermedios pueden filtrar ICMP).");
                }

                continue;
            }

            if (minRtt > 0 && best > minRtt * 4 && best > 100)
            {
                observations.Add($"Salto {hop.Ttl} ({hop.Address}): latencia {best:F0} ms — posible cuello de botella.");
            }

            if (hop.Timeouts > 0)
            {
                observations.Add($"Salto {hop.Ttl} ({hop.Address}): {hop.Timeouts}/{hop.Timeouts + (hop.RttMs1.HasValue ? 1 : 0) + (hop.RttMs2.HasValue ? 1 : 0)} probes perdidas.");
            }
        }
    }
}
