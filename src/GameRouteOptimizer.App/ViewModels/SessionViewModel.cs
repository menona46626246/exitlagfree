using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using Microsoft.Win32;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Sesión: eventos en vivo, historial y exportación de informes.</summary>
public sealed class SessionViewModel : SectionViewModel
{
    private string _sessionInfo = "Sin sesión activa.";
    private string _directText = string.Empty;
    private string _optimizedText = string.Empty;
    private string _improvementText = string.Empty;

    public SessionViewModel(AppServices app) : base(app)
    {
        App.Orchestrator.SessionEventAdded += (_, ev) => RunOnUi(() => OnSessionEvent(ev));
        App.Sessions.SessionUpdated += (_, session) => RunOnUi(() => OnSessionUpdated(session));
        RefreshHistory();
    }

    public ObservableCollection<SessionEvent> SessionEvents { get; } = new();

    public ObservableCollection<SessionRecord> History { get; } = new();

    public SessionRecord? SelectedSession { get; set; }

    public string SessionInfo
    {
        get => _sessionInfo;
        private set => SetProperty(ref _sessionInfo, value);
    }

    public string DirectText
    {
        get => _directText;
        private set => SetProperty(ref _directText, value);
    }

    public string OptimizedText
    {
        get => _optimizedText;
        private set => SetProperty(ref _optimizedText, value);
    }

    public string ImprovementText
    {
        get => _improvementText;
        private set => SetProperty(ref _improvementText, value);
    }

    public void OnMetrics(Core.Services.MetricsSnapshot snapshot)
    {
        var session = App.Orchestrator.CurrentSession;
        if (session is null)
        {
            return;
        }

        SessionInfo = $"Sesión: {session.GameProfileName} → {session.TargetDisplay} " +
                      $"({session.StartedUtc.ToLocalTime():HH:mm:ss})" +
                      (session.RelayName is null ? " · ruta directa" : $" · relay {session.RelayName}");
        DirectText = snapshot.Direct is null
            ? string.Empty
            : $"Directa: {snapshot.Direct}";
        OptimizedText = snapshot.Optimized is null
            ? string.Empty
            : $"Túnel:   {snapshot.Optimized}";
        ImprovementText = session.EstimatedImprovementMs is { } improvement
            ? improvement > 1
                ? $"Mejora estimada: {improvement:F1} ms menos de latencia media."
                : improvement < -1
                    ? $"El túnel es {Math.Abs(improvement):F1} ms más lento que la directa."
                    : "Sin diferencia significativa."
            : string.Empty;
    }

    private void OnSessionEvent(SessionEvent ev)
    {
        SessionEvents.Add(ev);
        while (SessionEvents.Count > 500)
        {
            SessionEvents.RemoveAt(0);
        }
    }

    private void OnSessionUpdated(SessionRecord session)
    {
        if (session.Id != App.Orchestrator.CurrentSession?.Id)
        {
            return;
        }

        RefreshHistory();
    }

    public void RefreshHistory()
    {
        var keep = History.FirstOrDefault();
        History.Clear();
        foreach (var session in App.Sessions.RecentSessions().OrderByDescending(s => s.StartedUtc))
        {
            History.Add(session);
        }

        SelectedSession = keep ?? History.FirstOrDefault();
    }

    public Mvvm.AsyncRelayCommand ExportSessionCommand => new(_ =>
    {
        var session = App.Orchestrator.CurrentSession ?? SelectedSession;
        if (session is null)
        {
            MessageBox.Show("No hay sesión que exportar.", "Exportar informe",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md",
            FileName = $"informe-sesion-{session.StartedUtc:yyyyMMdd-HHmm}.md",
            Title = "Exportar informe de sesión",
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            var markdown = App.Sessions.ExportSessionMarkdown(session);
            File.WriteAllText(dialog.FileName, markdown);
            MessageBox.Show("Informe exportado en Markdown.", "Exportar informe",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo exportar: " + ex.Message, "Exportar informe",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand LoadSelectedSessionCommand => new(_ =>
    {
        if (SelectedSession is not { } session)
        {
            return Task.CompletedTask;
        }

        SessionEvents.Clear();
        foreach (var ev in session.Events)
        {
            SessionEvents.Add(ev);
        }

        SessionInfo = $"Sesión histórica: {session.GameProfileName} → {session.TargetDisplay} " +
                      $"({session.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss})" +
                      (session.RelayName is null ? string.Empty : $" · relay {session.RelayName}") +
                      (session.EndedReason is null ? string.Empty : $" · fin: {session.EndedReason}");
        DirectText = session.DirectMetrics is null ? string.Empty : $"Directa: {session.DirectMetrics}";
        OptimizedText = session.OptimizedMetrics is null ? string.Empty : $"Túnel: {session.OptimizedMetrics}";
        ImprovementText = session.EstimatedImprovementMs is { } imp
            ? imp > 1
                ? $"Mejora estimada: {imp:F1} ms."
                : imp < -1
                    ? $"El túnel fue {Math.Abs(imp):F1} ms peor."
                    : "Sin diferencia significativa."
            : string.Empty;
        return Task.CompletedTask;
    });

    protected override System.Windows.FrameworkElement BuildView() => new SessionView { DataContext = this };
}
