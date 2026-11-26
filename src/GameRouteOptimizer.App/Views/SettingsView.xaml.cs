using System.Windows;
using System.Windows.Controls;

namespace GameRouteOptimizer.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OpenWizard_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenFirstRunWizard();
    }
}
