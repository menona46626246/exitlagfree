using System.Collections.ObjectModel;
using GameRouteOptimizer.App.Controls;
using GameRouteOptimizer.App.Infrastructure;
using GameRouteOptimizer.Core.Events;
using GameRouteOptimizer.Core.Game;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.StateMachine;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>VM de Dashboard: estado grande, métricas en vivo, botones optimizar/detener/emergencia.</summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly AppServices _svc;

    public DashboardViewModel(AppServices svc)
    {
        _svc = svc;
        LatencySeries = new ObservableCollection<double>();
        LossSeries = new ObservableCollection<double>();

        OptimizeCommand = new AsyncRelayCommand(OptimizeAsync, () => !svc.Orchestrator.IsRunning);
        StopCommand = new RelayCommand(Stop, () => svc.Orchestrator.IsRunning);
        EmergencyStopCommand = new AsyncRelayCommand(EmergencyStopAsync);

        _recommendation = svc.Relays.Relays.Count == 0
            ? "Sin relays WireGuard configurados: modo diagnóstico listo (no requiere VPS ni administrador). Usa Diagnóstico o el Asistente inicial para medir ping, jitter, pérdida y traceroute reales."
            : "Ejecuta un diagnóstico o pulsa 'Optimizar rutas' para comparar tu ruta directa frente a tus relays.";

        svc.Events.Subscribe<AppEvent>(OnAppEvent);
        svc.StateMachine.StateChanged += (_, next) =>
        {
            StateText = OptimizerStateMachine.Translate(next);
            IsRunning = svc.Orchestrator.IsRunning;
        };
    }

    public ObservableCollection<double> LatencySeries { get; }
    public ObservableCollection<double> LossSeries { get; }

    private string _stateText = "Inactivo";
    public string StateText
    {
        get => _stateText;
        set => SetProperty(ref _stateText, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                ((AsyncRelayCommand)OptimizeCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
            }
        }
    }

    private string _activeGame = "—";
    public string ActiveGame { get => _activeGame; set => SetProperty(ref _activeGame, value); }

    private string _selectedRoute = "—";
    public string SelectedRoute { get => _selectedRoute; set => SetProperty(ref _selectedRoute, value); }

    private double _directPing = double.NaN;
    public double DirectPing { get => _directPing; set => SetProperty(ref _directPing, value); }

    private double _optimizedPing = double.NaN;
    public double OptimizedPing { get => _optimizedPing; set => SetProperty(ref _optimizedPing, value); }

    private double _directLoss = double.NaN;
    public double DirectLoss { get => _directLoss; set => SetProperty(ref _directLoss, value); }

    private double _optimizedLoss = double.NaN;
    public double OptimizedLoss { get => _optimizedLoss; set => SetProperty(ref _optimizedLoss, value); }

    private double _jitter = double.NaN;
    public double Jitter { get => _jitter; set => SetProperty(ref _jitter, value); }

    private string _recommendation = "Ejecuta un diagnóstico para ver la recomendación.";
    public string Recommendation { get => _recommendation; set => SetProperty(ref _recommendation, value); }

    public ICommand OptimizeCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand EmergencyStopCommand { get; }

    private async Task OptimizeAsync()
    {
        var profile = _svc.Profiles.Profiles.FirstOrDefault();
        if (profile is null)
        {
            _svc.Notifications.Warning("Añade primero un juego en la pestaña Juegos.");
            return;
        }
        var relay = _svc.Relays.Relays.FirstOrDefault(r => r.IsEnabled);
        if (relay is null)
        {
            _svc.Notifications.Warning("No hay relays activos. Añade uno en la pestaña Relays o usa solo diagnóstico.");
            return;
        }
        var server = profile.Servers.FirstOrDefault();
        if (server is null)
        {
            _svc.Notifications.Warning("El perfil no tiene servidor objetivo configurado.");
            return;
        }
        if (!_svc.Settings.UserAcknowledgedGameToS)
        {
            _svc.Notifications.Warning("Aviso: algunos juegos restringen VPN/routing en sus términos de servicio. Revísalos antes de continuar (se puede confirmar en Configuración).");
            _svc.Settings.UserAcknowledgedGameToS = true;
            _svc.SaveSettings();
        }
        await _svc.Orchestrator.StartOptimizationAsync(profile, server, _svc.Relays.Relays,
            new OptimizationOptions { Automatic = true, ProbeMode = ProbeMode.Quick });
        IsRunning = _svc.Orchestrator.IsRunning;
    }

    private void Stop()
    {
        _ = _svc.Orchestrator.StopAsync("Solicitado por el usuario");
    }

    private async Task EmergencyStopAsync()
    {
        _svc.Notifications.Warning("EMERGENCIA: deteniendo túnel, kill switch y rutas; restaurando red…");
        try
        {
            if (_svc.Orchestrator.IsRunning)
            {
                await _svc.Orchestrator.StopAsync("Emergencia");
            }

            var response = await _svc.ServiceTunnel.SendAsync(
                new Core.Ipc.IpcMessage(Core.Ipc.IpcMessage.EmergencyRestore, new { }));
            if (response is { Type: Core.Ipc.IpcMessage.Ok })
            {
                _svc.Notifications.Success("Red restaurada a la configuración original.");
            }
            else if (response is not null)
            {
                _svc.Notifications.Error("El servicio respondió con error en la restauración.");
            }
            else
            {
                _svc.Notifications.Warning("Servicio no disponible. Comandos manuales: ver docs/solucion-de-problemas.md");
            }
        }
        catch (Exception ex)
        {
            _svc.Notifications.Error($"Emergencia: {ex.Message}");
        }
    }

    private void OnAppEvent(AppEvent evt)
    {
        // Refresco de métricas en vivo desde el orquestador.
        DirectPing = _svc.Orchestrator.Metrics.DirectPingMs;
        OptimizedPing = _svc.Orchestrator.Metrics.OptimizedPingMs;
        DirectLoss = _svc.Orchestrator.Metrics.DirectLoss;
        OptimizedLoss = _svc.Orchestrator.Metrics.OptimizedLoss;
        Jitter = _svc.Orchestrator.Metrics.JitterMs;

        lock (_svc.Orchestrator.Metrics.LatencySeries)
        {
            SyncSeries(LatencySeries, _svc.Orchestrator.Metrics.LatencySeries);
            SyncSeries(LossSeries, _svc.Orchestrator.Metrics.LossSeries);
        }

        var session = _svc.Sessions.Current;
        if (session is not null)
        {
            ActiveGame = session.ProfileName + " → " + session.GameServerTarget;
            SelectedRoute = string.IsNullOrEmpty(session.SelectedRelayName) ? "Ruta directa" : session.SelectedRelayName;
        }
    }

    private static void SyncSeries(ObservableCollection<double> target, IReadOnlyList<double> source)
    {
        if (source.Count == target.Count)
        {
            return;
        }

        if (source.Count < target.Count)
        {
            while (target.Count > source.Count)
            {
                target.RemoveAt(target.Count - 1);
            }

            return;
        }
        for (var i = target.Count; i < source.Count; i++)
        {
            target.Add(source[i]);
        }
    }
}
