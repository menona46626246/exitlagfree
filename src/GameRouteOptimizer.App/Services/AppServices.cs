using System.IO;
using GameRouteOptimizer.Core.Events;
using GameRouteOptimizer.Core.Game;
using GameRouteOptimizer.Core.Ipc;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Profiles;
using GameRouteOptimizer.Core.Relay;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Scoring;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.StateMachine;
using GameRouteOptimizer.Core.Storage;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.App.Services;

/// <summary>
/// Punto de composición de la App (DI manual). Decide el modo de operación:
/// - Diagnóstico SIEMPRE en proceso (no requiere elevación).
/// - Túneles/rutas/kill switch vía el Servicio elevado por IPC; si no está disponible,
///   se informa al usuario y la app funciona en modo diagnóstico.
/// </summary>
public sealed class AppServices
{
    public static AppServices? Current { get; private set; }

    public EventBus Events { get; } = new();
    public LogService Logs { get; }
    public ConfigStore Config { get; }
    public AppSettings Settings { get; private set; }
    public DataStore Store { get; }
    public ProfileManager Profiles { get; }
    public RelayManager Relays { get; }
    public GameManager Games { get; }
    public NotificationService Notifications { get; }
    public SessionDatabase SessionDb { get; }
    public SessionRecorder Sessions { get; }
    public ProbeEngine ProbeEngine { get; }
    public TracerouteEngine Traceroute { get; }
    public OptimizerStateMachine StateMachine { get; }
    public OptimizerOrchestrator Orchestrator { get; }
    public RelayProber RelayProber { get; }
    public IpcTunnelManager ServiceTunnel { get; }

    public string DataDirectory { get; }
    public bool ServiceAvailable => ServiceTunnel.LastCheckOk;

    public AppServices()
    {
        Current = this;

        DataDirectory = Environment.GetEnvironmentVariable("GAMEROUTE_DATA") ?? PlatformInfo.DefaultDataDirectory();
        Directory.CreateDirectory(DataDirectory);

        Config = new ConfigStore(DataDirectory);
        Settings = Config.Load();

        Logs = new LogService(Path.Combine(DataDirectory, "logs"), ParseLevel());
        LogService.SetDefault(Logs);

        Store = new DataStore(DataDirectory);
        var secrets = new SecretProtector(DataDirectory);
        Profiles = new ProfileManager(Store);
        Relays = new RelayManager(Store, secrets);
        Games = new GameManager(Profiles);
        Notifications = new NotificationService(Events);

        SessionDb = new SessionDatabase(DataDirectory);
        Sessions = new SessionRecorder(SessionDb, Events);

        var probeOptions = new ProbeOptions(Settings);
        ProbeEngine = new ProbeEngine(probeOptions);
        Traceroute = new TracerouteEngine(Settings.ProbeTimeoutMs, 20, 2);
        StateMachine = new OptimizerStateMachine(Events);

        ServiceTunnel = new IpcTunnelManager();
        var analyzer = new RouteAnalyzer(ProbeEngine, new ScoreEngine(extremeLossThreshold: Settings.ExtremeLossThreshold), Sessions);
        Orchestrator = new OptimizerOrchestrator(
            Events, StateMachine, ProbeEngine, Traceroute, analyzer,
            ServiceTunnel, Sessions, Settings,
            privateKeyResolver: relay => Relays.GetDecryptedPrivateKey(relay));

        RelayProber = new RelayProber(ProbeEngine);

        Events.Subscribe<AppEvent>(evt => Logs.Write(ToLevel(evt.Severity), evt.Message));
    }

    /// <summary>Guarda settings (llamar tras modificarlos desde Configuración).</summary>
    public void SaveSettings()
    {
        Config.Save(Settings);
    }

