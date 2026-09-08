using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using Microsoft.Win32;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Registros locales: cola en vivo, exportación y limpieza.</summary>
public sealed class LogsViewModel : SectionViewModel
{
    private LogEntry? _selectedEntry;
    private string _filter = string.Empty;

    public LogsViewModel(AppServices app) : base(app)
    {
        App.Log.EntryAdded += (_, entry) => RunOnUi(() => OnEntryAdded(entry));
        foreach (var entry in App.Log.GetRecent(1500))
        {
            Entries.Add(entry);
        }
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public LogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public string FilterText
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string EntryCountText => $"Entradas visibles: {Entries.Count}";

    private void OnEntryAdded(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > 2500)
        {
            Entries.RemoveAt(0);
        }

        OnPropertyChanged(nameof(EntryCountText));
    }

    private void ApplyFilter()
    {
        var needle = FilterText.Trim();
        if (needle.Length == 0)
        {
            return;
        }

        // En v1 el filtro selecciona entradas que coincidan (resaltado por selección).
        var matches = Entries.Where(e =>
                e.Message.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                e.Level.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count > 0)
        {
            SelectedEntry = matches[^1];
        }
    }

    public Mvvm.AsyncRelayCommand ExportCommand => new(_ =>
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Texto (*.log)|*.log|Markdown (*.md)|*.md",
            FileName = $"gro-{DateTime.Now:yyyyMMdd-HHmm}.log",
            Title = "Exportar registros",
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            File.WriteAllText(dialog.FileName, App.Log.ExportAllText());
            MessageBox.Show("Registros exportados.", "Exportar registros",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo exportar: " + ex.Message, "Exportar registros",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand ClearCommand => new(_ =>
    {
        if (MessageBox.Show("¿Limpiar los registros visibles? Los archivos .log en disco se conservan.",
                "Limpiar registros", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        App.Log.Clear();
        Entries.Clear();
        OnPropertyChanged(nameof(EntryCountText));
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand RefreshCommand => new(_ =>
    {
        Entries.Clear();
        foreach (var entry in App.Log.GetRecent(1500))
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(EntryCountText));
        return Task.CompletedTask;
    });

    protected override System.Windows.FrameworkElement BuildView() => new LogsView { DataContext = this };
}
