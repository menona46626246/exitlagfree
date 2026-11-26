using System.Text.Json;
using GameRouteOptimizer.Core.Ipc;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.Tunneling;
using GameRouteOptimizer.Service.PrivilegedOperations;
using GameRouteOptimizer.Service.RouteService;

namespace GameRouteOptimizer.Service;

/// <summary>
/// Servicio elevado de GameRoute Optimizer.
/// - Expone un servidor IPC por named pipe local "gameroute-optimizer-svc".
/// - Gestiona túneles WireGuard con herramientas oficiales.
/// - Aplica/restaura rutas con snapshot del estado original (persistido en disco).
/// - Activa/desactiva el kill switch por firewall.
/// - Botón de emergencia: detiene todo y restaura red.
/// Ejecutar como administrador. Ver installer/install-service.ps1.
/// </summary>
public static class Program
{
    public const string PipeName = "gameroute-optimizer-svc";
    private const string StateFile = "route-snapshot.json";

    private static WireGuardTunnelManager _tunnelManager = null!;
    private static KillSwitchManager _killSwitch = null!;
    private static RouteCalculator _routeCalculator = null!;
    private static RoutePlan? _activePlan;
    private static readonly object Gate = new();

    public static async Task<int> Main(string[] args)
    {
        // Logging a archivo local (sin telemetría).
        var dataDir = Environment.GetEnvironmentVariable("GAMEROUTE_DATA")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameRouteOptimizer");
        Directory.CreateDirectory(dataDir);
        var logService = new LogService(Path.Combine(dataDir, "logs"), LogLevel.Information);
        LogService.SetDefault(logService);

        Log.Information("=== GameRoute Optimizer Service v1.0.0 ===");
        Log.Information($"SO: {Environment.OSVersion} · Elevado: {PlatformInfo.IsElevated}");

        if (args.Contains("--status"))
        {
            Console.WriteLine($"elevated={PlatformInfo.IsElevated}");
            return 0;
        }

        if (!PlatformInfo.IsElevated)
        {
            Log.Warning("El servicio NO se ejecuta con privilegios elevados: las operaciones de red fallarán.");
            Console.Error.WriteLine("ADVERTENCIA: ejecuta este servicio como administrador para poder aplicar túneles, rutas y kill switch.");
        }

        _tunnelManager = new WireGuardTunnelManager();
        _killSwitch = new KillSwitchManager(
            new NetshFirewallExecutor(),
            () => WireGuardTunnelManager.ResolveWireGuardPath(),
            () => Path.Combine(AppContext.BaseDirectory, "GameRouteOptimizer.Service.exe"));
        _routeCalculator = new RouteCalculator(new SystemRouteApplier());

        // Restaurar snapshot de rutas si el servicio se cayó sin limpiar (arranque seguro).
        await RestoreSnapshotIfExistsAsync(dataDir);

        var server = new NamedPipeIpcServer(PipeName);
        server.RegisterHandler(IpcMessage.Ping, OnPing);
        server.RegisterHandler(IpcMessage.Status, OnStatus);
        server.RegisterHandler(IpcMessage.StartTunnel, OnStartTunnel);
        server.RegisterHandler(IpcMessage.StopTunnel, OnStopTunnel);
        server.RegisterHandler(IpcMessage.TunnelStatus, OnTunnelStatus);
        server.RegisterHandler(IpcMessage.ApplyRoutes, OnApplyRoutes);
        server.RegisterHandler(IpcMessage.RestoreRoutes, OnRestoreRoutes);
        server.RegisterHandler(IpcMessage.EnableKillSwitch, OnEnableKillSwitch);
        server.RegisterHandler(IpcMessage.DisableKillSwitch, OnDisableKillSwitch);
        server.RegisterHandler(IpcMessage.EmergencyRestore, OnEmergencyRestore);

        server.Start();
        Log.Information($"Servidor IPC escuchando en pipe '{PipeName}'.");

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; StopAll().Wait(); Environment.Exit(0); };

        Log.Information("Servicio listo. Ctrl+C para detener (restaura la red).");
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    // ---------- Handlers IPC ----------