    /// <summary>Consulta el estado del servicio elevado (IPC); actualiza ServiceAvailable.</summary>
    public async Task<ServiceStatusPayload?> RefreshServiceStatusAsync()
    {
        var response = await ServiceTunnel.SendAsync(new IpcMessage(IpcMessage.Ping, new { }));
        return ServiceTunnel.LastCheckOk
            ? response?.GetPayload<ServiceStatusPayload>()
            : null;
    }

    private static LogLevel ToLevel(EventSeverity severity) => severity switch
    {
        EventSeverity.Error => LogLevel.Error,
        EventSeverity.Warning => LogLevel.Warning,
        _ => LogLevel.Information
    };

    private LogLevel ParseLevel() => Settings?.LogLevel switch
    {
        "Debug" => LogLevel.Debug,
        "Warning" => LogLevel.Warning,
        "Error" => LogLevel.Error,
        _ => LogLevel.Information
    };
}

/// <summary>Constantes compartidas de la App.</summary>
public static class AppConstants
{
    /// <summary>Nombre del pipe local hacia el servicio elevado (misma constante que el Servicio).</summary>
    public const string ServicePipeName = "gameroute-optimizer-svc";
}

/// <summary>
/// ITunnelManager que delega en el Servicio elevado por named pipes locales.
/// Si el servicio no responde, devuelve estado con detalle explicativo.
/// La clave privada viaja solo por el pipe local (protegido por el SO).
/// </summary>
public sealed class IpcTunnelManager : ITunnelManager
{
    private readonly NamedPipeIpcClient _client = new(AppConstants.ServicePipeName);

    public bool LastCheckOk { get; private set; }

    public event EventHandler<TunnelStatus>? StatusChanged;

    public async Task<IpcMessage?> SendAsync(IpcMessage message, int timeoutMs = 20000)
    {
        var response = await _client.SendAsync(message, timeoutMs);
        LastCheckOk = response is not null;
        return response;
    }

    public async Task<TunnelStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(new IpcMessage(IpcMessage.TunnelStatus, new { }));
        if (response?.GetPayload<TunnelStatus>() is { } status) return status;
        return new TunnelStatus { State = TunnelState.Unknown, Detail = "Servicio no disponible." };
    }

    public async Task<TunnelStatus> StartAsync(TunnelStartRequest request, CancellationToken ct = default)
    {
        var payload = new TunnelRequestPayload
        {
            TunnelName = request.TunnelName,
            Dto = new TunnelProfileDto
            {
                Name = request.Tunnel.Name,
                PrivateKey = request.Tunnel.PrivateKey,
                Address = request.Tunnel.Address,
                Dns = request.Tunnel.Dns,
                Mtu = request.Mtu > 0 ? request.Mtu : request.Tunnel.Mtu,
                PublicKey = request.Tunnel.PublicKey,
                AllowedIps = request.Tunnel.AllowedIps,
                Endpoint = request.Tunnel.Endpoint,
                PersistentKeepalive = request.Tunnel.PersistentKeepalive
            }
        };
        var response = await SendAsync(new IpcMessage(IpcMessage.StartTunnel, payload), 40000);
        if (response?.Type == IpcMessage.TunnelStatus && response.GetPayload<TunnelStatus>() is { } status)
        {
            StatusChanged?.Invoke(this, status);
            return status;
        }
        var detail = response?.GetPayload<Dictionary<string, object>>()?.TryGetValue("message", out var m) == true
            ? m.ToString() : "Servicio no disponible: instala el servicio (installer/install-service.ps1) y ejecútalo como administrador.";
        return new TunnelStatus { State = TunnelState.Failed, TunnelName = request.TunnelName, Detail = detail };
    }

    public async Task<TunnelStatus> StopAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(new IpcMessage(IpcMessage.StopTunnel, new { }), 20000);
        if (response?.GetPayload<TunnelStatus>() is { } status)
        {
            StatusChanged?.Invoke(this, status);
            return status;
        }
        return new TunnelStatus { State = TunnelState.Unknown, Detail = "Servicio no disponible." };
    }
}
