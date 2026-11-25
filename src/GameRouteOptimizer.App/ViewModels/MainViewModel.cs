using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using GameRouteOptimizer.App.Infrastructure;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>VM de Logs: ring buffer en vivo, filtros, exportar y limpiar.</summary>
public sealed class LogsViewModel : ViewModelBase
{
    private readonly AppServices _svc;
    private LogLevel _filter = LogLevel.Debug;

    public LogsViewModel(AppServices svc)
    {
        _svc = svc;
        Entries = new ObservableCollection<LogEntry>(svc.Logs.Snapshot());
        svc.Logs.EntryAdded += entry => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (entry.Level >= Filter) Entries.Add(entry);
            while (Entries.Count > 1000) Entries.RemoveAt(0);
        });

        ExportCommand = new RelayCommand(Export);
        ClearCommand = new RelayCommand(Clear);
    }

    public ObservableCollection<LogEntry> Entries { get; }

    public LogLevel Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value)) return;
            Entries.Clear();
            foreach (var e in _svc.Logs.Snapshot().Where(e => e.Level >= value)) Entries.Add(e);
        }
    }

    public Array FilterOptions { get; } = Enum.GetValues<LogLevel>();

    public ICommand ExportCommand { get; }
    public ICommand ClearCommand { get; }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar logs",
            FileName = $"gameroute-logs-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = "Log (*.log)|*.log|Texto (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() is not true) return;
        var path = _svc.Logs.ExportTo(Path.GetDirectoryName(dialog.FileName)!);
        if (path is not null)
        {
            var target = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileName(path));
            if (target != dialog.FileName) File.Move(target, dialog.FileName, overwrite: true);
            _svc.Notifications.Success($"Logs exportados: {dialog.FileName}");
        }
    }

    private void Clear() => _svc.Logs.Clear();
}

/// <summary>VM raíz: navegación lateral entre pantallas.</summary>
public sealed class MainViewModel : ViewModelBase
{
    public MainViewModel(AppServices svc)
    {
        Services = svc;
        Dashboard = new DashboardViewModel(svc);
        Games = new GamesViewModel(svc);
        Relays = new RelaysViewModel(svc);
        Diagnostics = new DiagnosticsViewModel(svc);
        Session = new SessionViewModel(svc);
        Settings = new SettingsViewModel(svc);
        Logs = new LogsViewModel(svc);

        NavigateDashboardCommand = new RelayCommand(() => CurrentView = Dashboard);
        NavigateGamesCommand = new RelayCommand(() => CurrentView = Games);
        NavigateRelaysCommand = new RelayCommand(() => CurrentView = Relays);
        NavigateDiagnosticsCommand = new RelayCommand(() => CurrentView = Diagnostics);
        NavigateSessionCommand = new RelayCommand(() => CurrentView = Session);
        NavigateSettingsCommand = new RelayCommand(() => CurrentView = Settings);
        NavigateLogsCommand = new RelayCommand(() => CurrentView = Logs);

        CurrentView = Dashboard;

        // Estado del servicio al arrancar (async, sin bloquear).
        _ = Task.Run(async () =>
        {
            var ok = await svc.ServiceTunnel.SendAsync(
                new Core.Ipc.IpcMessage(Core.Ipc.IpcMessage.Ping, new { }), 4000);
            var message = ok is not null
                ? "Servicio elevado conectado: túneles, rutas y kill switch disponibles."
                : "Servicio elevado NO disponible: modo diagnóstico (sin túnel). Instálalo con installer\\install-service.ps1.";
            svc.Events.Publish(new Core.Events.AppEvent
            {
                Severity = ok is not null ? Core.Models.EventSeverity.Success : Core.Models.EventSeverity.Warning,
                Message = message
            });
        });
    }

    public AppServices Services { get; }
    public DashboardViewModel Dashboard { get; }
    public GamesViewModel Games { get; }
    public RelaysViewModel Relays { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public SessionViewModel Session { get; }
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; }

    private ViewModelBase _currentView = null!;
    public ViewModelBase CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public string WindowTitle => $"GameRoute Optimizer — {Core.StateMachine.OptimizerStateMachine.Translate(Services.StateMachine.Current)}";

    public ICommand NavigateDashboardCommand { get; }
    public ICommand NavigateGamesCommand { get; }
    public ICommand NavigateRelaysCommand { get; }
    public ICommand NavigateDiagnosticsCommand { get; }
    public ICommand NavigateSessionCommand { get; }
    public ICommand NavigateSettingsCommand { get; }
    public ICommand NavigateLogsCommand { get; }
}