    private static IpcMessage OnPing(IpcMessage msg) =>
        new(IpcMessage.Pong, new { service = "GameRouteOptimizer", version = "1.0.0", elevated = PlatformInfo.IsElevated, time = DateTimeOffset.UtcNow });

    private static IpcMessage OnStatus(IpcMessage msg)
    {
        var status = new ServiceStatusPayload
        {
            Elevated = PlatformInfo.IsElevated,
            TunnelActive = _tunnelManager.GetStatusAsync().GetAwaiter().GetResult().IsConnected,
            TunnelState = _tunnelManager.GetStatusAsync().GetAwaiter().GetResult().State.ToString(),
            KillSwitchActive = _killSwitch.CheckActiveAsync().GetAwaiter().GetResult(),
            WireGuardAvailable = WireGuardTunnelManager.IsWireGuardAvailable() ? "yes" : "no",
            Message = PlatformInfo.IsElevated
                ? "Servicio operativo."
                : "El servicio no tiene privilegios elevados: no puede aplicar túneles ni rutas."
        };
        return new IpcMessage(IpcMessage.Status, status);
    }

    private static IpcMessage OnStartTunnel(IpcMessage msg)
    {
        var payload = msg.GetPayload<TunnelRequestPayload>();
        if (payload?.Tunnel is null)
        {
            return ErrorMsg("Petición de túnel sin configuración.");
        }

        try
        {
            WireGuardConfigParser.EnsureValid(payload.Tunnel);
        }
        catch (Exception ex)
        {
            return ErrorMsg("Configuración inválida: " + ex.Message);
        }
        var status = _tunnelManager.StartAsync(new TunnelStartRequest
        {
            Tunnel = payload.Tunnel,
            TunnelName = payload.TunnelName,
            Mtu = payload.Tunnel.Mtu
        }).GetAwaiter().GetResult();
        return new IpcMessage(IpcMessage.TunnelStatus, status);
    }

    private static IpcMessage OnStopTunnel(IpcMessage msg)
    {
        var status = _tunnelManager.StopAsync().GetAwaiter().GetResult();
        return new IpcMessage(IpcMessage.TunnelStatus, status);
    }

    private static IpcMessage OnTunnelStatus(IpcMessage msg)
    {
        var status = _tunnelManager.GetStatusAsync().GetAwaiter().GetResult();
        return new IpcMessage(IpcMessage.TunnelStatus, status);
    }

    private static IpcMessage OnApplyRoutes(IpcMessage msg)
    {
        if (!PrivilegedOps.HasPrivileges)
        {
            return ErrorMsg("Sin privilegios para aplicar rutas (eleva el servicio).");
        }

        var payload = msg.GetPayload<RoutesPayload>();
        if (payload is null)
        {
            return ErrorMsg("Petición de rutas vacía.");
        }

        var plan = new RoutePlan
        {
            AddRoutes = payload.AddRoutes,
            TunnelAddress = payload.TunnelAddress,
            TunnelInterfaceIndex = payload.TunnelInterfaceIndex,
            TunnelName = payload.TunnelName,
            OriginalRoutes = payload.OriginalRoutes.Select(r => new RouteEntry(r.DestinationCidr, r.Gateway, r.InterfaceIndex, r.InterfaceName, r.Metric)).ToList()
        };

        var applied = _routeCalculator.ApplyPlanAsync(plan).GetAwaiter().GetResult();
        lock (Gate)
        {
            _activePlan = plan;
        }

        SnapshotPlan(plan);
        return new IpcMessage(IpcMessage.Ok, new { applied, message = $"Rutas aplicadas: {applied}." });
    }

    private static IpcMessage OnRestoreRoutes(IpcMessage msg)
    {
        if (!PrivilegedOps.HasPrivileges)
        {
            return ErrorMsg("Sin privilegios para restaurar rutas.");
        }

        RoutePlan? plan;
        lock (Gate)
        {
            plan = _activePlan;
        }

        if (plan is null)
        {
            return new IpcMessage(IpcMessage.Ok, new { restored = 0, message = "No hay plan activo: nada que restaurar." });
        }

        var restored = _routeCalculator.RestorePlanAsync(plan).GetAwaiter().GetResult();
        lock (Gate)
        {
            _activePlan = null;
        }

        DeleteSnapshot();
        return new IpcMessage(IpcMessage.Ok, new { restored, message = $"Rutas restauradas ({restored} re-agregadas)." });
    }

