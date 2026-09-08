using System.Windows;
using System.Windows.Threading;

namespace GameRouteOptimizer.App;

public partial class App : System.Windows.Application
{
    private AppServices _services = null!;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var smoke = e.Args.Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));

        try
        {
            _services = new AppServices(Dispatcher);
            _window = new MainWindow();
            MainWindow = _window;

            if (smoke)
            {
                // Smoke-test de CI: construye servicios, VMs y la ventana principal
                // (carga el XAML de cada sección) y sale solo con código 0.
                _services.Log.Info("Smoke-test: la UI se construyó correctamente.");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Shutdown(0);
                };
                timer.Start();
            }
            else
            {
                _window.Show();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo iniciar GameRoute Optimizer:\n\n" + ex,
                "Error de inicio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _services?.Log.Error("Excepción no controlada: " + e.Exception);
        }
        catch
        {
            // Nada más que hacer si el log también falla.
        }

        if (MessageBox.Show(
                "Ocurrió un error inesperado:\n\n" + e.Exception.Message +
                "\n\n¿Continuar la aplicación? (No la cierra)",
                "GameRoute Optimizer", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            e.Handled = true;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Restaurar red (túnel + kill switch) y liberar recursos antes de salir.
            _services?.StopEverythingAsync("la aplicación se cerró")
                .GetAwaiter().GetResult();
        }
        catch
        {
            // El cierre no debe fallar.
        }

        base.OnExit(e);
    }
}
