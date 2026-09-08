using System.Collections.ObjectModel;
using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Scoring;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Pantalla de diagnóstico: prueba rápida/profunda, traceroute y comparación directa vs relays.</summary>
public sealed class DiagnosticsViewModel : SectionViewModel
{
    private GameProfile? _selectedProfile;
    private string _host = string.Empty;
    private string _port = "443";
    private bool _deep;
    private string _output = string.Empty;
    private bool _busy;
    private ObservableCollection<TraceHopRow> _traceHops = new();
    private string _recommendation = string.Empty;

    public DiagnosticsViewModel(AppServices app) : base(app)
    {
        App.Games.ProfilesChanged += (_, _) => RunOnUi(ReloadProfiles);
        App.Orchestrator.DiagnosticsCompleted += (_, report) =>
            RunOnUi(() => OnDiagnosticsCompleted(report));
        ReloadProfiles();
    }

    public ObservableCollection<GameProfile> Profiles { get; } = new();

    public GameProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
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

    public bool Deep
    {
        get => _deep;
        set => SetProperty(ref _deep, value);
    }

    public bool IsBusy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    public string Output
    {
        get => _output;
        private set => SetProperty(ref _output, value);
    }

    public string Recommendation
    {
        get => _recommendation;
        private set => SetProperty(ref _recommendation, value);
    }

    public ObservableCollection<TraceHopRow> TraceHops
    {
        get => _traceHops;
        private set => SetProperty(ref _traceHops, value);
    }

