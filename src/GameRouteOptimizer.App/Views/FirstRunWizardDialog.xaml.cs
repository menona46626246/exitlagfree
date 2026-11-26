using System.Net;
using System.Windows;
using System.Windows.Media;
using GameRouteOptimizer.App.ViewModels;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;

namespace GameRouteOptimizer.App.Views;

/// <summary>Asistente guiado de primera ejecución para configurar modo diagnóstico y primer perfil.</summary>
public partial class FirstRunWizardDialog : Window
{
    private readonly AppServices _svc;
    private readonly MainViewModel? _mainVm;
    private int _step = 1;

    public FirstRunWizardDialog(AppServices svc, MainViewModel? mainVm = null)
    {
        InitializeComponent();
        _svc = svc;
        _mainVm = mainVm;

        RoutingModeBox.ItemsSource = new[]
        {
            new RoutingOption(RoutingMode.Direct, "Directo (solo medición / diagnóstico — ideal sin VPS)"),
            new RoutingOption(RoutingMode.TunnelGameOnly, "Túnel solo juego (usará relay WireGuard cuando agregues uno)")
        };
        RoutingModeBox.DisplayMemberPath = nameof(RoutingOption.Label);
        RoutingModeBox.SelectedIndex = svc.Relays.Relays.Count == 0 ? 0 : 1;

        UpdateStepUi();
    }

    private sealed record RoutingOption(RoutingMode Mode, string Label);

    private void FillQuickTest_Click(object sender, RoutedEventArgs e)
    {
        ProfileNameBox.Text = "Test de conectividad (Cloudflare)";
        ExecutableBox.Text = "diagnostico.exe";
        HostBox.Text = "1.1.1.1";
        PortBox.Text = "443";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 3 && CreateProfileCheck.IsChecked != true)
        {
            _step = 1;
        }
        else if (_step > 1)
        {
            _step--;
        }

