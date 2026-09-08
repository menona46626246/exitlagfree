using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Configuración: probing, auto-switch, túnel, kill switch, logs y privacidad.</summary>
public sealed class SettingsViewModel : SectionViewModel
{
    private AppSettings _settings;

    public SettingsViewModel(AppServices app) : base(app)
    {
        _settings = app.Settings;
    }

    public AppSettings Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    public string DataDirectoryText => App.DataDirectory;

    public string[] LogLevels { get; } =
    {
        "Verbose", "Debug", "Information", "Warning", "Error",
    };

    public int LogLevelIndex
    {
        get => (int)Settings.Logging.MinimumLevel;
        set => Settings.Logging.MinimumLevel = (EventLevel)Math.Clamp(value, 0, 4);
    }

    public string[] ThemeOptions { get; } = { "Oscuro", "Claro" };

    /// <summary>0 = oscuro, 1 = claro (coincide con <see cref="AppSettings.Theme"/>).</summary>
    public int ThemeIndex
    {
        get => Settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set => Settings.Theme = value == 1 ? "Light" : "Dark";
    }

    public void Reload()
    {
        Settings = App.Settings;
        OnPropertyChanged(nameof(ThemeIndex));
        OnPropertyChanged(nameof(LogLevelIndex));
    }

    public Mvvm.AsyncRelayCommand SaveCommand => new(_ =>
    {
        App.SaveSettings(Settings);
        // El tema se aplica al instante (los pinceles usan DynamicResource).
        ThemeManager.Apply(Settings.Theme);
        MessageBox.Show("Configuración guardada y tema aplicado.",
            "GameRoute Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand DiscardCommand => new(_ =>
    {
        Reload();
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand OpenDataFolderCommand => new(_ =>
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", App.DataDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo abrir la carpeta: " + ex.Message,
                "GameRoute Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    protected override System.Windows.FrameworkElement BuildView() => new SettingsView { DataContext = this };
}
