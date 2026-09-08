using System.IO.Pipes;
using System.Text;

namespace GameRouteOptimizer.Core.Privileged;

/// <summary>
/// Servidor IPC por named pipe para ejecutar operaciones privilegiadas desde la UI
/// (modo agente elevado). Protocolo: una línea JSON = PrivilegedOp; respuesta una línea JSON.
/// </summary>
public sealed class OpPipeServer : IAsyncDisposable
{
    public const string PipeName = "gro.privileged.v1";
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly OpExecutor _executor;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool _started;

    public OpPipeServer(OpExecutor executor)
    {
        _executor = executor;
    }

    public event EventHandler<string>? ClientConnected;
    public event EventHandler<Exception>? Error;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        try
        {
            if (_loop is not null)
            {
                await _loop.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Esperado.
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 4,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                ClientConnected?.Invoke(this, "cliente conectado");
                _ = Task.Run(() => HandleClientAsync(server, ct), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, ex);
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            var line = await ReadLineAsync(server, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            PrivilegedOp? op = null;
            try
            {
                op = System.Text.Json.JsonSerializer.Deserialize<PrivilegedOp>(line);
            }
            catch (System.Text.Json.JsonException)
            {
                // Respuesta de error abajo.
            }

            PrivilegedOpResult result;
            if (op is null)
            {
                result = PrivilegedOpResult.Failure("Mensaje IPC inválido.");
            }
            else
            {
                result = await _executor.ExecuteAsync(op, ct).ConfigureAwait(false);
            }

            var response = System.Text.Json.JsonSerializer.Serialize(result) + "\n";
            var bytes = Encoding.UTF8.GetBytes(response);
            await server.WriteAsync(bytes, ct).ConfigureAwait(false);
            await server.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cliente desconectado / servidor parado.
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
        finally
        {
            try
            {
                server.Dispose();
            }
            catch (Exception)
            {
                // Ignorado.
            }
        }
    }

    private static async Task<string?> ReadLineAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageBytes];
        var total = 0;
        while (total < MaxMessageBytes)
        {
            var read = await server.ReadAsync(buffer.AsMemory(total, 1024), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            // El cliente cierra su lado de escritura tras enviar: si hay datos y EOF, terminamos.
            if (!server.IsConnected && server.CanRead)
            {
                break;
            }

            if (buffer.AsSpan(0, total).IndexOf((byte)'\n') >= 0)
            {
                break;
            }
        }

        if (total == 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, total);
        var newline = text.IndexOf('\n');
        return newline >= 0 ? text[..newline] : text;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}

/// <summary>Cliente IPC simple (la app se conecta al agente elevado).</summary>
public sealed class OpPipeClient : IPrivilegedOps
{
    public const int ConnectTimeoutMs = 3000;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public async Task<PrivilegedOpResult> RunAsync(PrivilegedOp op, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return PrivilegedOpResult.Failure("El agente IPC solo está disponible en Windows.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        await using var client = new NamedPipeClientStream(".", OpPipeServer.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            var payload = System.Text.Json.JsonSerializer.Serialize(op) + "\n";
            var bytes = Encoding.UTF8.GetBytes(payload);
            await client.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
            await client.FlushAsync(cts.Token).ConfigureAwait(false);
            client.Flush();

            var response = await ReadResponseAsync(client, cts.Token).ConfigureAwait(false);
            if (response is null)
            {
                return PrivilegedOpResult.Failure("El agente no respondió.");
            }

            return System.Text.Json.JsonSerializer.Deserialize<PrivilegedOpResult>(response)
                   ?? PrivilegedOpResult.Failure("Respuesta del agente inválida.");
        }
        catch (TimeoutException)
        {
            return PrivilegedOpResult.Failure("El agente elevado no está disponible (¿está en marcha?).");
        }
        catch (OperationCanceledException)
        {
            return PrivilegedOpResult.Failure("Operación IPC cancelada.");
        }
        catch (Exception ex)
        {
            return PrivilegedOpResult.Failure("Error IPC: " + ex.Message);
        }
    }

    private static async Task<string?> ReadResponseAsync(NamedPipeClientStream client, CancellationToken ct)
    {
        var buffer = new byte[1024 * 1024];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await client.ReadAsync(buffer.AsMemory(total, Math.Min(1024, buffer.Length - total)), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (buffer.AsSpan(0, total).IndexOf((byte)'\n') >= 0)
            {
                break;
            }
        }

        if (total == 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, total);
        var newline = text.IndexOf('\n');
        return newline >= 0 ? text[..newline] : text;
    }
}
