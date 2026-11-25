using System.Collections.ObjectModel;
using System.Windows.Input;
using GameRouteOptimizer.App.Infrastructure;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>VM de Diagnóstico: probes, traceroute, comparación directa vs relays.</summary>
public sealed class DiagnosticsViewModel : ViewModelBase
{
    private readonly AppServices _svc;

    private string _host = string.Empty;
    private string _port = "80";
    private ProbeProtocol _protocol = ProbeProtocol.Auto;
    private ProbeMode _mode = ProbeMode.Quick;
    private string _recommendation = string.Empty;
    private ProbeSummary? _lastSummary;

    public DiagnosticsViewModel(AppServices svc)
    {
        _svc = svc;
        HopLines = new ObservableCollection<string>();
        ScoreLines = new ObservableCollection<string>();

        RunProbeCommand = new AsyncRelayCommand(RunProbeAsync, () => !IsBusy);
        RunTraceCommand = new AsyncRelayCommand(RunTraceAsync, () => !IsBusy);
        CompareCommand = new AsyncRelayCommand(CompareAsync, () => !IsBusy);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public string Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public ProbeProtocol Protocol
    {
        get => _protocol;
        set => SetProperty(ref _protocol, value);
    }

    public ProbeMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    public Array ProtocolOptions { get; } = Enum.GetValues<ProbeProtocol>();
    public Array ModeOptions { get; } = Enum.GetValues<ProbeMode>();

    public ObservableCollection<string> HopLines { get; }
    public ObservableCollection<string> ScoreLines { get; }

    private string _probeResult = string.Empty;
    public string ProbeResult
    {
        get => _probeResult;
        set => SetProperty(ref _probeResult, value);
    }

    public string Recommendation
    {
        get => _recommendation;
        set => SetProperty(ref _recommendation, value);
    }

    public double MeanMs => _lastSummary?.MeanMs ?? double.NaN;
    public double Loss => _lastSummary?.LossRatio ?? double.NaN;
    public double Jitter => _lastSummary?.JitterMs ?? double.NaN;
    public double P95 => _lastSummary?.P95Ms ?? double.NaN;

    public ICommand RunProbeCommand { get; }
    public ICommand RunTraceCommand { get; }
    public ICommand CompareCommand { get; }

    private GameServer BuildTarget()
    {
        if (string.IsNullOrWhiteSpace(Host)) throw new ArgumentException("Escribe un dominio o IP a diagnosticar.");
        var target = new GameServer
        {
            Name = Host.Trim(),
            Domain = Uri.CheckHostName(Host.Trim()) != UriHostNameType.Unknown && !System.Net.IPAddress.TryParse(Host.Trim(), out _) ? Host.Trim() : string.Empty,
            Ip = System.Net.IPAddress.TryParse(Host.Trim(), out _) ? Host.Trim() : string.Empty,
            PreferredProtocol = Protocol
        };
        if (int.TryParse(Port, out var port) && port is > 0 and < 65536) target.Ports.Add(port);
        return target;
    }

    private async Task RunProbeAsync()
    {
        IsBusy = true;
        BusyText = "Midiendo…";
        try
        {
            var target = BuildTarget();
            var summary = await _svc.ProbeEngine.ProbeAsync(target, Mode);
            _lastSummary = summary;
            ProbeResult =
                $"Media {summary.MeanMs:0.0} ms · p95 {summary.P95Ms:0.0} · pérdida {summary.LossRatio:P1} · " +
                $"jitter {summary.JitterMs:0.0} ms · estabilidad {summary.StabilityScore:0}/100 · " +
                $"protocolo {summary.ProtocolUsed} ({summary.SuccessCount}/{summary.TotalProbes} OK)";
            OnPropertyChanged(nameof(MeanMs));
            OnPropertyChanged(nameof(Loss));
            OnPropertyChanged(nameof(Jitter));
            OnPropertyChanged(nameof(P95));
            if (!summary.IsReachable)
                ProbeResult += "\nSin respuesta: el endpoint no responde a este protocolo o está filtrado.";
            if (!string.IsNullOrWhiteSpace(summary.Notes))
                ProbeResult += "\n" + summary.Notes;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunTraceAsync()
    {
        IsBusy = true;
        BusyText = "Trazando ruta…";
        try
        {
            var target = BuildTarget();
            HopLines.Clear();
            var result = await _svc.Traceroute.TraceAsync(target.Endpoint);
            foreach (var hop in result.Hops)
            {
                var times = hop.Responded ? string.Join("  ", hop.RttSamplesMs.Select(t => $"{t:0.0} ms")) : "* sin respuesta";
                var flag = hop.SuspectedBottleneck ? "  ← posible cuello de botella" : "";
                HopLines.Add($"{hop.Hop,3}. {hop.Address,-16} {times}{flag}");
            }
            HopLines.Add("→ " + result.Explain());
        }
        catch (Exception ex)
        {
            _svc.Notifications.Error($"Traceroute: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompareAsync()
    {
        IsBusy = true;
        BusyText = "Comparando rutas (directa vs relays)…";
        try
        {
            var target = BuildTarget();
            var profile = new GameProfile { Name = "Diagnóstico", Servers = [target] };
            var relays = _svc.Relays.Relays.Where(r => r.IsEnabled).ToList();
            ScoreLines.Clear();
            if (relays.Count == 0)
            {
                ScoreLines.Add("No hay relays configurados: solo se mide la ruta directa.");
            }
            var rec = await new Core.Routing.RouteAnalyzer(_svc.ProbeEngine,
                new Core.Scoring.ScoreEngine(extremeLossThreshold: _svc.Settings.ExtremeLossThreshold),
                _svc.Sessions).CompareAsync(profile, target, relays, Mode);

            foreach (var (candidate, score) in rec.Candidates.Zip(rec.AllScores))
            {
                var state = score.Excluded
                    ? $"EXCLUIDO — {score.ExclusionReason}"
                    : $"score {score.Value:0.0}/100 · confianza {score.Confidence:P0}";
                ScoreLines.Add($"{candidate.DisplayName,-22} {candidate.LatencyMs,8:0.0} ms  pérdida {candidate.LossRatio,6:P1}  {state}");
            }
            Recommendation = $"{rec.Best?.DisplayName ?? "—"}: {rec.SimpleExplanation}";
        }
        catch (Exception ex)
        {
            _svc.Notifications.Error($"Comparación: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
