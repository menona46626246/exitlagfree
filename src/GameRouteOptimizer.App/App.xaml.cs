using System.Windows;
using System.Windows.Threading;
using GameRouteOptimizer.App.ViewModels;

namespace GameRouteOptimizer.App;

public partial class App : System.Windows.Application
{
    private AppServices _services = null!;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _services = new AppServices(Dispatcher);
            AppServices.Current = _services;

            var vm = new MainViewModel(_services);
            _window = new MainWindow { DataContext = vm };
            MainWindow = _window;
            _window.Show();
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
            _services?.Log.Error("Excepción no controlada: {0}", e.Exception);
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