        UpdateStepUi();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            _step = CreateProfileCheck.IsChecked == true ? 2 : 3;
            UpdateStepUi();
            return;
        }

        if (_step == 2)
        {
            if (string.IsNullOrWhiteSpace(HostBox.Text))
            {
                MessageBox.Show("Escribe una IP o dominio objetivo (o pulsa 'Rellenar prueba rápida').",
                    "Servidor requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(ProfileNameBox.Text))
            {
                MessageBox.Show("Escribe un nombre para el perfil.",
                    "Nombre requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _step = 3;
            UpdateStepUi();
        }
    }

    private async void RunQuickProbe_Click(object sender, RoutedEventArgs e)
    {
        var target = BuildServerFromInputs();
        if (target is null)
        {
            QuickProbeResultText.Text = "No se definió ningún servidor; puedes escribir uno en la pestaña Diagnóstico.";
            return;
        }

        RunQuickProbeButton.IsEnabled = false;
        QuickProbeResultText.Text = $"Midiendo {target.Endpoint} (modo rápido)...";
        try
        {
            var summary = await _svc.ProbeEngine.ProbeAsync(target, ProbeMode.Quick);
            if (summary.IsReachable)
            {
                QuickProbeResultText.Text =
                    $"Conexión medida hacia {target.Endpoint} ({summary.ProtocolUsed}):\n" +
                    $"• Latencia media: {summary.MeanMs:0.0} ms (p95: {summary.P95Ms:0.0} ms)\n" +
                    $"• Jitter: {summary.JitterMs:0.0} ms · Pérdida: {summary.LossRatio:P1}\n" +
                    $"• Estabilidad: {summary.StabilityScore:0}/100 ({summary.SuccessCount}/{summary.TotalProbes} muestras OK)";
            }
            else
            {
                QuickProbeResultText.Text =
                    $"Sin respuesta desde {target.Endpoint} ({summary.ProtocolUsed}). " +
                    "El servidor puede filtrar ICMP/TCP en ese puerto o estar sin conexión.";
            }
        }
        catch (Exception ex)
        {
            QuickProbeResultText.Text = $"Error al medir: {ex.Message}";
        }
        finally
        {
            RunQuickProbeButton.IsEnabled = true;
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _svc.Settings.OnboardingCompleted = true;
        _svc.SaveSettings();
        DialogResult = false;
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (CreateProfileCheck.IsChecked == true)
        {
            var server = BuildServerFromInputs();
            if (server is not null)
            {
                var exe = string.IsNullOrWhiteSpace(ExecutableBox.Text) ? "juego.exe" : ExecutableBox.Text.Trim();
                var mode = (RoutingModeBox.SelectedItem as RoutingOption)?.Mode ?? RoutingMode.Direct;
                var profile = new GameProfile
                {
                    Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? "Mi Juego" : ProfileNameBox.Text.Trim(),
                    Executables = [exe],
                    Servers = [server],
                    RoutingMode = mode,
                    Notes = "Creado desde el asistente inicial."
                };

                try
                {
                    if (!_svc.Profiles.Profiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        _svc.Profiles.Add(profile);
                        _mainVm?.Games.Reload();
                        _svc.Notifications.Success($"Perfil '{profile.Name}' creado desde el asistente.");
                    }

                    if (_mainVm is not null)
                    {
                        _mainVm.Diagnostics.Host = server.Endpoint;
                        if (server.Ports.Count > 0)
                        {
                            _mainVm.Diagnostics.Port = server.Ports[0].ToString();
                        }

                        _mainVm.Dashboard.ActiveGame = profile.Name;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"No se pudo guardar el perfil: {ex.Message}",
                        "Perfil inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }

        _svc.Settings.OnboardingCompleted = DontShowAgainCheck.IsChecked == true;
        _svc.SaveSettings();
        DialogResult = true;
    }

    private GameServer? BuildServerFromInputs()
    {
        var rawHost = HostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawHost))
        {
            return CreateProfileCheck.IsChecked == true ? null : new GameServer { Name = "Cloudflare DNS", Ip = "1.1.1.1", Ports = [443] };
        }

        var isIp = IPAddress.TryParse(rawHost, out _);
        var server = new GameServer
        {
            Name = "Principal",
            Ip = isIp ? rawHost : string.Empty,
            Domain = isIp ? string.Empty : rawHost,
            PreferredProtocol = ProbeProtocol.Auto
        };
        if (int.TryParse(PortBox.Text.Trim(), out var port) && port is > 0 and < 65536)
        {
            server.Ports.Add(port);
        }

        return server;
    }

    private void UpdateStepUi()
    {
        Step1Panel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = _step > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = _step < 3 ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

        StepCounterText.Text = $"Paso {_step} de 3";
        StepTitleText.Text = _step switch
        {
            1 => "Bienvenido a GameRoute Optimizer",
            2 => "Configura tu primer perfil u objetivo",
            _ => "Prueba rápida en vivo"
        };

        var activeBg = (Brush)FindResource("SurfaceAltBrush");
        var idleBg = (Brush)FindResource("SurfaceBrush");
        var activeBorder = (Brush)FindResource("AccentBrush");
        var idleBorder = (Brush)FindResource("BorderBrush");

        Step1Pill.Background = _step == 1 ? activeBg : idleBg;
        Step1Pill.BorderBrush = _step == 1 ? activeBorder : idleBorder;
        Step2Pill.Background = _step == 2 ? activeBg : idleBg;
        Step2Pill.BorderBrush = _step == 2 ? activeBorder : idleBorder;
        Step3Pill.Background = _step == 3 ? activeBg : idleBg;
        Step3Pill.BorderBrush = _step == 3 ? activeBorder : idleBorder;

        if (_step == 3)
        {
            var host = string.IsNullOrWhiteSpace(HostBox.Text) ? "1.1.1.1" : HostBox.Text.Trim();
            WizardSummaryText.Text = CreateProfileCheck.IsChecked == true
                ? $"Se guardará el perfil '{ProfileNameBox.Text.Trim()}' apuntando a '{host}'. Puedes probar la latencia real ahora mismo antes de entrar:"
                : $"No se creará ningún perfil nuevo. Puedes ejecutar una prueba rápida hacia '{host}' o finalizar directamente:";
        }
    }
}
