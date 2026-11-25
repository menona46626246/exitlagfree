using System.Windows;
using System.Windows.Controls;
using GameRouteOptimizer.App.Services;
using GameRouteOptimizer.App.ViewModels;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.App.Views;

/// <summary>Diálogo de edición de perfil de juego (todos los campos del spec).</summary>
public partial class GameProfileDialog : Window
{
    public GameProfileDialog(GameProfile source)
    {
        InitializeComponent();
        // Clon de trabajo: cancelar no afecta al original.
        EditedProfile = source.Clone();
        Editor = new GameProfileEditor(EditedProfile);
        DataContext = Editor;
    }

    public GameProfile EditedProfile { get; }

    public GameProfileEditor Editor { get; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Editor.Apply();
        if (!string.IsNullOrEmpty(Editor.ServersError))
        {
            MessageBox.Show(Editor.ServersError, "Servidores inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var errors = EditedProfile.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Perfil inválido",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
