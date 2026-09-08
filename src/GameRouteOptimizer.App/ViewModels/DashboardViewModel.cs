using System.Collections.ObjectModel;
using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.State;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Panel principal: estado, métricas en vivo, gráficos, notificaciones y acciones.</summary>
public sealed class DashboardViewModel : SectionViewModel
{
    private readonly MainViewModel _main;
    private GameProfile? _selectedProfile;
    private string _directLatency = "—";
    private string _directLoss = "—";
    private string _directJitter = "—";
    private string _optimizedLatency = "—";
    private string _optimizedLoss = "—";
    private string _optimizedJitter = "—";
    private string _recommendation = "Añade un juego con servidor objetivo para empezar.";
    private string _confidence = string.Empty;
    private bool _canConfirmRelay;
    private string _activeModeNote = string.Empty;
    private bool _globalModeWarning;
    private string _statusText = "En reposo.";
    private string _lastUpdateText = string.Empty;
    private Mvvm.AsyncRelayCommand? _optimizeCommand;
    private Mvvm.AsyncRelayCommand? _stopCommand;

    public DashboardViewModel(AppServices app, MainViewModel main) : base(app)
    {
        _main = main;
        App.Games.ProfilesChanged += (_, _) => RunOnUi(ReloadGames);
        App.Relays.RelaysChanged += (_, _) => RunOnUi(Refresh);
        App.Orchestrator.RecommendationUpdated += (_, _) => RunOnUi(Refresh);
        App.Notifications.NotificationAdded += (_, notification) =>
            RunOnUi(() => OnNotificationAdded(notification));
        foreach (var notification in App.Notifications.GetRecent(10))
        {
            Notifications.Add(NotificationRow.From(notification));
        }

        ReloadGames();
        Refresh();
    }

    public ObservableCollection<GameProfile> Games { get; } = new();

    public GameProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                OnPropertyChanged(nameof(HasSelectedGame));
                _optimizeCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedGame => SelectedProfile is not null;

    public string DirectLatency { get => _directLatency; private set => SetProperty(ref _directLatency, value); }
    public string DirectLoss { get => _directLoss; private set => SetProperty(ref _directLoss, value); }
    public string DirectJitter { get => _directJitter; private set => SetProperty(ref _directJitter, value); }
    public string OptimizedLatency { get => _optimizedLatency; private set => SetProperty(ref _optimizedLatency, value); }
    public string OptimizedLoss { get => _optimizedLoss; private set => SetProperty(ref _optimizedLoss, value); }
    public string OptimizedJitter { get => _optimizedJitter; private set => SetProperty(ref _optimizedJitter, value); }

    public string RecommendationText
    {
        get => _recommendation;
        private set => SetProperty(ref _recommendation, value);
    }

    public string ConfidenceText
    {
        get => _confidence;
        private set => SetProperty(ref _confidence, value);
    }

    public bool CanConfirmRelay
    {
        get => _canConfirmRelay;
        private set => SetProperty(ref _canConfirmRelay, value);
    }

    public string ActiveModeNote
    {
        get => _activeModeNote;
        private set => SetProperty(ref _activeModeNote, value);
    }

    public bool GlobalModeWarning
    {
        get => _globalModeWarning;
        private set => SetProperty(ref _globalModeWarning, value);
    }

    /// <summary>Frase de estado actual (qué está haciendo el programa ahora).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Hora de la última medición recibida (vacío si aún no hay).</summary>
    public string LastUpdateText
    {
        get => _lastUpdateText;
        private set => SetProperty(ref _lastUpdateText, value);
    }

    /// <summary>true mientras hay una optimización o diagnóstico en curso.</summary>
    public bool IsBusy => App.Orchestrator.IsOptimizing || App.Orchestrator.IsDiagnosticsRunning;

    public ObservableCollection<NotificationRow> Notifications { get; } = new();

    public ObservableCollection<double?> LatencySamples { get; } = new();
    public ObservableCollection<double?> LossSamples { get; } = new();

