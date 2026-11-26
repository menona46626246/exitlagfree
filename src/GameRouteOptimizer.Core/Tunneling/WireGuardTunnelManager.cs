using System.Diagnostics;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>Petición de inicio de túnel.</summary>
public sealed class TunnelStartRequest
{
    public TunnelProfile Tunnel { get; set; } = new();

    /// <summary>Nombre de servicio/interfaz (p. ej. "gameroute-0").</summary>
    public string TunnelName { get; set; } = "gameroute-0";

    public int Mtu { get; set; } = 1380;
}

/// <summary>Estado actual del túnel.</summary>
public sealed class TunnelStatus
{
    public TunnelState State { get; set; } = TunnelState.Unknown;

    public string TunnelName { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string? LocalAddress { get; set; }

    public double SecondsSinceHandshake { get; set; } = double.NaN;

    public long ReceivedBytes { get; set; }

    public long SentBytes { get; set; }

    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public bool IsConnected => State == TunnelState.Connected;
}

/// <summary>Manager de túneles (implementaciones: WireGuard local, proxy IPC, simulado para tests).</summary>
public interface ITunnelManager
{
    Task<TunnelStatus> GetStatusAsync(CancellationToken ct = default);

    Task<TunnelStatus> StartAsync(TunnelStartRequest request, CancellationToken ct = default);

    Task<TunnelStatus> StopAsync(CancellationToken ct = default);

    event EventHandler<TunnelStatus>? StatusChanged;
}

/// <summary>
/// Integración WireGuard SOLO con herramientas oficiales:
/// - Windows: `wireguard.exe /installtunnelservice <conf>` y `/uninstalltunnelservice <nombre>`,
///   estado vía `wg.exe show <túnel>` (requiere WireGuard for Windows instalado con Wintun).
/// - Linux: `wg-quick up|down <conf>` y `wg show`.
/// No se implementan drivers propios ni se manipula el kernel.
/// </summary>
public sealed class WireGuardTunnelManager : ITunnelManager
{
    private readonly string _wgExePath;
    private readonly string _confDirectory;
    private TunnelStatus _lastStatus = new() { State = TunnelState.Disconnected };
    private readonly object _gate = new();

    public WireGuardTunnelManager(string? wgExePath = null, string? confDirectory = null)
    {
        _wgExePath = wgExePath ?? ResolveWireGuardPath();
        _confDirectory = confDirectory ?? Path.Combine(Path.GetTempPath(), "gameroute-tunnels");
    }

