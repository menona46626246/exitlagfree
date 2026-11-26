using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Ipc;

/// <summary>Mensaje IPC genérico (JSON con prefijo de longitud de 4 bytes little-endian).</summary>
public sealed class IpcMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Type { get; set; } = string.Empty;

    /// <summary>Payload JSON serializado del tipo específico.</summary>
    public string Payload { get; set; } = string.Empty;

    public IpcMessage() { }

    public IpcMessage(string type, object payload)
    {
        Type = type;
        Payload = JsonSerializer.Serialize(payload, IpcJson.Options);
    }

    public T? GetPayload<T>() => string.IsNullOrWhiteSpace(Payload)
        ? default
        : JsonSerializer.Deserialize<T>(Payload, IpcJson.Options);

    // ---------- Tipos de mensaje (constantes) ----------
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Status = "status";
    public const string StartTunnel = "start-tunnel";
    public const string StopTunnel = "stop-tunnel";
    public const string TunnelStatus = "tunnel-status";
    public const string ApplyRoutes = "apply-routes";
    public const string RestoreRoutes = "restore-routes";
    public const string EnableKillSwitch = "enable-killswitch";
    public const string DisableKillSwitch = "disable-killswitch";
    public const string EmergencyRestore = "emergency-restore";
    public const string Error = "error";
    public const string Ok = "ok";
}

/// <summary>Payload de petición de túnel (la clave privada viaja por pipe local cifrado por el SO).</summary>
public sealed class TunnelRequestPayload
{
    public string TunnelName { get; set; } = "gameroute-0";

    /// <summary>Configuración WireGuard serializada (TunnelProfile). Solo viaja localmente.</summary>
    public TunnelProfile? Tunnel { get; set; }

    public TunnelProfileDto? Dto { get; set; }
}

/// <summary>DTO simple del perfil de túnel (evita acoplar serialización del modelo interno).</summary>
public sealed class TunnelProfileDto
{
    public string Name { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Dns { get; set; } = string.Empty;
    public int Mtu { get; set; } = 1380;
    public string PublicKey { get; set; } = string.Empty;
    public List<string> AllowedIps { get; set; } = new();
    public string Endpoint { get; set; } = string.Empty;
    public int PersistentKeepalive { get; set; } = 25;
}

/// <summary>Payload de plan de rutas.</summary>
public sealed class RoutesPayload
{
    public List<string> AddRoutes { get; set; } = new();
    public string? TunnelAddress { get; set; }
    public int? TunnelInterfaceIndex { get; set; }
    public string TunnelName { get; set; } = string.Empty;
    public List<RouteEntryDto> OriginalRoutes { get; set; } = new();
}

public sealed class RouteEntryDto
{
    public string DestinationCidr { get; set; } = string.Empty;
    public string? Gateway { get; set; }
    public int? InterfaceIndex { get; set; }
    public string? InterfaceName { get; set; }
    public int Metric { get; set; }
}

/// <summary>Payload de estado del servicio.</summary>
public sealed class ServiceStatusPayload
{
    public string Version { get; set; } = "1.0.0";
    public bool Elevated { get; set; }
    public bool TunnelActive { get; set; }
    public string TunnelState { get; set; } = "Unknown";
    public bool KillSwitchActive { get; set; }
    public string WireGuardAvailable { get; set; } = "unknown";
    public string Message { get; set; } = string.Empty;
}

/// <summary>Opciones JSON compartidas de IPC.</summary>
public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Frames: [4 bytes len LE][JSON UTF8].</summary>
public static class IpcFraming
{
    public static async Task WriteAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, IpcJson.Options);
        var body = Encoding.UTF8.GetBytes(json);
        var len = BitConverter.GetBytes(body.Length);
        await stream.WriteAsync(len.AsMemory(0, 4), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var lenBuf = new byte[4];
        var read = 0;
        while (read < 4)
        {
            var n = await stream.ReadAsync(lenBuf.AsMemory(read, 4 - read), ct);
            if (n == 0)
            {
                return null;
            }

            read += n;
        }
        var length = BitConverter.ToInt32(lenBuf, 0);
        if (length is < 0 or > 4 * 1024 * 1024)
        {
            throw new IOException("Frame IPC inválido (tamaño).");
        }

        var body = new byte[length];
        read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0)
            {
                return null;
            }

            read += n;
        }
        return JsonSerializer.Deserialize<IpcMessage>(Encoding.UTF8.GetString(body), IpcJson.Options);
    }
}

/// <summary>Servidor IPC por named pipe local. Un hilo de aceptación; conexiones secuenciales por mensaje.</summary>
public sealed class NamedPipeIpcServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Dictionary<string, Func<IpcMessage, IpcMessage>> _handlers = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private readonly object _gate = new();

    public NamedPipeIpcServer(string pipeName)
    {
        _pipeName = pipeName;
    }

    public string PipeName => _pipeName;

    public void RegisterHandler(string type, Func<IpcMessage, IpcMessage> handler)
    {
        lock (_gate)
        {
            _handlers[type] = handler;
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream serverPipe;
            try
            {
                serverPipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }
            catch (Exception)
            {
                await Task.Delay(2000, ct);
                continue;
            }

            try
            {
                await serverPipe.WaitForConnectionAsync(ct);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(60_000);
                var message = await IpcFraming.ReadAsync(serverPipe, timeoutCts.Token);
                IpcMessage response;
                if (message is null)
                {
                    response = new IpcMessage(IpcMessage.Error, new { message = "Conexión vacía." });
                }
                else
                {
                    Func<IpcMessage, IpcMessage>? handler;
                    lock (_gate)
                    {
                        _handlers.TryGetValue(message.Type, out handler);
                    }

                    try
                    {
                        response = handler is not null
                            ? handler(message)
                            : new IpcMessage(IpcMessage.Error, new { message = $"Tipo desconocido: {message.Type}" });
                    }
                    catch (Exception ex)
                    {
                        response = new IpcMessage(IpcMessage.Error, new { message = ex.Message });
                    }
                }
                await IpcFraming.WriteAsync(serverPipe, response, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // cierre normal
            }
            catch (IOException)
            {
                // cliente desconectado: seguir aceptando
            }
            catch (Exception)
            {
                // error inesperado: seguir aceptando
            }
            finally
            {
                try { serverPipe.Dispose(); } catch { }
            }
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    public void Dispose() => Stop();
}

/// <summary>Cliente IPC por named pipe local (petición-respuesta sincrónica con timeout).</summary>
public sealed class NamedPipeIpcClient : IDisposable
{
    private readonly string _pipeName;

    public NamedPipeIpcClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>Envía un mensaje y espera respuesta. Devuelve null si el servicio no está disponible.</summary>
    public async Task<IpcMessage?> SendAsync(IpcMessage message, int timeoutMs = 15000, CancellationToken ct = default)
    {
        NamedPipeClientStream pipe;
        try
        {
            pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000, ct);
        }
        catch (Exception)
        {
            return null; // servicio no disponible
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await IpcFraming.WriteAsync(pipe, message, cts.Token);
            return await IpcFraming.ReadAsync(pipe, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return new IpcMessage(IpcMessage.Error, new { message = "Timeout esperando al servicio." });
        }
        catch (Exception ex)
        {
            return new IpcMessage(IpcMessage.Error, new { message = ex.Message });
        }
        finally
        {
            try { pipe.Dispose(); } catch { }
        }
    }

    public void Dispose() { }
}
