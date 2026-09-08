using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Text;
using GameRouteOptimizer.App.ViewModels;

namespace GameRouteOptimizer.App.Views;

public partial class RelaysView : UserControl
{
    public RelaysView()
    {
        InitializeComponent();
    }

    private void OnSavePrivateKey(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RelaysViewModel vm)
        {
            return;
        }

        var key = PrivateKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            MessageBox.Show("Escribe o pega la clave privada primero.", "Clave privada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        vm.SetPrivateKeyFromUi(key);
        PrivateKeyBox.Clear();
    }

    private void OnGeneratePrivateKey(object sender, RoutedEventArgs e)
    {
        // WireGuard usa Curve25519: una clave privada son 32 bytes aleatorios en base64.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var key = Convert.ToBase64String(bytes);
        PrivateKeyBox.Password = key;
        if (DataContext is RelaysViewModel vm)
        {
            var persisted = vm.SetPrivateKeyFromUi(key);
            PrivateKeyBox.Clear();
            if (!persisted)
            {
                MessageBox.Show(
                    "Se generó una clave privada aleatoria y se guardó cifrada.\n" +
                    "IMPORTANTE: el servidor WireGuard debe tener la clave PÚBLICA correspondiente en su [Peer].\n" +
                    "Cualquier configuración importada antes quedará sin efecto con esta clave nueva.",
                    "Clave generada", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