    public static string ResolveWireGuardPath()
    {
        if (PlatformInfo.IsWindows)
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe"),
                "wireguard.exe"
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c) || (c == "wireguard.exe" && PlatformInfo.CommandExists("wireguard.exe")))
                {
                    return c;
                }
            }

            return candidates[0];
        }
        return PlatformInfo.CommandExists("wg-quick") ? "wg-quick" : "wg-quick";
    }

    public static bool IsWireGuardAvailable()
    {
        if (PlatformInfo.IsWindows)
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return File.Exists(Path.Combine(pf, "WireGuard", "wireguard.exe"))
                || PlatformInfo.CommandExists("wireguard.exe")
                || PlatformInfo.CommandExists("wg.exe");
        }
        return PlatformInfo.CommandExists("wg-quick") || PlatformInfo.CommandExists("wg");
    }

    public event EventHandler<TunnelStatus>? StatusChanged;

    public Task<TunnelStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var name = _lastStatus.TunnelName;
        if (string.IsNullOrWhiteSpace(name) || _lastStatus.State is TunnelState.Disconnected or TunnelState.Unknown or TunnelState.Failed)
        {
            return Task.FromResult(_lastStatus);
        }

        var show = CommandRunner.Run("wg", new[] { "show", name }, 10_000);
        var status = ParseWgShow(show.StdOut, _lastStatus);
        SetStatus(status);
        return Task.FromResult(status);
    }

    /// <summary>Parser de la salida de `wg show <name>` (real, para estado y bytes).</summary>
    public static TunnelStatus ParseWgShow(string output, TunnelStatus previous)
    {
        var status = new TunnelStatus
        {
            State = previous.State,
            TunnelName = previous.TunnelName,
            Endpoint = previous.Endpoint
        };
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("endpoint:"))
            {
                status.Endpoint = line["endpoint:".Length..].Trim();
                status.State = TunnelState.Connected;
            }
            else if (line.StartsWith("latest handshake:"))
            {
                var text = line["latest handshake:".Length..].Trim();
                status.SecondsSinceHandshake = ParseHandshakeSeconds(text);
                if (status.SecondsSinceHandshake is >= 0 and < 180)
                {
                    status.State = TunnelState.Connected;
                }
                else if (status.SecondsSinceHandshake >= 180)
                {
                    status.State = TunnelState.Reconnecting;
                }
            }
            else if (line.StartsWith("transfer:"))
            {
                var parts = line["transfer:".Length..].Trim().Split(',');
                if (parts.Length >= 2)
                {
                    status.ReceivedBytes = ParseTransfer(parts[0]);
                    status.SentBytes = ParseTransfer(parts[1]);
                }
            }
            else if (line.StartsWith("interface:"))
            {
                status.TunnelName = line["interface:".Length..].Trim();
            }
        }
        if (status.State == TunnelState.Unknown && previous.State == TunnelState.Connecting)
        {
            status.State = TunnelState.Connecting;
        }

        status.Timestamp = DateTimeOffset.UtcNow;
        return status;
    }

    private static double ParseHandshakeSeconds(string text)
    {
        // "1 minute, 2 seconds ago" (inglés) o "hace 1 minuto" — parse robusto de números + unidad.
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s*(second|segundo|minute|minuto|hour|hora)");
        if (!match.Success)
        {
            return -1;
        }

        var value = double.Parse(match.Groups[1].Value);
        return match.Groups[2].Value.StartsWith("second") || match.Groups[2].Value.StartsWith("segundo")
            ? value
            : match.Groups[2].Value.StartsWith("minute") || match.Groups[2].Value.StartsWith("minuto") ? value * 60 : value * 3600;
    }

    private static long ParseTransfer(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"([\d.]+)\s*(B|KiB|MiB|GiB)");
        if (!match.Success)
        {
            return 0;
        }

        var value = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return match.Groups[2].Value switch
        {
            "KiB" => (long)(value * 1024),
            "MiB" => (long)(value * 1024 * 1024),
            "GiB" => (long)(value * 1024 * 1024 * 1024),
            _ => (long)value
        };
    }

    public async Task<TunnelStatus> StartAsync(TunnelStartRequest request, CancellationToken ct = default)
    {
        // 1) Validación dura ANTES de tocar el sistema.
        try
        {
            WireGuardConfigParser.EnsureValid(request.Tunnel);
        }
        catch (WireGuardConfigException ex)
        {
            var rejected = new TunnelStatus
            {
                State = TunnelState.Failed,
                TunnelName = request.TunnelName,
                Detail = "Configuración inválida, no se aplicó nada: " + ex.Message
            };
            SetStatus(rejected);
            Log.Warning("Túnel rechazado por validación: " + ex.Message);
            return rejected;
        }

        var confPath = WriteConfFile(request);
        SetStatus(new TunnelStatus
        {
            State = TunnelState.Connecting,
            TunnelName = request.TunnelName,
            Endpoint = request.Tunnel.Endpoint,
            Detail = "Validado; iniciando servicio de túnel…"
        });

        CommandResult result;
        if (PlatformInfo.IsWindows)
        {
            result = await Task.Run(() => CommandRunner.Run(_wgExePath, new[] { "/installtunnelservice", confPath }, 30_000), ct);
        }
        else
        {
            result = await Task.Run(() => CommandRunner.Run("wg-quick", new[] { "up", confPath }, 30_000), ct);
        }

        if (!result.Success)
        {
            SetStatus(new TunnelStatus
            {
                State = TunnelState.Failed,
                TunnelName = request.TunnelName,
                Endpoint = request.Tunnel.Endpoint,
                Detail = $"Error al iniciar el túnel: {Truncate(result.StdErr, 300)}"
            });
            Log.Error($"Fallo al iniciar túnel WireGuard: {Truncate(result.StdErr, 300)}");
            return _lastStatus;
        }

        Log.Information($"Túnel '{request.TunnelName}' iniciado (herramientas oficiales WireGuard).");
        // Espera breve y consulta de estado real.
        await Task.Delay(1500, ct).ContinueWith(_ => { }, ct);
        return await GetStatusAsync(ct);
    }

    public async Task<TunnelStatus> StopAsync(CancellationToken ct = default)
    {
        var name = _lastStatus.TunnelName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return _lastStatus;
        }

        SetStatus(new TunnelStatus { State = TunnelState.Stopping, TunnelName = name, Endpoint = _lastStatus.Endpoint });

        CommandResult result;
        if (PlatformInfo.IsWindows)
        {
            result = await Task.Run(() => CommandRunner.Run(_wgExePath, new[] { "/uninstalltunnelservice", name }, 30_000), ct);
        }
        else
        {
            var confPath = Path.Combine(_confDirectory, $"{name}.conf");
            result = await Task.Run(() => CommandRunner.Run("wg-quick", new[] { "down", confPath }, 30_000), ct);
        }

        var finalStatus = new TunnelStatus
        {
            State = result.Success ? TunnelState.Disconnected : TunnelState.Failed,
            TunnelName = name,
            Endpoint = _lastStatus.Endpoint,
            Detail = result.Success ? "Túnel detenido." : $"Error al detener: {Truncate(result.StdErr, 300)}"
        };
        SetStatus(finalStatus);
        Log.Information(result.Success ? $"Túnel '{name}' detenido." : $"Fallo al detener túnel '{name}': {Truncate(result.StdErr, 200)}");
        return finalStatus;
    }

    /// <summary>Escribe el .conf (con la clave privada real — permisos restrictivos) para las herramientas oficiales.</summary>
    private string WriteConfFile(TunnelStartRequest request)
    {
        Directory.CreateDirectory(_confDirectory);
        var path = Path.Combine(_confDirectory, $"{request.TunnelName}.conf");
        var conf = new System.Text.StringBuilder();
        conf.AppendLine("[Interface]");
        conf.AppendLine($"PrivateKey = {request.Tunnel.PrivateKey}");
        conf.AppendLine($"Address = {request.Tunnel.Address}");
        if (!string.IsNullOrWhiteSpace(request.Tunnel.Dns))
        {
            conf.AppendLine($"DNS = {request.Tunnel.Dns}");
        }

        conf.AppendLine($"MTU = {(request.Mtu > 0 ? request.Mtu : request.Tunnel.Mtu)}");
        conf.AppendLine();
        conf.AppendLine("[Peer]");
        conf.AppendLine($"PublicKey = {request.Tunnel.PublicKey}");
        if (request.Tunnel.AllowedIps.Count > 0)
        {
            conf.AppendLine($"AllowedIPs = {string.Join(", ", request.Tunnel.AllowedIps)}");
        }

        if (!string.IsNullOrWhiteSpace(request.Tunnel.Endpoint))
        {
            conf.AppendLine($"Endpoint = {request.Tunnel.Endpoint}");
        }

        if (request.Tunnel.PersistentKeepalive > 0)
        {
            conf.AppendLine($"PersistentKeepalive = {request.Tunnel.PersistentKeepalive}");
        }

        File.WriteAllText(path, conf.ToString());
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch { /* best effort */ }
        return path;
    }

    private void SetStatus(TunnelStatus status)
    {
        lock (_gate)
        {
            _lastStatus = status;
        }
        StatusChanged?.Invoke(this, status);
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");
}
