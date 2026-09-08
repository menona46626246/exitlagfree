using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Services;
using Microsoft.Win32;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Gestión de relays WireGuard: lista, editor, importación .conf, medición y exportación.</summary>
public sealed class RelaysViewModel : SectionViewModel
{
    private RelayNode? _selected;
    private string _privateKeyStatus = string.Empty;
    private string _healthText = string.Empty;
    private bool _measuring;

    public RelaysViewModel(AppServices app) : base(app)
    {
        App.Relays.RelaysChanged += (_, _) => RunOnUi(Reload);
        Reload();
    }

    public ObservableCollection<RelayNode> Relays { get; } = new();

    public RelayNode? SelectedRelay
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(IsEditing));
                RefreshKeyAndHealth();
            }
        }
    }

    public bool IsEditing => SelectedRelay is not null;

    public string PrivateKeyStatus
    {
        get => _privateKeyStatus;
        private set => SetProperty(ref _privateKeyStatus, value);
    }

    public string HealthText
    {
        get => _healthText;
        private set => SetProperty(ref _healthText, value);
    }

    public bool IsMeasuring
    {
        get => _measuring;
        private set => SetProperty(ref _measuring, value);
    }

    private void Reload()
    {
        var selectedId = SelectedRelay?.Id;
        Relays.Clear();
        foreach (var relay in App.Relays.GetAll())
        {
            Relays.Add(relay);
        }

        SelectedRelay = Relays.FirstOrDefault(r => r.Id == selectedId) ?? Relays.FirstOrDefault();
    }

    private void RefreshKeyAndHealth()
    {
        if (SelectedRelay is not { } relay)
        {
            PrivateKeyStatus = string.Empty;
            HealthText = string.Empty;
            return;
        }

        var usable = App.Relays.HasUsablePrivateKey(relay);
        PrivateKeyStatus = usable
            ? "Clave privada disponible (cifrada con DPAPI)."
            : "Sin clave privada: puedes medir este relay, pero no conectar el túnel. Importa un archivo .conf o pégala abajo.";

        HealthText = relay.Health is { } health
            ? $"Última medición {health.MeasuredUtc.ToLocalTime():HH:mm:ss}: " +
              (health.AvgLatencyMs.HasValue ? $"{health.AvgLatencyMs:F1} ms" : "sin datos") +
              (health.LossPercent.HasValue ? $", pérdida {health.LossPercent:F1} %" : string.Empty) +
              (health.JitterMs.HasValue ? $", jitter {health.JitterMs:F1} ms" : string.Empty)
            : "Sin mediciones todavía. Usa «Probar relay».";
    }

    // ----- comandos -----

    public Mvvm.AsyncRelayCommand NewRelayCommand => new(_ =>
    {
        var relay = new RelayNode
        {
            Name = "Nuevo relay",
            EndpointPort = 51820,
            AllowedIps = "0.0.0.0/0, ::/0",
        };
        if (!App.Relays.Save(relay, out var error))
        {
            MessageBox.Show(error, "No se pudo crear", MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        Reload();
        SelectedRelay = Relays.FirstOrDefault(r => r.Id == relay.Id);
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand SaveRelayCommand => new(_ =>
    {
        if (SelectedRelay is not { } relay)
        {
            return Task.CompletedTask;
        }

        if (!App.Relays.Save(relay, out var error))
        {
            MessageBox.Show(error, "No se pudo guardar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        SelectedRelay = Relays.FirstOrDefault(r => r.Id == relay.Id) ?? SelectedRelay;
        RefreshKeyAndHealth();
        MessageBox.Show("Relay guardado.", "GameRoute Optimizer", MessageBoxButton.OK,
            MessageBoxImage.Information);
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand DeleteRelayCommand => new(_ =>
    {
        if (SelectedRelay is not { } relay)
        {
            return Task.CompletedTask;
        }

        if (MessageBox.Show($"¿Eliminar el relay «{relay.Name}»?", "Eliminar relay",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        App.Relays.Delete(relay.Id);
        Reload();
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand MeasureCommand => new(async parameter =>
    {
        // Prueba rápida: desde la fila de la lista (parámetro) o desde el editor.
        var relay = parameter as RelayNode ?? SelectedRelay;
        if (relay is null || !relay.HasEndpoint)
        {
            MessageBox.Show("Completa primero el endpoint del relay (host y puerto).",
                "Probar relay", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsMeasuring = true;
        try
        {
            var settings = App.Settings.Probing;
            var engine = new ProbeEngine(App.Probes.Transport, settings);
            var result = await engine.ProbeAsync(new ProbeTargetSpec
            {
                Label = $"relay {relay.Name}",
                Host = relay.EndpointHost,
                Kind = ProbeKind.Icmp,
            }, deep: false, CancellationToken.None);

            var summary = result.Summary;
            relay.Health = new RelayHealth
            {
                MeasuredUtc = DateTimeOffset.UtcNow,
                AvgLatencyMs = summary.AvgMs,
                LossPercent = summary.LossPercent,
                JitterMs = summary.JitterMs,
                Note = summary.Note,
            };
            App.Relays.Save(relay, out var ignoredSaveError);
            SelectedRelay = Relays.FirstOrDefault(r => r.Id == relay.Id) ?? SelectedRelay;
            RefreshKeyAndHealth();
            MessageBox.Show(summary.Usable
                    ? $"Relay OK: {summary.AvgMs:F1} ms de media, pérdida {summary.LossPercent:F1} %, jitter {summary.JitterMs:F1} ms."
                    : "El relay no respondió: " + (summary.UnavailableReason ?? "sin datos") + ". " +
                      "Comprueba host/puerto, claves y que el servidor WireGuard acepte ICMP.",
                "Probar relay",
                MessageBoxButton.OK,
                summary.Usable ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error midiendo el relay: " + ex.Message, "Probar relay",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsMeasuring = false;
        }
    });

    public Mvvm.AsyncRelayCommand ImportConfCommand => new(_ =>
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Configuración WireGuard (*.conf)|*.conf|Todos (*.*)|*.*",
            Title = "Importar configuración WireGuard del relay",
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            var imported = App.Relays.ImportFromConfigText(text, Path.GetFileNameWithoutExtension(dialog.FileName),
                out var warnings);
            if (imported is null)
            {
                MessageBox.Show("No se pudo importar la configuración:\n" +
                                string.Join("\n", warnings.Take(10)),
                    "Importar relay", MessageBoxButton.OK, MessageBoxImage.Warning);
                return Task.CompletedTask;
            }

            if (!App.Relays.Save(imported, out var error))
            {
                MessageBox.Show("Relay inválido: " + error, "Importar relay",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return Task.CompletedTask;
            }

            var message = $"Relay «{imported.Name}» importado." +
                          (warnings.Count > 0 ? "\n\nAvisos:\n" + string.Join("\n", warnings.Take(8)) : string.Empty);
            MessageBox.Show(message, "Importar relay", MessageBoxButton.OK,
                warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Reload();
            SelectedRelay = Relays.FirstOrDefault(r => r.Id == imported.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo leer el archivo: " + ex.Message, "Importar relay",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand ImportJsonCommand => new(_ =>
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON de relays (*.json)|*.json",
            Title = "Importar relays (JSON exportado)",
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            var imported = RelayManager.ImportFromJson(File.ReadAllText(dialog.FileName), out var warnings);
            var count = 0;
            foreach (var relay in imported)
            {
                // Reasignar Ids para evitar colisiones con relays existentes.
                relay.Id = Guid.NewGuid().ToString("N");
                if (App.Relays.Save(relay, out var error))
                {
                    count++;
                }
                else
                {
                    warnings.Add($"«{relay.Name}»: {error}");
                }
            }

            MessageBox.Show($"Importados {count} relays." +
                            (warnings.Count > 0 ? "\n\nAvisos:\n" + string.Join("\n", warnings.Take(8)) : string.Empty),
                "Importar relays", MessageBoxButton.OK,
                warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo importar: " + ex.Message, "Importar relays",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand ExportJsonCommand => new(_ =>
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = "relays.json",
            Title = "Exportar relays (sin claves privadas)",
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            File.WriteAllText(dialog.FileName,
                RelayManager.ExportToJson(App.Relays.GetAll()));
            MessageBox.Show(
                "Relays exportados SIN claves privadas por seguridad.\nLa clave privada de cada relay permanece cifrada solo en este equipo.",
                "Exportar relays", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo exportar: " + ex.Message, "Exportar relays",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    /// <summary>Fija la clave privada escrita en la UI (la vista pasa el contenido del PasswordBox).</summary>
    public bool SetPrivateKeyFromUi(string privateKey)
    {
        if (SelectedRelay is not { } relay)
        {
            return false;
        }

        var persisted = App.Relays.SetPrivateKey(relay, privateKey);
        if (!persisted)
        {
            // Clave válida pero no persistible (sin DPAPI): se queda en memoria.
            PrivateKeyStatus = App.Relays.HasUsablePrivateKey(relay)
                ? "Clave cargada en memoria (no se pudo cifrar en este equipo; se pedirá de nuevo al reiniciar)."
                : "La clave no parece válida (debe ser base64 de 44 caracteres).";
        }
        else
        {
            PrivateKeyStatus = "Clave privada guardada cifrada (DPAPI).";
        }

        return persisted || App.Relays.HasUsablePrivateKey(relay);
    }

    protected override System.Windows.FrameworkElement BuildView() => new RelaysView { DataContext = this };
}
