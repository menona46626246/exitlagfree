using System.Windows;
using GameRouteOptimizer.App.ViewModels;

namespace GameRouteOptimizer.App.Views;

/// <summary>Ventana principal con navegación lateral y barra de título personalizada.</summary>
public partial class MainWindow : Window
{
    private bool _wizardChecked;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_wizardChecked)
        {
            return;
        }

        _wizardChecked = true;

        if (AppServices.Current is { } svc &&
            !svc.Settings.OnboardingCompleted &&
            svc.Profiles.Profiles.Count == 0)
        {
            OpenFirstRunWizard();
        }
    }

    /// <summary>Abre el asistente guiado de primera ejecución.</summary>
    public void OpenFirstRunWizard()
    {
        if (AppServices.Current is null)
        {
            return;
        }

        var wizard = new FirstRunWizardDialog(AppServices.Current, DataContext as MainViewModel)
        {
            Owner = this
        };
        wizard.ShowDialog();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