    private const int MaxSamples = 150;

    public void ReloadGames()
    {
        var selectedId = SelectedProfile?.Id;
        Games.Clear();
        foreach (var profile in App.Games.GetAll())
        {
            Games.Add(profile);
        }

        SelectedProfile = Games.FirstOrDefault(g => g.Id == selectedId) ?? Games.FirstOrDefault();
        Refresh();
    }

    public void Refresh()
    {
        var orchestator = App.Orchestrator;
        var rec = orchestator.LastRecommendation;
        if (rec is not null)
        {
            var winner = rec.Ranked.FirstOrDefault(c => c.CandidateId == rec.WinnerId);
            RecommendationText = winner is null
                ? rec.ExplanationEs
                : $"Recomendación: {winner.DisplayName}. {rec.ExplanationEs}";
            ConfidenceText = $"Confianza: {rec.Confidence * 100:F0} %";
        }

        CanConfirmRelay = orchestator.State == ProgramState.WaitingUser;
        StatusText = DescribeState(orchestator);
        var tunnel = orchestator.ActiveTunnel;
        if (tunnel is not null)
        {
            ActiveModeNote = tunnel.Mode == RouteMode.TunnelGlobal
                ? "⚠ Túnel GLOBAL: todo el tráfico de tu equipo va por el relay."
                : $"Túnel activo solo para los destinos del juego ({tunnel.DisplayName}).";
            GlobalModeWarning = tunnel.Mode == RouteMode.TunnelGlobal;
        }
        else
        {
            ActiveModeNote = orchestator.State is ProgramState.Idle or ProgramState.Error
                ? "Ruta directa (sin túnel)."
                : "Ruta directa en uso…";
            GlobalModeWarning = false;
        }

        OnPropertyChanged(nameof(IsBusy));
        _optimizeCommand?.RaiseCanExecuteChanged();
        _stopCommand?.RaiseCanExecuteChanged();
    }

    private string DescribeState(OptimizationOrchestrator orchestrator)
    {
        return orchestrator.State switch
        {
            ProgramState.Idle => "En reposo: elige un juego y pulsa «Optimizar».",
            ProgramState.ProbingDirect => "Midiendo la ruta directa…",
            ProgramState.ProbingRelays => "Comparando con los relays disponibles…",
            ProgramState.SelectingRoute => "Seleccionando la mejor ruta…",
            ProgramState.WaitingUser => "Relay recomendado: confírmalo para conectarlo.",
            ProgramState.Connecting => "Conectando el túnel WireGuard…",
            ProgramState.Monitoring => orchestrator.ActiveTunnel is { } t
                ? $"Túnel activo por «{t.DisplayName}»: vigilando calidad y estabilidad."
                : "Ruta directa en uso: vigilando si algún relay mejora la conexión.",
            ProgramState.Degraded => "⚠ Túnel degradado: comprobando la conexión…",
            ProgramState.Switching => "Cambiando de ruta…",
            ProgramState.FailingBack => "Volviendo a la ruta directa…",
            ProgramState.Stopping => "Deteniendo y restaurando la red…",
            ProgramState.Error => "Ocurrió un error. La red debería estar restaurada; revisa la sesión.",
            _ => "Trabajando…",
        };
    }

