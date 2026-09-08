using System.Windows;
using System.Windows.Threading;

namespace GameRouteOptimizer.App;

/// <summary>
/// Aplicación WPF de GameRoute Optimizer.
/// </summary>
public partial class App : Application
{
    internal const string SmokeTestArgument = "--smoke-test";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "Ocurrió un error inesperado: " + args.Exception.Message,
                "GameRoute Optimizer — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow();
        window.Show();

        // Modo autoverificación (CI / QA): abre la UI, espera unos segundos y sale con código 0.
        if (e.Args.Contains(SmokeTestArgument, StringComparer.OrdinalIgnoreCase))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Shutdown(0);
            };
            timer.Start();
        }
    }
}
