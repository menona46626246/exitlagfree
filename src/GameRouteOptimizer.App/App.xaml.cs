using System.Windows;
using GameRouteOptimizer.App.Services;
using GameRouteOptimizer.App.ViewModels;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App;

/// <summary>
/// Punto de entrada WPF. Composición manual de servicios, manejador global de
/// excepciones y ventana principal. Sin telemetría: las excepciones solo se registran
/// en el log local.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            var text = args.Exception.ToString();
            try
            {
                var crashPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameRouteOptimizer", "crash.txt");
                File.WriteAllText(crashPath, text);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"), text);
            }
            catch { }
            Log.Error("Excepción de UI no manejada", args.Exception);
            MessageBox.Show($"Ocurrió un error (registrado en el log local):\n\n{text}",
                "GameRoute Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var services = new AppServices();
        services.Events.Subscribe<Core.Events.AppEvent>(evt => Log.Information(evt.Message));
        services.Notifications.Info("GameRoute Optimizer iniciado. Modo diagnóstico disponible sin privilegios.");

        var main = new MainWindow
        {
            DataContext = new MainViewModel(services)
        };
        main.Show();
    }
}
