using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using GameRouteOptimizer.App.Infrastructure;
using GameRouteOptimizer.App.Services;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>VM de Configuración: probing, auto-switch, kill switch, MTU/DNS, detección, servicio.</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _svc;

    public SettingsViewModel(AppServices svc)
    {
        _svc = svc;
        var s = svc.Settings;
        _probeIntervalMs = s.ProbeIntervalMs;
        _probeTimeoutMs = s.ProbeTimeoutMs;
        _monitorIntervalMs = s.MonitorIntervalMs;
        _maxProbesPerSecond = s.MaxProbesPerSecond;
        _autoSwitchEnabled = s.AutoSwitchEnabled;
        _minImprovementLatencyMs = s.MinImprovementLatencyMs;
        _switchCooldownSeconds = s.SwitchCooldownSeconds;
        _maxSwitchesPerHour = s.MaxSwitchesPerHour;
        _failbackOnTunnelDown = s.FailbackOnTunnelDown;
        _killSwitchEnabled = s.KillSwitchEnabled;
        _tunnelMtu = s.TunnelMtu;
        _tunnelDns = s.TunnelDns;
        _maxReconnectAttempts = s.MaxReconnectAttempts;
        _extremeLossThresholdPct = s.ExtremeLossThreshold * 100;
        _safeMode = s.SafeMode;
        _allowGameScan = s.AllowGameScan;
        _gameScanRoots = string.Join("\n", s.GameScanRoots);
        _userAcknowledgedGameToS = s.UserAcknowledgedGameToS;

        SaveCommand = new RelayCommand(Save);
        CheckServiceCommand = new AsyncRelayCommand(CheckServiceAsync);
        EnableKillSwitchCommand = new AsyncRelayCommand(EnableKillSwitchAsync);
        DisableKillSwitchCommand = new AsyncRelayCommand(DisableKillSwitchAsync);
    }

    // Probing
    private int _probeIntervalMs;
    public int ProbeIntervalMs { get => _probeIntervalMs; set => SetProperty(ref _probeIntervalMs, value); }

    private int _probeTimeoutMs;
    public int ProbeTimeoutMs { get => _probeTimeoutMs; set => SetProperty(ref _probeTimeoutMs, value); }

    private int _monitorIntervalMs;
    public int MonitorIntervalMs { get => _monitorIntervalMs; set => SetProperty(ref _monitorIntervalMs, value); }

    private double _maxProbesPerSecond;
    public double MaxProbesPerSecond { get => _maxProbesPerSecond; set => SetProperty(ref _maxProbesPerSecond, value); }

    // Auto-switch
    private bool _autoSwitchEnabled;
    public bool AutoSwitchEnabled { get => _autoSwitchEnabled; set => SetProperty(ref _autoSwitchEnabled, value); }

    private double _minImprovementLatencyMs;
    public double MinImprovementLatencyMs { get => _minImprovementLatencyMs; set => SetProperty(ref _minImprovementLatencyMs, value); }

    private int _switchCooldownSeconds;
    public int SwitchCooldownSeconds { get => _switchCooldownSeconds; set => SetProperty(ref _switchCooldownSeconds, value); }

    private int _maxSwitchesPerHour;
    public int MaxSwitchesPerHour { get => _maxSwitchesPerHour; set => SetProperty(ref _maxSwitchesPerHour, value); }

    private bool _failbackOnTunnelDown;
    public bool FailbackOnTunnelDown { get => _failbackOnTunnelDown; set => SetProperty(ref _failbackOnTunnelDown, value); }

    // Túnel
    private bool _killSwitchEnabled;
    public bool KillSwitchEnabled { get => _killSwitchEnabled; set => SetProperty(ref _killSwitchEnabled, value); }

    private int _tunnelMtu;
    public int TunnelMtu { get => _tunnelMtu; set => SetProperty(ref _tunnelMtu, value); }

    private string _tunnelDns;
    public string TunnelDns { get => _tunnelDns; set => SetProperty(ref _tunnelDns, value); }

    private int _maxReconnectAttempts;
    public int MaxReconnectAttempts { get => _maxReconnectAttempts; set => SetProperty(ref _maxReconnectAttempts, value); }

    private double _extremeLossThresholdPct;
    public double ExtremeLossThresholdPct { get => _extremeLossThresholdPct; set => SetProperty(ref _extremeLossThresholdPct, value); }

    // Seguridad / detección
    private bool _safeMode;
    public bool SafeMode { get => _safeMode; set => SetProperty(ref _safeMode, value); }

    private bool _allowGameScan;
    public bool AllowGameScan { get => _allowGameScan; set => SetProperty(ref _allowGameScan, value); }

    private string _gameScanRoots;
    public string GameScanRoots { get => _gameScanRoots; set => SetProperty(ref _gameScanRoots, value); }

    private bool _userAcknowledgedGameToS;
    public bool UserAcknowledgedGameToS { get => _userAcknowledgedGameToS; set => SetProperty(ref _userAcknowledgedGameToS, value); }

    // Servicio
    private string _serviceStatus = "Sin comprobar";
    public string ServiceStatus { get => _serviceStatus; set => SetProperty(ref _serviceStatus, value); }

    private string _wireGuardStatus = "Sin comprobar";
    public string WireGuardStatus { get => _wireGuardStatus; set => SetProperty(ref _wireGuardStatus, value); }

    public ICommand SaveCommand { get; }
    public ICommand CheckServiceCommand { get; }
    public ICommand EnableKillSwitchCommand { get; }
    public ICommand DisableKillSwitchCommand { get; }

    private void Save()
    {
        var s = _svc.Settings;
        s.ProbeIntervalMs = Math.Max(100, ProbeIntervalMs);
        s.ProbeTimeoutMs = Math.Clamp(ProbeTimeoutMs, 200, 10_000);
        s.MonitorIntervalMs = Math.Max(500, MonitorIntervalMs);
        s.MaxProbesPerSecond = Math.Clamp(MaxProbesPerSecond, 0.5, 50);
        s.AutoSwitchEnabled = AutoSwitchEnabled;
        s.MinImprovementLatencyMs = Math.Max(0, MinImprovementLatencyMs);
        s.SwitchCooldownSeconds = Math.Clamp(SwitchCooldownSeconds, 0, 3600);
        s.MaxSwitchesPerHour = Math.Clamp(MaxSwitchesPerHour, 1, 60);
        s.FailbackOnTunnelDown = FailbackOnTunnelDown;
        s.KillSwitchEnabled = KillSwitchEnabled;
        s.TunnelMtu = Math.Clamp(TunnelMtu, 1200, 1500);
        s.TunnelDns = TunnelDns.Trim();
        s.MaxReconnectAttempts = Math.Clamp(MaxReconnectAttempts, 1, 10);
        s.ExtremeLossThreshold = Math.Clamp(ExtremeLossThresholdPct, 1, 90) / 100.0;
        s.SafeMode = SafeMode;
        s.AllowGameScan = AllowGameScan;
        s.GameScanRoots = GameScanRoots.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        s.UserAcknowledgedGameToS = UserAcknowledgedGameToS;
        _svc.SaveSettings();
        _svc.Notifications.Success("Configuración guardada. Algunos cambios aplican en la próxima optimización.");
    }

    private async Task CheckServiceAsync()
    {
        var response = await _svc.ServiceTunnel.SendAsync(new Core.Ipc.IpcMessage(Core.Ipc.IpcMessage.Ping, new { }), 5000);
        ServiceStatus = response is { Type: Core.Ipc.IpcMessage.Pong }
            ? $"Servicio disponible (elevado: ver payload). Pipe: {AppConstants.ServicePipeName}"
            : "Servicio NO disponible. Instálalo con installer\\install-service.ps1 (como administrador). El diagnóstico funciona sin servicio.";

        WireGuardStatus = Core.Platform.PlatformInfo.IsWindows
            ? (WireGuardTunnelManager.IsWireGuardAvailable()
                ? "WireGuard detectado (herramientas oficiales)."
                : "WireGuard NO detectado: instálalo desde wireguard.com (incluye Wintun).")
            : "Plataforma no-Windows: el túnel usa wg-quick si está instalado.";
    }

    private async Task EnableKillSwitchAsync()
    {
        if (!ConfirmKillSwitch())
        {
            return;
        }

        var response = await _svc.ServiceTunnel.SendAsync(new Core.Ipc.IpcMessage(Core.Ipc.IpcMessage.EnableKillSwitch, new { }), 30000);
        ReportKillSwitch(response, "activado");
    }

    private async Task DisableKillSwitchAsync()
    {
        var response = await _svc.ServiceTunnel.SendAsync(new Core.Ipc.IpcMessage(Core.Ipc.IpcMessage.DisableKillSwitch, new { }), 30000);
        ReportKillSwitch(response, "desactivado");
    }

    private bool ConfirmKillSwitch()
    {
        var result = MessageBox.Show(
            "El kill switch BLOQUEA todo el tráfico saliente excepto WireGuard y el servicio, " +
            "para proteger tu IP real si el túnel cae. Si el túnel falla, tu internet quedará aislado " +
            "hasta desactivarlo. ¿Continuar?",
            "Activar kill switch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void ReportKillSwitch(Core.Ipc.IpcMessage? response, string verb)
    {
        if (response is { Type: Core.Ipc.IpcMessage.Ok })
        {
            var payload = response.GetPayload<Dictionary<string, System.Text.Json.JsonElement>>();
            var message = payload is not null && payload.TryGetValue("message", out var m) ? m.GetString() : $"Kill switch {verb}.";
            _svc.Notifications.Success(message ?? $"Kill switch {verb}.");
        }
        else if (response is not null)
        {
            var payload = response.GetPayload<Dictionary<string, System.Text.Json.JsonElement>>();
            var message = payload is not null && payload.TryGetValue("message", out var m) ? m.GetString() : "Error.";
            _svc.Notifications.Error(message ?? "Error en kill switch.");
        }
        else
        {
            _svc.Notifications.Warning("Servicio no disponible: el kill switch requiere el servicio elevado.");
        }
    }
}
