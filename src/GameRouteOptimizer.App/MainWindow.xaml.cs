using System.Windows;
using GameRouteOptimizer.App.ViewModels;

namespace GameRouteOptimizer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var services = AppServices.Current;
        DataContext = new MainViewModel(services);
    }
}
