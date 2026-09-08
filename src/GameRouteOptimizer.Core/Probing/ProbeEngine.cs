using System.Diagnostics;
using System.Net;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Motor de pruebas de red respetuosas: series con rate limit bajo, timeouts configurables,
/// detección de ICMP bloqueado con alternativa TCP, y resúmenes estadísticos.
/// Nunca escanea: solo mide destinos explícitamente autorizados por el usuario.
/// </summary>
public sealed class ProbeEngine
{
    private readonly IProbeTransport _transport;
    private readonly ProbingSettings _settings;
    private readonly EndpointResolver _resolver;

    public ProbeEngine(IProbeTransport transport, ProbingSettings settings, EndpointResolver? resolver = null)
    {
        _transport = transport;
        _settings = settings;
        _resolver = resolver ?? new EndpointResolver();
    }

    public IProbeTransport Transport => _transport;
    public ProbingSettings Settings => _settings;

    /// <summary>
    /// Ejecuta una serie de probes. Si el tipo pedido es ICMP y está bloqueado, y la
    /// configuración lo permite, reintenta automáticamente con TCP connect.
    /// </summary>
    public async Task<ProbeSeriesResult> ProbeAsync(ProbeTargetSpec spec, bool deep, CancellationToken ct)
    {
        var count = deep ? _settings.DeepProbeCount : _settings.QuickProbeCount;

        var attemptKind = spec.Kind;
        if (attemptKind == ProbeKind.Icmp && !_settings.UseIcmp)
        {
            attemptKind = ResolveTcpKind(spec);
        }

        var result = await RunSeriesAsync(spec, attemptKind, count, ct).ConfigureAwait(false);

        // Fallback automático cuando ICMP no es fiable y hay permiso configurado.
        if (result.Summary.Successes == 0 &&
            attemptKind == ProbeKind.Icmp &&
            _settings.TcpFallbackWhenIcmpBlocked &&
            !ct.IsCancellationRequested)
        {
            var tcpSpec = new ProbeTargetSpec
            {
                Label = spec.Label,
                Host = spec.Host,
                Kind = ProbeKind.TcpConnect,
                Port = spec.Port > 0 ? spec.Port : _settings.TcpDefaultPort,
            };
            var tcpResult = await RunSeriesAsync(tcpSpec, ProbeKind.TcpConnect, count, ct).ConfigureAwait(false);
            tcpResult.Summary.IcmpReliable = false;
            tcpResult.Summary.Note = "ICMP sin respuesta o bloqueado; se usó TCP connect como alternativa.";
            return tcpResult;
        }

        return result;
    }

    public async Task<ProbeSeriesResult> RunSeriesAsync(
        ProbeTargetSpec spec,
        ProbeKind kind,
        int count,
        CancellationToken ct)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var label = string.IsNullOrWhiteSpace(spec.Label) ? spec.Host : spec.Label;
        var port = spec.Port > 0 ? spec.Port : _settings.TcpDefaultPort;

        if (ct.IsCancellationRequested)
        {
            return new ProbeSeriesResult
            {
                Summary = ProbeSummaryCalculator.Compute(label, kind, Array.Empty<ProbeReply>(), startedUtc,
                    DateTimeOffset.UtcNow, unavailableReason: "cancelado"),
                RawRttsMs = new List<double?>(),
            };
        }

        // Resolución DNS (solo hosts autorizados).
        var ip = await ResolveBestIpAsync(spec.Host, ct).ConfigureAwait(false);
        if (ip is null)
        {
            return new ProbeSeriesResult
            {
                Summary = ProbeSummaryCalculator.Compute(label, kind, Array.Empty<ProbeReply>(), startedUtc,
                    DateTimeOffset.UtcNow, unavailableReason: "no se pudo resolver el dominio (DNS)"),
                RawRttsMs = new List<double?>(),
            };
        }

        var attempts = new List<ProbeReply>(count);
        var raw = new List<double?>(count);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Separación entre probes (rate limit respetuoso), excepto la primera.
            if (i > 0)
            {
                var elapsed = stopwatch.ElapsedMilliseconds;
                var targetGap = _settings.IntervalMs;
                if (elapsed < targetGap)
                {
                    await Task.Delay((int)(targetGap - elapsed), ct).ConfigureAwait(false);
                }
            }

            stopwatch.Restart();
            ProbeReply reply;
            switch (kind)
            {
                case ProbeKind.Icmp:
                    reply = await _transport.IcmpProbeAsync(ip, ttl: 64, _settings.TimeoutMs, ct).ConfigureAwait(false);
                    break;
                case ProbeKind.TcpConnect:
                    reply = await _transport.TcpConnectAsync(ip, port, _settings.TimeoutMs, ct).ConfigureAwait(false);
                    break;
                case ProbeKind.Udp:
                    reply = await _transport.UdpProbeAsync(ip, port, _settings.TimeoutMs, ct).ConfigureAwait(false);
                    break;
                case ProbeKind.HttpGet:
                    reply = await ProbeHttpAsync(spec, ct).ConfigureAwait(false);
                    break;
                default:
                    reply = ProbeReply.Fail(ProbeFailureReason.Error, "tipo de probe no soportado");
                    break;
            }

            attempts.Add(reply);
            raw.Add(reply.Success ? reply.RttMs : null);
        }

        var summary = ProbeSummaryCalculator.Compute(
            label, kind, attempts, startedUtc, DateTimeOffset.UtcNow,
            icmpReliable: kind != ProbeKind.Icmp || attempts.Any(a => a.Success || a.Failure != ProbeFailureReason.IcmpBlocked),
            note: kind == ProbeKind.Udp && attempts.All(a => !a.Success)
                ? "UDP sin eco: muchos servidores de juego no responden a datagramas arbitrarios."
                : null);

        return new ProbeSeriesResult { Summary = summary, RawRttsMs = raw };
    }

    private async Task<ProbeReply> ProbeHttpAsync(ProbeTargetSpec spec, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spec.HttpUrl) ||
            !Uri.TryCreate(spec.HttpUrl, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return ProbeReply.Fail(ProbeFailureReason.Error, "URL HTTP/S inválida o ausente");
        }

        return await _transport.HttpProbeAsync(url, _settings.TimeoutMs, ct).ConfigureAwait(false);
    }

    private async Task<IPAddress?> ResolveBestIpAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var fixedIp))
        {
            return fixedIp;
        }

        var addresses = await _resolver.ResolveAsync(host, ct).ConfigureAwait(false);
        return _resolver.PreferIpv4(addresses);
    }

    private static ProbeKind ResolveTcpKind(ProbeTargetSpec spec) =>
        spec.Kind == ProbeKind.HttpGet ? ProbeKind.HttpGet : ProbeKind.TcpConnect;
}