    public void OnMetrics(MetricsSnapshot snapshot)
    {
        var effectiveLatency = snapshot.TunnelActive ? snapshot.Optimized : snapshot.Direct;
        var effectiveLoss = snapshot.TunnelActive ? snapshot.Optimized : snapshot.Direct;
        if (snapshot.Direct is { AvgMs: not null } direct)
        {
            DirectLatency = $"{direct.AvgMs:F1} ms";
            DirectLoss = direct.LossPercent.HasValue ? $"{direct.LossPercent:F1} %" : "—";
            DirectJitter = direct.JitterMs.HasValue ? $"{direct.JitterMs:F1} ms" : "—";
        }

        if (snapshot.Optimized is { AvgMs: not null } optimized)
        {
            OptimizedLatency = $"{optimized.AvgMs:F1} ms";
            OptimizedLoss = optimized.LossPercent.HasValue ? $"{optimized.LossPercent:F1} %" : "—";
            OptimizedJitter = optimized.JitterMs.HasValue ? $"{optimized.JitterMs:F1} ms" : "—";
        }
        else
        {
            OptimizedLatency = "—";
            OptimizedLoss = "—";
            OptimizedJitter = "—";
        }

        PushSample(LatencySamples, effectiveLatency?.AvgMs);
        PushSample(LossSamples, effectiveLoss?.LossPercent);
        LastUpdateText = "Última medición: " + snapshot.Utc.ToLocalTime().ToString("HH:mm:ss");

        Refresh();
    }

    private void OnNotificationAdded(AppNotification notification)
    {
        Notifications.Insert(0, NotificationRow.From(notification));
        while (Notifications.Count > 12)
        {
            Notifications.RemoveAt(Notifications.Count - 1);
        }
    }

    private static void PushSample(ObservableCollection<double?> series, double? value)
    {
        series.Add(value);
        while (series.Count > MaxSamples)
        {
            series.RemoveAt(0);
        }
    }

    // ----- acciones -----

    public Mvvm.AsyncRelayCommand OptimizeCommand =>
        _optimizeCommand ??= new Mvvm.AsyncRelayCommand(OptimizeAsync, _ => !IsBusy && SelectedProfile is not null);

    public Mvvm.AsyncRelayCommand StopCommand =>
        _stopCommand ??= new Mvvm.AsyncRelayCommand(StopAsync, _ => IsBusy);

    public Mvvm.AsyncRelayCommand ConfirmRelayCommand => new(_ =>
    {
        App.Orchestrator.ConfirmPendingRelay();
        CanConfirmRelay = false;
        Refresh();
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand CancelSelectionCommand => new(async _ =>
    {
        await App.Orchestrator.CancelWaitingUserAsync();
        Refresh();
    });

    private async Task OptimizeAsync(object? _)
    {
        if (SelectedProfile is not { } profile)
        {
            MessageBox.Show("Selecciona un juego primero.",
                "GameRoute Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startProblem = OptimizationOrchestrator.FindStartProblem(profile);
        if (startProblem.Length > 0)
        {
            MessageBox.Show(startProblem,
                "No se pudo iniciar la optimización", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (profile.RouteMode == RouteMode.Direct &&
            MessageBox.Show(
                "El perfil está en modo «ruta directa»: se medirá y monitoreará sin túnel.\n" +
                "Para usar un relay, edita el juego y elige «túnel solo destinos del juego» o «túnel global».\n\n" +
                "¿Continuar en modo directo?",
                "Modo ruta directa", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var autoApproved = App.Settings.AutoSwitch.Enabled && profile.RouteMode != RouteMode.Direct;
        App.Orchestrator.RequestOptimization(profile, autoApproved);
        Refresh();
    }

    private async Task StopAsync(object? _)
    {
        await App.Orchestrator.StopOptimizationAsync("el usuario detuvo la optimización");
        Refresh();
    }

    protected override System.Windows.FrameworkElement BuildView() => new DashboardView { DataContext = this };
}

/// <summary>Notificación lista para mostrar en el panel (icono, hora y texto).</summary>
public sealed class NotificationRow
{
    private NotificationRow(AppNotification source)
    {
        Icon = source.Level switch
        {
            Core.Models.EventLevel.Error => "⛔",
            Core.Models.EventLevel.Warning => "⚠",
            _ => "ⓘ",
        };
        Time = source.AtUtc.ToLocalTime().ToString("HH:mm:ss");
        Title = source.Title;
        Message = source.Message;
    }

    public string Icon { get; }
    public string Time { get; }
    public string Title { get; }
    public string Message { get; }

    public static NotificationRow From(AppNotification source) => new(source);
}
