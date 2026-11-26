using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using GameRouteOptimizer.App.Infrastructure;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>VM de Sesión: métricas en vivo, eventos, historial y exportación de informes.</summary>
public sealed class SessionViewModel : ViewModelBase
{
    private readonly AppServices _svc;
    private SessionRecord? _selectedHistory;

    public SessionViewModel(AppServices svc)
    {
        _svc = svc;
        Events = new ObservableCollection<SessionEvent>();
        History = new ObservableCollection<SessionRecord>(svc.Sessions.GetRecentSessions());

        svc.Sessions.EventRecorded += evt => DispatcherSafe(() =>
        {
            Events.Insert(0, evt);
            while (Events.Count > 500)
            {
                Events.RemoveAt(Events.Count - 1);
            }
        });

        ExportMarkdownCommand = new RelayCommand(ExportMarkdown, () => _svc.Sessions.Current is not null);
        ExportJsonCommand = new RelayCommand(ExportJson, () => _svc.Sessions.Current is not null);
        ExportHistoryCommand = new RelayCommand(ExportHistoryReport, () => SelectedHistory is not null);
        RefreshHistoryCommand = new RelayCommand(() =>
        {
            History.Clear();
            foreach (var s in _svc.Sessions.GetRecentSessions())
            {
                History.Add(s);
            }
        });
    }

    public ObservableCollection<SessionEvent> Events { get; }
    public ObservableCollection<SessionRecord> History { get; }

    public SessionRecord? SelectedHistory
    {
        get => _selectedHistory;
        set => SetProperty(ref _selectedHistory, value);
    }

    public string CurrentSessionText => _svc.Sessions.Current is { } s
        ? $"{s.ProfileName} → {s.GameServerTarget} (modo {s.Mode})"
        : "Sin sesión activa";

    public string LiveSummaryText
    {
        get
        {
            lock (_svc.Orchestrator.Metrics.LatencySeries)
            {
                var series = _svc.Orchestrator.Metrics.LatencySeries;
                return series.Count == 0
                    ? "Sin muestras en esta sesión."
                    : $"Últimos {series.Count} samples: media {series.Average():0.0} ms, máx {series.Max():0.0} ms.";
            }
        }
    }

    public ICommand ExportMarkdownCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportHistoryCommand { get; }
    public ICommand RefreshHistoryCommand { get; }

    private void ExportMarkdown()
    {
        var session = _svc.Sessions.Current;
        if (session is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar informe de sesión (Markdown)",
            FileName = $"informe-{session.ProfileName}.md",
            Filter = "Markdown (*.md)|*.md"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        var path = _svc.Sessions.ExportReportMarkdown(session, Path.GetDirectoryName(dialog.FileName)!);
        var finalPath = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileName(path));
        if (finalPath != dialog.FileName)
        {
            File.Move(finalPath, dialog.FileName, overwrite: true);
        }

        _svc.Notifications.Success($"Informe exportado: {dialog.FileName}");
    }

    private void ExportJson()
    {
        var session = _svc.Sessions.Current;
        if (session is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar informe de sesión (JSON)",
            FileName = $"informe-{session.ProfileName}.json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        var path = _svc.Sessions.ExportReportJson(session, Path.GetDirectoryName(dialog.FileName)!);
        var finalPath = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileName(path));
        if (finalPath != dialog.FileName)
        {
            File.Move(finalPath, dialog.FileName, overwrite: true);
        }

        _svc.Notifications.Success($"Informe exportado: {dialog.FileName}");
    }

    private void ExportHistoryReport()
    {
        if (SelectedHistory is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar informe histórico (Markdown)",
            FileName = $"informe-hist-{SelectedHistory.Id[..8]}.md",
            Filter = "Markdown (*.md)|*.md"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        var loaded = new SessionRecord { Id = SelectedHistory.Id };
        // Rehidratar eventos del histórico para el informe completo.
        loaded = Rehydrate(SelectedHistory);
        var path = _svc.Sessions.ExportReportMarkdown(loaded, Path.GetDirectoryName(dialog.FileName)!);
        var finalPath = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileName(path));
        if (finalPath != dialog.FileName)
        {
            File.Move(finalPath, dialog.FileName, overwrite: true);
        }

        _svc.Notifications.Success($"Informe exportado: {dialog.FileName}");
    }

    private SessionRecord Rehydrate(SessionRecord partial)
    {
        partial.Events.Clear();
        partial.Events.AddRange(_svc.Sessions.GetSessionEvents(partial.Id));
        return partial;
    }

    private void DispatcherSafe(Action action)
    {
        Application.Current?.Dispatcher.Invoke(action);
    }
}
