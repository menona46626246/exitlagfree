using System.Windows;
using System.Windows.Media;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.State;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>ViewModel principal: navegación, estado global y botón de emergencia.</summary>
public sealed class MainViewModel : ObservableObjectBase
{
    private readonly AppServices _app;
    private string _stateLabel = "Inactivo";
    private string _stateDetail = "Listo. Añade un juego y un relay para empezar.";
    private Brush _stateBrush = GoodBrush;
    private string _currentGameLabel = "—";
    private FrameworkElement? _content;
    private bool _busy;

    public MainViewModel(AppServices app)
    {
        _app = app;
        Dashboard = new DashboardViewModel(app, this);
        Games = new GamesViewModel(app);
        Relays = new RelaysViewModel(app);
        Diagnostics = new DiagnosticsViewModel(app);
        Session = new SessionViewModel(app);
        Settings = new SettingsViewModel(app);
        Logs = new LogsViewModel(app);

        _app.Orchestrator.StateChanged += (_, args) =>
            _app.RunOnUi(() => OnOrchestratorStateChanged(args));
        _app.Orchestrator.MetricsUpdated += (_, snapshot) =>
            _app.RunOnUi(() => OnMetrics(snapshot));
        _app.Orchestrator.SessionEventAdded += (_, _) => RefreshStatusFromState();
        // Notificaciones del orquestador visibles para el usuario (barra de estado y
        // cuadro de diálogo para errores).
        _app.Notifications.NotificationAdded += (_, notification) =>
            _app.RunOnUi(() => OnNotification(notification));
        // Pinceles del indicador de estado según el tema activo.
        ThemeManager.ThemeChanged += (_, _) => _app.RunOnUi(RefreshStatusFromState);

        _content = Dashboard.View;
        RefreshStatusFromState();
    }

    private void OnNotification(Core.Models.AppNotification notification)
    {
        StateDetail = notification.Level switch
        {
            Core.Models.EventLevel.Error => "⛔ " + notification.Title + ": " + notification.Message,
            Core.Models.EventLevel.Warning => "⚠ " + notification.Title + ": " + notification.Message,
            _ => "ⓘ " + notification.Title + ": " + notification.Message,
        };

        if (notification.Level == Core.Models.EventLevel.Error)
        {
            MessageBox.Show(notification.Message, notification.Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public DashboardViewModel Dashboard { get; }
    public GamesViewModel Games { get; }
    public RelaysViewModel Relays { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public SessionViewModel Session { get; }
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; }

    public FrameworkElement? Content
    {
        get => _content;
        private set => SetProperty(ref _content, value);
    }

    public string StateLabel
    {
        get => _stateLabel;
        private set => SetProperty(ref _stateLabel, value);
    }

    public string StateDetail
    {
        get => _stateDetail;
        private set => SetProperty(ref _stateDetail, value);
    }

    public Brush StateBrush
    {
        get => _stateBrush;
        private set => SetProperty(ref _stateBrush, value);
    }

    public string CurrentGameLabel
    {
        get => _currentGameLabel;
        private set => SetProperty(ref _currentGameLabel, value);
    }

    public bool IsBusy => _app.Orchestrator.IsOptimizing || _app.Orchestrator.IsDiagnosticsRunning;

    public Mvvm.AsyncRelayCommand NavigateDashboard => new(_ => SetSection(Dashboard));
    public Mvvm.AsyncRelayCommand NavigateGames => new(_ => SetSection(Games));
    public Mvvm.AsyncRelayCommand NavigateRelays => new(_ => SetSection(Relays));
    public Mvvm.AsyncRelayCommand NavigateDiagnostics => new(_ => SetSection(Diagnostics));
    public Mvvm.AsyncRelayCommand NavigateSession => new(_ => SetSection(Session));
    public Mvvm.AsyncRelayCommand NavigateSettings => new(_ => SetSection(Settings));
    public Mvvm.AsyncRelayCommand NavigateLogs => new(_ => SetSection(Logs));

    public Mvvm.AsyncRelayCommand EmergencyStopCommand => new(async _ =>
    {
        var result = MessageBox.Show(
            "¿Detener el túnel, desactivar el kill switch y restaurar la red a su estado normal?\n" +
            "Úsalo si algo falla con la conexión.",
            "Detener y restaurar red",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _app.Orchestrator.StopOptimizationAsync("botón de emergencia", emergency: true);
        await _app.Orchestrator.CancelWaitingUserAsync();
        RefreshStatusFromState();
    });

    private Task SetSection(SectionViewModel section)
    {
        Content = section.View;
        return Task.CompletedTask;
    }

    private void OnOrchestratorStateChanged(StateChangedEventArgs args)
    {
        RefreshStatusFromState();
        _app.Log.Info($"Estado: {ProgramStateMachine.ToSpanish(args.From)} → {ProgramStateMachine.ToSpanish(args.To)} ({args.Reason})");
        Dashboard.Refresh();
    }

    private void OnMetrics(Core.Services.MetricsSnapshot snapshot)
    {
        Dashboard.OnMetrics(snapshot);
        Session.OnMetrics(snapshot);
        RefreshStatusFromState();
    }

    public void RefreshStatusFromState()
    {
        var state = _app.Orchestrator.State;
        StateLabel = ProgramStateMachine.ToSpanish(state);
        StateBrush = ThemeManager.Brush(state switch
        {
            ProgramState.Active or ProgramState.Monitoring => "GoodBrush",
            ProgramState.Degraded or ProgramState.WaitingUser => "WarnBrush",
            ProgramState.Error => "BadBrush",
            _ => "MutedTextBrush",
        }, Color.FromRgb(0x8F, 0xA0, 0xB0));
        var profile = _app.Orchestrator.CurrentProfile;
        CurrentGameLabel = profile?.Name ?? "—";
        StateDetail = _app.Orchestrator.ActiveTunnel is { } tunnel
            ? $"Túnel activo: {tunnel.DisplayName}"
            : _app.Orchestrator.LastRecommendation is { WinnerId: not null } rec
                ? rec.ExplanationEs
                : "Listo.";
    }
}

/// <summary>Base para los ViewModels de sección con acceso a servicios y a su vista.</summary>
public abstract class SectionViewModel : ObservableObjectBase
{
    private FrameworkElement? _view;

    protected SectionViewModel(AppServices app)
    {
        App = app;
    }

    protected AppServices App { get; }

    public FrameworkElement View => _view ??= BuildView();

    protected abstract FrameworkElement BuildView();

    protected void RunOnUi(Action action) => App.RunOnUi(action);
}
