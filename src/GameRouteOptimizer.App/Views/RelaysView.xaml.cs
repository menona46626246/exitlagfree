using System.Windows;
using System.Windows.Controls;
using GameRouteOptimizer.App.Services;
using GameRouteOptimizer.App.ViewModels;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.App.Views;

/// <summary>Vista de relays: CRUD con diálogo, medición, import/export.</summary>
public partial class RelaysView : UserControl
{
    private RelaysViewModel? _vm;

    public RelaysView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as RelaysViewModel;
            if (_vm is not null) _vm.RequestEdit += OpenEditor;
        };
    }

    private void OpenEditor(RelayNode relay)
    {
        if (AppServices.Current is null) return;
        var existed = AppServices.Current.Relays.Relays.Any(r => r.Id == relay.Id);
        var dialog = new RelayDialog(relay) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() is not true) return;

        try
        {
            var toSave = dialog.EditedRelay;
            if (existed)
                AppServices.Current.Relays.Update(toSave);
            else
                AppServices.Current.Relays.Add(toSave);
            AppServices.Current.Notifications.Success($"Relay '{toSave.Name}' guardado.");
            _vm?.Reload();
        }
        catch (Exception ex)
        {
            AppServices.Current.Notifications.Error($"Guardar relay: {ex.Message}");
        }
    }
}

/// <summary>Diálogo de edición de relay (todos los campos del spec).</summary>
public partial class RelayDialog : Window
{
    public RelayDialog(RelayNode source)
    {
        InitializeComponent();
        EditedRelay = source.Clone();
        Editor = new RelayEditor(EditedRelay);
        DataContext = Editor;
    }

    public RelayNode EditedRelay { get; }

    public RelayEditor Editor { get; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Editor.Apply();
        var errors = EditedRelay.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Relay inválido",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