    private void ReloadProfiles()
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in App.Games.GetAll())
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();
    }

    private string ResolveHost()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return Host.Trim();
        }

        if (SelectedProfile is { } profile)
        {
            // Primer objetivo utilizable (con dominio o IP), nunca un placeholder vacío.
            foreach (var target in profile.Targets)
            {
                var host = !string.IsNullOrWhiteSpace(target.Domain)
                    ? target.Domain!.Trim()
                    : target.IpAddress?.Trim();
                if (!string.IsNullOrWhiteSpace(host))
                {
                    return host;
                }
            }
        }

        return string.Empty;
    }

    public Mvvm.AsyncRelayCommand RunProfileDiagnosticsCommand => new(async _ =>
    {
        if (SelectedProfile is not { } profile)
        {
            MessageBox.Show("Selecciona un juego de la lista (o usa la prueba libre).",
                "Diagnóstico", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startProblem = OptimizationOrchestrator.FindStartProblem(profile);
        if (startProblem.Length > 0)
        {
            MessageBox.Show(startProblem,
                "No se pudo iniciar el diagnóstico", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        Output = "Ejecutando diagnóstico…";
        TraceHops = new ObservableCollection<TraceHopRow>();
        Recommendation = string.Empty;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await App.Orchestrator.RunDiagnosticsAsync(profile, Deep, cts.Token);
            Output += "\n\n(El resultado detallado se muestra abajo y se guarda en la sesión actual si está activa).";
        }
        catch (OperationCanceledException)
        {
            Output = "Diagnóstico cancelado.";
        }
        catch (Exception ex)
        {
            Output = "Error en el diagnóstico: " + ex.Message;
            MessageBox.Show(ex.Message, "Diagnóstico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    });

    public Mvvm.AsyncRelayCommand RunFreeProbeCommand => new(async _ =>
    {
        var host = ResolveHost();
        if (host.Length == 0)
        {
            MessageBox.Show("Escribe un host (dominio o IP) para probar.",
                "Diagnóstico", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        try
        {
            var settings = App.Settings.Probing;
            var engine = new ProbeEngine(new SystemProbeTransport(), settings);
            var kind = ProbeKind.Icmp;
            if (!settings.UseIcmp)
            {
                kind = ProbeKind.TcpConnect;
            }

            var spec = new ProbeTargetSpec
            {
                Label = host,
                Host = host,
                Kind = kind,
                Port = ParsePort(),
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = await engine.ProbeAsync(spec, Deep, cts.Token);
            Output = FormatSummary(result.Summary);
        }
        catch (Exception ex)
        {
            Output = "Error: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    });

    public Mvvm.AsyncRelayCommand RunTracerouteCommand => new(async _ =>
    {
        var host = ResolveHost();
        if (host.Length == 0)
        {
            MessageBox.Show("Escribe un host (dominio o IP) para trazar la ruta.",
                "Traceroute", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var result = await App.Traceroute.TraceAsync(host, cts.Token);
            var rows = new ObservableCollection<TraceHopRow>();
            foreach (var hop in result.Hops)
            {
                rows.Add(new TraceHopRow(
                    hop.Ttl,
                    hop.Address ?? "*",
                    hop.RttMs1,
                    hop.RttMs2,
                    hop.Timeouts));
            }

            TraceHops = rows;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Traceroute hacia {result.Target}: {result.Note}");
            foreach (var obs in result.Observations)
            {
                sb.AppendLine("⚠ " + obs);
            }

            Output = sb.ToString();
        }
        catch (Exception ex)
        {
            Output = "Error en traceroute: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    });

    private int ParsePort()
    {
        int.TryParse(Port, out var port);
        return port is > 0 and <= 65535 ? port : 443;
    }

    private void OnDiagnosticsCompleted(DiagnosticsReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== DIAGNÓSTICO COMPLETADO ===");
        sb.AppendLine($"Juego: {report.GameName} → {report.TargetDisplay}");
        if (report.Direct is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Ruta directa:");
            sb.AppendLine(FormatSummary(report.Direct));
        }

        foreach (var m in report.RelayMeasurements)
        {
            sb.AppendLine();
            sb.AppendLine($"Relay {m.RelayName}:");
            sb.AppendLine(m.Endpoint is null ? "  (sin datos)" : FormatSummary(m.Endpoint));
        }

        if (report.Traceroute is { } trace)
        {
            sb.AppendLine();
            sb.AppendLine($"Traceroute: {trace.Note}");
            foreach (var obs in trace.Observations)
            {
                sb.AppendLine("⚠ " + obs);
            }

            var rows = new ObservableCollection<TraceHopRow>();
            foreach (var hop in trace.Hops)
            {
                rows.Add(new TraceHopRow(hop.Ttl, hop.Address ?? "*", hop.RttMs1, hop.RttMs2, hop.Timeouts));
            }

            TraceHops = rows;
        }

        if (report.Recommendation is { } rec)
        {
            Recommendation = FormatRecommendation(rec);
            sb.AppendLine();
            sb.AppendLine("Recomendación: " + rec.ExplanationEs);
        }

        Output = sb.ToString();
    }

    private string FormatSummary(ProbeSummary s)
    {
        static string Ms(double? v) => v.HasValue ? $"{v:F1} ms" : "—";
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"  Destino: {s.TargetLabel}  ({s.Kind})");
        lines.AppendLine($"  Intentos: {s.Attempts} — éxito {s.Successes}, fallo {s.Failures}");
        lines.AppendLine($"  Latencia: media {Ms(s.AvgMs)} | p95 {Ms(s.P95Ms)} | mín {Ms(s.MinMs)} | máx {Ms(s.MaxMs)}");
        lines.AppendLine($"  Jitter: {Ms(s.JitterMs)} | desviación {Ms(s.StdDevMs)}");
        lines.AppendLine($"  Pérdida: {(s.LossPercent.HasValue ? $"{s.LossPercent:F1} %" : "—")}");
        if (!s.IcmpReliable)
        {
            lines.AppendLine("  ⚠ ICMP no fiable: se usó TCP connect (muchos servidores de juego bloquean ICMP).");
        }

        if (!string.IsNullOrWhiteSpace(s.UnavailableReason))
        {
            lines.AppendLine("  ✗ No utilizable: " + s.UnavailableReason);
        }

        if (!string.IsNullOrWhiteSpace(s.Note))
        {
            lines.AppendLine("  Nota: " + s.Note);
        }

        return lines.ToString();
    }

    private string FormatRecommendation(ScoringOutput rec)
    {
        if (rec.WinnerId is null)
        {
            return rec.ExplanationEs;
        }

        var winner = rec.Ranked.FirstOrDefault(c => c.CandidateId == rec.WinnerId);
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"Ganador: {winner?.DisplayName} — {rec.ExplanationEs}");
        lines.AppendLine($"Confianza: {rec.Confidence * 100:F0} %");
        lines.AppendLine();
        lines.AppendLine("Clasificación (menor puntuación = mejor):");
        foreach (var candidate in rec.Ranked)
        {
            var score = candidate.Excluded
                ? $"excluido ({candidate.ExcludeReason})"
                : $"{candidate.ScoreMs:F0} ms equiv.";
            lines.AppendLine($"  • {candidate.DisplayName}: {score}" +
                             (candidate.Strengths.Count > 0
                                 ? $" — {string.Join(", ", candidate.Strengths)}"
                                 : string.Empty));
        }

        return lines.ToString();
    }

    protected override System.Windows.FrameworkElement BuildView() => new DiagnosticsView { DataContext = this };
}

/// <summary>Fila de traceroute para la UI.</summary>
public sealed class TraceHopRow
{
    public TraceHopRow(int ttl, string address, double? rtt1, double? rtt2, int timeouts)
    {
        Ttl = ttl;
        Address = address;
        Rtt1 = rtt1.HasValue ? $"{rtt1:F0} ms" : "-";
        Rtt2 = rtt2.HasValue ? $"{rtt2:F0} ms" : "-";
        Timeouts = timeouts;
    }

    public int Ttl { get; }
    public string Address { get; }
    public string Rtt1 { get; }
    public string Rtt2 { get; }
    public int Timeouts { get; }
}
