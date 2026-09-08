using System.IO;
using System.Windows.Threading;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Privileged;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.State;
using GameRouteOptimizer.Core.Storage;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.App;

/// <summary>
/// Raíz de composición de la aplicación: construye y conecta todos los servicios.
/// Se crea en el hilo de UI (los eventos se re-emiten en el Dispatcher).
/// </summary>
public sealed class AppServices
{
    private static AppServices? _current;
    private readonly Dispatcher _dispatcher;

    public static AppServices Current => _current ?? throw new InvalidOperationException("AppServices no inicializado");

    public ConfigStore Store { get; }
    public LogService Log { get; }
    public NotificationService Notifications { get; }
    public GameManager Games { get; }
    public RelayManager Relays { get; }
    public SessionRecorder Sessions { get; }
    public ProbeEngine Probes { get; }
    public TracerouteEngine Traceroute { get; }
    public TunnelManager Tunnel { get; }
    public KillSwitchManager KillSwitch { get; }
    public ProgramStateMachine State { get; }
    public OptimizationOrchestrator Orchestrator { get; }
    public ProcessWatchdog Watchdog { get; }
    public IPrivilegedOps PrivilegedOps { get; }

    public string DataDirectory { get; }
    public string ExportDirectory { get; }

    private AppSettings _settings;

    public AppServices(Dispatcher dispatcher, string? dataDirectory = null)
    {
        _dispatcher = dispatcher;
        DataDirectory = dataDirectory ?? DefaultDataDirectory();
        Directory.CreateDirectory(DataDirectory);
        ExportDirectory = Path.Combine(DataDirectory, "exports");
        Directory.CreateDirectory(ExportDirectory);

        var dbPath = Path.Combine(DataDirectory, "gro.db");
        Store = new ConfigStore(dbPath);
        Log = new LogService(Path.Combine(DataDirectory, "logs"));
        Notifications = new NotificationService();
        _settings = Store.LoadSettings();
        _settings.DataDirectory = DataDirectory;

        var protector = new DpapiSecretProtector();
        Games = new GameManager(Store);
        Relays = new RelayManager(Store, protector);
        Sessions = new SessionRecorder(Store);

        PrivilegedOps = CreatePrivilegedOps();
        var probingSettings = () => Settings.Probing;
        Probes = new ProbeEngine(new SystemProbeTransport(), _settings.Probing);
        Traceroute = new TracerouteEngine(new SystemProbeTransport());
        Tunnel = new TunnelManager(PrivilegedOps);
        KillSwitch = new KillSwitchManager(PrivilegedOps);
        State = new ProgramStateMachine();
        Orchestrator = new OptimizationOrchestrator(
            () => Settings,
            Log,
            Notifications,
            Sessions,
            Games,
            Relays,
            Probes,
            Traceroute,
            Tunnel,
            KillSwitch,
            State,
            Store);

        Watchdog = new ProcessWatchdog(Games);
        Watchdog.GameLaunched += (_, e) =>
        {
            var profile = Games.GetById(e.ProfileId);
            if (profile is null || !profile.AutoStartOptimization)
            {
                return;
            }

            RunOnUi(() =>
            {
                Log.Info($"Juego detectado: {profile.Name} — iniciando optimización automática.");
                Orchestrator.RequestOptimization(profile,
                    autoApproved: Settings.AutoSwitch.Enabled);
            });
        };
        Watchdog.GameExited += (_, e) =>
        {
            var profile = Games.GetById(e.ProfileId);
            if (profile is null || !profile.AutoStopOnExit)
            {
                return;
            }

            RunOnUi(async () =>
            {
                Log.Info($"Juego cerrado: {profile.Name} — deteniendo optimización.");
                await Orchestrator.StopOptimizationAsync("el juego se cerró");
            });
        };

        _current = this;
        Log.Info("GameRoute Optimizer iniciado. Directorio de datos: " + DataDirectory);
    }

    public AppSettings Settings => _settings;

    /// <summary>Recarga la configuración tras guardarla (ajustes de UI/red).</summary>
    public void ReloadSettings()
    {
        _settings = Store.LoadSettings();
        _settings.DataDirectory = DataDirectory;
    }

    public void SaveSettings(AppSettings settings)
    {
        settings.DataDirectory = DataDirectory;
        Store.SaveSettings(settings);
        ReloadSettings();
    }

    /// <summary>Servicio de logs en caliente para cambios de configuración.</summary>
    public void ApplyRuntimeSettings(AppSettings settings)
    {
        // v1: se aplica el cambio de nivel de logs solo a partir de la próxima creación.
    }

    private IPrivilegedOps CreatePrivilegedOps()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UnavailablePrivilegedOps("Las operaciones de red privilegiadas requieren Windows.");
        }

        var baseDir = AppContext.BaseDirectory;
        var helper = Path.Combine(baseDir, "GameRouteOptimizer.Cli.exe");
        if (!File.Exists(helper))
        {
            // En desarrollo (dotnet run) el helper está en su propia carpeta de salida.
            var devHelper = Path.Combine(
                Path.GetDirectoryName(baseDir) ?? baseDir,
                "GameRouteOptimizer.Cli",
                "bin",
                "Release",
                "net8.0",
                "GameRouteOptimizer.Cli.exe");
            if (File.Exists(devHelper))
            {
                helper = devHelper;
            }
        }

        return new CliElevatedRunner(File.Exists(helper) ? helper : baseDir);
    }

    public static string DefaultDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(Path.GetTempPath(), "GameRouteOptimizer");
        }

        return Path.Combine(baseDir, "GameRouteOptimizer");
    }

    public void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action);
        }
    }

    public void RunOnUiAsync(Func<Task> action)
    {
        if (_dispatcher.CheckAccess())
        {
            _ = action();
        }
        else
        {
            _ = _dispatcher.InvokeAsync(action);
        }
    }

    public async Task StopEverythingAsync(string reason)
    {
        await Orchestrator.StopOptimizationAsync(reason, emergency: false);
        Watchdog.Dispose();
    }

    private sealed class UnavailablePrivilegedOps : IPrivilegedOps
    {
        private readonly string _message;

        public UnavailablePrivilegedOps(string message)
        {
            _message = message;
        }

        public bool IsAvailable => false;

        public Task<PrivilegedOpResult> RunAsync(PrivilegedOp op, CancellationToken ct) =>
            Task.FromResult(PrivilegedOpResult.Failure(_message));
    }
}
