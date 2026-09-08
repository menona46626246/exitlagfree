using System.Collections.ObjectModel;
using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.State;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Panel principal: estado, métricas en vivo, gráficos y acciones.</summary>
public sealed class DashboardViewModel : SectionViewModel
{
    private readonly MainViewModel _main;
    private GameProfile? _selectedProfile;
    private string _directLatency = "—";
    private string _directLoss = "—";
    private string _directJitter = "—";
    private string _optimizedLatency = "—";
    private string _optimizedLoss = "—";
    private string _recommendation = "Añade un juego con servidor objetivo para empezar.";
    private string _confidence = string.Empty;
    private bool _canConfirmRelay;
    private string _activeModeNote = string.Empty;
    private bool _globalModeWarning;

    public DashboardViewModel(AppServices app, MainViewModel main) : base(app)
    {
        _main = main;
        App.Games.ProfilesChanged += (_, _) => RunOnUi(ReloadGames);
        App.Relays.RelaysChanged += (_, _) => RunOnUi(Refresh);
        App.Orchestrator.RecommendationUpdated += (_, _) => RunOnUi(Refresh);
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
            }
        }
    }

    public bool HasSelectedGame => SelectedProfile is not null;

    public string DirectLatency { get => _directLatency; private set => SetProperty(ref _directLatency, value); }
    public string DirectLoss { get => _directLoss; private set => SetProperty(ref _directLoss, value); }
    public string DirectJitter { get => _directJitter; private set => SetProperty(ref _directJitter, value); }
    public string OptimizedLatency { get => _optimizedLatency; private set => SetProperty(ref _optimizedLatency, value); }
    public string OptimizedLoss { get => _optimizedLoss; private set => SetProperty(ref _optimizedLoss, value); }

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
        }
        else
        {
            OptimizedLatency = "—";
            OptimizedLoss = "—";
        }

        PushSample(LatencySamples, effectiveLatency?.AvgMs);
        PushSample(LossSamples, effectiveLoss?.LossPercent);

        Refresh();
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

    public Mvvm.AsyncRelayCommand OptimizeCommand => new(async _ =>
    {
        if (SelectedProfile is not { } profile)
        {
            MessageBox.Show("Selecciona un juego primero.",
                "GameRoute Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startProblem = GameRouteOptimizer.Core.Services.OptimizationOrchestrator.FindStartProblem(profile);
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
    });

    public Mvvm.AsyncRelayCommand StopCommand => new(async _ =>
    {
        await App.Orchestrator.StopOptimizationAsync("el usuario detuvo la optimización");
        Refresh();
    });

    public Mvvm.AsyncRelayCommand ConfirmRelayCommand => new(_ =>
    {
        App.Orchestrator.ConfirmPendingRelay();
        CanConfirmRelay = false;
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand CancelSelectionCommand => new(async _ =>
    {
        await App.Orchestrator.CancelWaitingUserAsync();
        Refresh();
    });

    protected override System.Windows.FrameworkElement BuildView() => new DashboardView { DataContext = this };
}
