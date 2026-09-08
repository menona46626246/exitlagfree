using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Transporte real de probes usando solo APIs del sistema (Ping, TCP, UDP, HTTP).
/// Nunca lanza excepciones de red al llamador: todo se normaliza a <see cref="ProbeReply"/>.
/// No requiere administrador (Ping de Windows usa ICMP.dll).
/// </summary>
public sealed class SystemProbeTransport : IProbeTransport
{
    public string Description => "API del sistema (ICMP/TCP/UDP/HTTP)";

    private const int PayloadSize = 32;

    public async Task<ProbeReply> IcmpProbeAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var options = new PingOptions { Ttl = ttl, DontFragment = false };
            var buffer = new byte[PayloadSize];
            var reply = await ping.SendPingAsync(
                    target, TimeSpan.FromMilliseconds(timeoutMs), buffer, options, ct)
                .ConfigureAwait(false);

            switch (reply.Status)
            {
                case IPStatus.Success:
                    return ProbeReply.Ok(reply.RoundtripTime, reply.Address);
                case IPStatus.TtlExpired:
                    return new ProbeReply { TtlExpired = true, Sender = reply.Address, Failure = ProbeFailureReason.None };
                case IPStatus.TimedOut:
                    return ProbeReply.Fail(ProbeFailureReason.Timeout);
                case IPStatus.DestinationUnreachable:
                case IPStatus.DestinationHostUnreachable:
                case IPStatus.DestinationNetworkUnreachable:
                case IPStatus.DestinationPortUnreachable:
                case IPStatus.DestinationProtocolUnreachable:
                    return ProbeReply.Fail(ProbeFailureReason.Unreachable, $"IPStatus.{reply.Status}");
                default:
                    return ProbeReply.Fail(ProbeFailureReason.Error, $"IPStatus.{reply.Status}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ProbeReply.Fail(ProbeFailureReason.Cancelled, "cancelado");
        }
        catch (PingException ex)
        {
            return ClassifySocketError(ex, "ping");
        }
        catch (PlatformNotSupportedException)
        {
            return ProbeReply.Fail(ProbeFailureReason.ProtocolNotSupported, "ICMP no soportado en esta plataforma");
        }
        catch (Exception ex)
        {
            return ProbeReply.Fail(ProbeFailureReason.Error, ex.Message);
        }
    }

    public async Task<ProbeReply> TcpConnectAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient(target.AddressFamily);
            await client.ConnectAsync(target, port, cts.Token).ConfigureAwait(false);
            sw.Stop();
            return ProbeReply.Ok(sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ProbeReply.Fail(ProbeFailureReason.Cancelled, "cancelado");
        }
        catch (OperationCanceledException)
        {
            return ProbeReply.Fail(ProbeFailureReason.Timeout, $"sin respuesta TCP en {timeoutMs} ms");
        }
        catch (SocketException ex)
        {
            return ClassifySocketError(ex, "tcp");
        }
        catch (Exception ex)
        {
            return ProbeReply.Fail(ProbeFailureReason.Error, ex.Message);
        }
    }

    public async Task<ProbeReply> UdpProbeAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();

        try
        {
            using var udp = new UdpClient(target.AddressFamily);
            udp.Connect(target, port);
            var payload = new byte[1] { 0x00 };
            await udp.SendAsync(payload, cts.Token).ConfigureAwait(false);
            await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();
            return ProbeReply.Ok(sw.Elapsed.TotalMilliseconds,
                detail: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ProbeReply.Fail(ProbeFailureReason.Cancelled, "cancelado");
        }
        catch (OperationCanceledException)
        {
            // Timeout del CTS interno (CancelAfter): sin respuesta UDP dentro del plazo.
            return ProbeReply.Fail(ProbeFailureReason.Timeout, $"sin respuesta UDP en {timeoutMs} ms");
        }
        catch (SocketException ex)
        {
            // 10054: ICMP port unreachable en socket UDP conectado → el host está vivo, puerto cerrado.
            if (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused)
            {
                return ProbeReply.Fail(ProbeFailureReason.Refused,
                    "el host respondió con puerto UDP cerrado (el host está activo)");
            }

            return ClassifySocketError(ex, "udp");
        }
        catch (Exception ex)
        {
            return ProbeReply.Fail(ProbeFailureReason.Error, ex.Message);
        }
    }

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan, // el timeout se controla con CTS
        };
    }

    public async Task<ProbeReply> HttpProbeAsync(Uri url, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "GameRouteOptimizer/1.0 (health-check autorizado)");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            // Se lee el primer byte del cuerpo para incluir el tiempo de transferencia inicial.
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[1];
            await stream.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false);
            sw.Stop();
            var ok = (int)response.StatusCode < 500; // 5xx = servidor vivo pero con error: latencia válida, salud dudosa
            return new ProbeReply
            {
                Success = ok,
                RttMs = sw.Elapsed.TotalMilliseconds,
                Detail = $"HTTP {(int)response.StatusCode}",
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ProbeReply.Fail(ProbeFailureReason.Cancelled, "cancelado");
        }
        catch (OperationCanceledException)
        {
            return ProbeReply.Fail(ProbeFailureReason.Timeout, $"HTTP sin respuesta en {timeoutMs} ms");
        }
        catch (HttpRequestException ex)
        {
            return ex.InnerException is SocketException sock
                ? ClassifySocketError(sock, "http")
                : ProbeReply.Fail(ProbeFailureReason.Error, ex.Message);
        }
        catch (Exception ex)
        {
            return ProbeReply.Fail(ProbeFailureReason.Error, ex.Message);
        }
    }

    private static ProbeReply ClassifySocketError(Exception ex, string op)
    {
        var socketCode = (ex as SocketException)?.SocketErrorCode
            ?? (ex.InnerException as SocketException)?.SocketErrorCode;

        switch (socketCode)
        {
            case SocketError.AccessDenied:
                return ProbeReply.Fail(ProbeFailureReason.IcmpBlocked, $"acceso denegado ({op}); ICMP suele requerir permiso o está bloqueado");
            case SocketError.TimedOut:
            case SocketError.WouldBlock:
                return ProbeReply.Fail(ProbeFailureReason.Timeout, $"{op}: sin respuesta");
            case SocketError.ConnectionRefused:
                return ProbeReply.Fail(ProbeFailureReason.Refused, $"{op}: conexión rechazada");
            case SocketError.HostUnreachable:
            case SocketError.NetworkUnreachable:
            case SocketError.AddressNotAvailable:
                return ProbeReply.Fail(ProbeFailureReason.Unreachable, $"{op}: destino inalcanzable");
            case SocketError.OperationAborted:
                return ProbeReply.Fail(ProbeFailureReason.Cancelled, "operación cancelada");
            case null:
            case SocketError.SocketError:
                return ProbeReply.Fail(ProbeFailureReason.Error, $"{op}: {ex.Message}");
            default:
                return ProbeReply.Fail(ProbeFailureReason.Error, $"{op}: {ex.Message} ({(int)socketCode})");
        }
    }
}
