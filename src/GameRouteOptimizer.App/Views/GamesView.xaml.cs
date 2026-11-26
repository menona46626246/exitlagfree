using System.Windows;
using System.Windows.Controls;
using GameRouteOptimizer.App.Services;
using GameRouteOptimizer.App.ViewModels;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.App.Views;

/// <summary>Vista de juegos: abre el editor al añadir/editar (el diálogo vive en GameProfileDialog.xaml).</summary>
public partial class GamesView : UserControl
{
    private GamesViewModel? _vm;

    public GamesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as GamesViewModel;
            if (_vm is not null)
            {
                _vm.RequestEdit += OpenEditor;
            }
        };
    }

    private void OpenEditor(GameProfile profile)
    {
        if (AppServices.Current is null)
        {
            return;
        }

        var existed = AppServices.Current.Profiles.Profiles.Any(p => p.Id == profile.Id);
        var dialog = new GameProfileDialog(profile) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        var editor = dialog.Editor;
        if (!string.IsNullOrEmpty(editor.ServersError))
        {
            MessageBox.Show(editor.ServersError, "Servidores inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var profileToSave = dialog.EditedProfile;
            if (existed)
            {
                AppServices.Current.Profiles.Update(profileToSave);
            }
            else
            {
                AppServices.Current.Profiles.Add(profileToSave);
            }

            AppServices.Current.Notifications.Success($"Perfil '{profileToSave.Name}' guardado.");
            _vm?.Reload();
        }
        catch (Exception ex)
        {
            AppServices.Current.Notifications.Error($"Guardar perfil: {ex.Message}");
        }
    }
}