    private static IpcMessage OnEnableKillSwitch(IpcMessage msg)
    {
        if (!PlatformInfo.IsWindows)
        {
            return ErrorMsg("El kill switch por firewall solo está implementado en Windows.");
        }

        if (!PrivilegedOps.HasPrivileges)
        {
            return ErrorMsg("Sin privilegios para el kill switch.");
        }

        var message = _killSwitch.EnableAsync().GetAwaiter().GetResult();
        return new IpcMessage(IpcMessage.Ok, new { message });
    }

    private static IpcMessage OnDisableKillSwitch(IpcMessage msg)
    {
        var message = _killSwitch.DisableAsync().GetAwaiter().GetResult();
        return new IpcMessage(IpcMessage.Ok, new { message });
    }

    private static IpcMessage OnEmergencyRestore(IpcMessage msg)
    {
        Log.Warning("RESTAURACIÓN DE EMERGENCIA solicitada.");
        var messages = new List<string>();
        try
        {
            var tunnel = _tunnelManager.StopAsync().GetAwaiter().GetResult();
            messages.Add($"Túnel: {tunnel.State}.");
        }
        catch (Exception ex) { messages.Add("Túnel: error " + ex.Message); }

        try
        {
            var ks = _killSwitch.DisableAsync().GetAwaiter().GetResult();
            messages.Add(ks);
        }
        catch (Exception ex) { messages.Add("Kill switch: error " + ex.Message); }

        try
        {
            RoutePlan? plan;
            lock (Gate)
            {
                plan = _activePlan;
            }

            if (plan is not null)
            {
                var restored = _routeCalculator.RestorePlanAsync(plan).GetAwaiter().GetResult();
                lock (Gate)
                {
                    _activePlan = null;
                }

                DeleteSnapshot();
                messages.Add($"Rutas restauradas ({restored}).");
            }
            else
            {
                messages.Add("Sin plan de rutas activo.");
            }
        }
        catch (Exception ex) { messages.Add("Rutas: error " + ex.Message); }

        PrivilegedOps.FlushDnsAsync().Wait(5000);
        return new IpcMessage(IpcMessage.Ok, new { message = string.Join(" ", messages) });
    }

    private static IpcMessage ErrorMsg(string message) =>
        new(IpcMessage.Error, new { message });

    // ---------- Snapshot de rutas (protección ante caída del servicio) ----------

    private static void SnapshotPlan(RoutePlan plan)
    {
        try
        {
            var dataDir = GetDataDir();
            var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            ConfigStore.WriteAtomicPublic(Path.Combine(dataDir, StateFile), json);
        }
        catch (Exception ex)
        {
            Log.Warning($"No se pudo guardar el snapshot de rutas: {ex.Message}");
        }
    }

    private static void DeleteSnapshot()
    {
        try
        {
            var path = Path.Combine(GetDataDir(), StateFile);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    private static async Task RestoreSnapshotIfExistsAsync(string dataDir)
    {
        var path = Path.Combine(dataDir, StateFile);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var plan = JsonSerializer.Deserialize<RoutePlan>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (plan is null)
            {
                return;
            }

            Log.Warning("Snapshot de rutas previo encontrado: restaurando red a estado original…");
            await _routeCalculator.RestorePlanAsync(plan);
            File.Delete(path);
            Log.Information("Red restaurada tras arranque del servicio.");
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo restaurar el snapshot de rutas: {ex.Message}");
        }
    }

    private static string GetDataDir()
        => Environment.GetEnvironmentVariable("GAMEROUTE_DATA")
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameRouteOptimizer");

    private static async Task StopAll()
    {
        try
        {
            await _tunnelManager.StopAsync();
            RoutePlan? plan;
            lock (Gate)
            {
                plan = _activePlan;
            }

            if (plan is not null)
            {
                await _routeCalculator.RestorePlanAsync(plan);
            }

            await _killSwitch.DisableAsync();
            Log.Information("Servicio detenido; red restaurada.");
        }
        catch (Exception ex)
        {
            Log.Error($"Error al detener: {ex.Message}");
        }
    }
}
