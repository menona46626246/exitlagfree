using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using GameRouteOptimizer.App.Infrastructure;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Relay;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>VM de Relays: CRUD, import/export JSON, importar .conf WireGuard, medir salud.</summary>
public sealed class RelaysViewModel : ViewModelBase
{
    private readonly AppServices _svc;
    private RelayNode? _selected;

    public RelaysViewModel(AppServices svc)
    {
        _svc = svc;
        Relays = new ObservableCollection<RelayNode>(svc.Relays.Relays);
        svc.Relays.RelayAdded += r => DispatcherSafe(() => Relays.Add(r));
        svc.Relays.RelayRemoved += id => DispatcherSafe(() =>
        {
            var item = Relays.FirstOrDefault(r => r.Id == id);
            if (item is not null)
            {
                Relays.Remove(item);
            }
        });

        AddCommand = new RelayCommand(Add);
        EditCommand = new RelayCommand(Edit, () => Selected is not null);
        RemoveCommand = new RelayCommand(Remove, () => Selected is not null);
        MeasureCommand = new AsyncRelayCommand(MeasureAsync, () => Selected is not null);
        ImportJsonCommand = new RelayCommand(ImportJson);
        ExportJsonCommand = new RelayCommand(ExportJson);
        ImportWgCommand = new RelayCommand(ImportWireGuardConf);
        ToggleCommand = new RelayCommand(Toggle, () => Selected is not null);
    }

    public ObservableCollection<RelayNode> Relays { get; }

    public RelayNode? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand MeasureCommand { get; }
    public ICommand ImportJsonCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ImportWgCommand { get; }
    public ICommand ToggleCommand { get; }

    public event Action<RelayNode>? RequestEdit;

    /// <summary>Recarga la lista desde el manager.</summary>
    public void Reload()
    {
        Relays.Clear();
        foreach (var r in _svc.Relays.Relays)
        {
            Relays.Add(r);
        }

        OnPropertyChanged(nameof(Relays));
    }

    private void Add()
    {
        var relay = new RelayNode { Name = "Nuevo relay", EndpointPort = 51820 };
        RequestEdit?.Invoke(relay);
    }

    private void Edit() { if (Selected is { } r)
        {
            RequestEdit?.Invoke(r);
        }
    }

    private void Remove()
    {
        if (Selected is null)
        {
            return;
        }

        _svc.Relays.Remove(Selected.Id);
        _svc.Notifications.Info($"Relay '{Selected.Name}' eliminado.");
    }

    private async Task MeasureAsync()
    {
        if (Selected is null)
        {
            return;
        }

        IsBusy = true;
        BusyText = $"Midiendo '{Selected.Name}'…";
        var relay = Selected;
        try
        {
            var result = await _svc.RelayProber.TestRelayAsync(relay, ProbeMode.Quick);
            _svc.Relays.Update(relay);
            _svc.Notifications.Info($"'{relay.Name}': {(result.Reachable ? $"alcanzable ({result.MeanMs:0.0} ms)" : "sin respuesta")} — {result.Note}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ImportJson()
    {
        var dialog = new OpenFileDialog { Title = "Importar relays", Filter = "JSON (*.json)|*.json|Todos (*.*)|*.*" };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        try
        {
            var count = _svc.Relays.ImportRelaysJson(File.ReadAllText(dialog.FileName));
            _svc.Notifications.Success($"{count} relays importados.");
        }
        catch (Exception ex)
        {
            _svc.Notifications.Error($"Importación: {ex.Message}");
        }
    }

    private void ExportJson()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar relays (SIN secretos)",
            FileName = "relays.json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, _svc.Relays.ExportRelaysJson(includeSecretRefs: false));
        _svc.Notifications.Success("Relays exportados sin referencias a claves privadas.");
    }

    private void ImportWireGuardConf()
    {
        var dialog = new OpenFileDialog { Title = "Importar configuración WireGuard (.conf)", Filter = "WireGuard (*.conf)|*.conf|Texto (*.txt)|*.txt" };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        try
        {
            var relay = _svc.Relays.ImportWireGuardConfig(File.ReadAllText(dialog.FileName),
                System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
            _svc.Notifications.Success($"Relay '{relay.Name}' importado. La clave privada se cifró con {_svc.Settings.SafeMode switch { _ => "DPAPI/máquina local" }}.");
        }
        catch (Exception ex)
        {
            _svc.Notifications.Error($"Importación WireGuard: {ex.Message}");
        }
    }

    private void Toggle()
    {
        if (Selected is null)
        {
            return;
        }

        _svc.Relays.SetEnabled(Selected.Id, !Selected.IsEnabled);
    }

    private void DispatcherSafe(Action action)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(action);
    }
}
