using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using GameRouteOptimizer.App.Infrastructure;
using GameRouteOptimizer.Core.Game;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>VM de Juegos: lista de perfiles, CRUD, import/export, detección de instalados.</summary>
public sealed class GamesViewModel : ViewModelBase
{
    private readonly AppServices _svc;
    private GameProfile? _selected;

    public GamesViewModel(AppServices svc)
    {
        _svc = svc;
        Profiles = new ObservableCollection<GameProfile>(svc.Profiles.Profiles);
        svc.Profiles.ProfileAdded += p => DispatcherSafe(() => { Profiles.Add(p); });
        svc.Profiles.ProfileRemoved += id => DispatcherSafe(() =>
        {
            var item = Profiles.FirstOrDefault(p => p.Id == id);
            if (item is not null)
            {
                Profiles.Remove(item);
            }
        });

        AddCommand = new RelayCommand(Add);
        EditCommand = new RelayCommand(Edit, () => Selected is not null);
        RemoveCommand = new RelayCommand(Remove, () => Selected is not null);
        DetectCommand = new AsyncRelayCommand(DetectInstalledAsync);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export, () => Selected is not null);
        IsRunningCommand = new RelayCommand(CheckRunning);
    }

    public ObservableCollection<GameProfile> Profiles { get; }

    public GameProfile? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand DetectCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand IsRunningCommand { get; }

    public event Action<GameProfile>? RequestEdit;

    /// <summary>Recarga la lista desde el manager (tras añadir/editar desde el diálogo).</summary>
    public void Reload()
    {
        Profiles.Clear();
        foreach (var p in _svc.Profiles.Profiles)
        {
            Profiles.Add(p);
        }

        OnPropertyChanged(nameof(Profiles));
    }

    private void Add()
    {
        var profile = new GameProfile { Name = "Nuevo juego" };
        RequestEdit?.Invoke(profile);
    }

    private void Edit() { if (Selected is { } p)
        {
            RequestEdit?.Invoke(p);
        }
    }

    private void Remove()
    {
        if (Selected is null)
        {
            return;
        }

        _svc.Profiles.Remove(Selected.Id);
        _svc.Notifications.Info($"Perfil '{Selected.Name}' eliminado.");
    }

    private async Task DetectInstalledAsync()
    {
        if (!_svc.Settings.AllowGameScan)
        {
            _svc.Notifications.Warning("La detección de juegos instalados requiere tu autorización. Actívala en Configuración (solo se leen nombres de ejecutables).");
            return;
        }
        IsBusy = true;
        BusyText = "Buscando ejecutables en las carpetas autorizadas…";
        try
        {
            await Task.Run(() =>
            {
                var found = _svc.Games.DetectInstalledGames(_svc.Settings.GameScanRoots);
                foreach (var (profileId, exePath) in found)
                {
                    var profile = _svc.Profiles.GetById(profileId);
                    if (profile is null)
                    {
                        continue;
                    }

                    profile.ExecutablePath = exePath;
                    profile.Executables = [System.IO.Path.GetFileNameWithoutExtension(exePath)];
                    _svc.Profiles.Update(profile);
                    _svc.Notifications.Success($"'{profile.Name}' → {exePath}");
                }
                if (found.Count == 0)
                {
                    _svc.Notifications.Info("No se encontraron ejecutables coincidentes en las carpetas configuradas.");
                }
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar perfil de juego",
            Filter = "JSON (*.json)|*.json|Todos (*.*)|*.*"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        try
        {
            var profile = _svc.Profiles.ImportProfileJson(File.ReadAllText(dialog.FileName));
            _svc.Notifications.Success($"Perfil '{profile.Name}' importado.");
        }
        catch (Exception ex)
        {
            _svc.Notifications.Error($"Importación: {ex.Message}");
        }
    }

    private void Export()
    {
        if (Selected is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar perfil de juego",
            FileName = $"{Selected.Name}.json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, _svc.Profiles.ExportProfileJson(Selected));
        _svc.Notifications.Success($"Perfil exportado a {dialog.FileName} (sin secretos: los perfiles nunca los contienen).");
    }

    private void CheckRunning()
    {
        if (Selected is null)
        {
            return;
        }

        var running = _svc.Games.IsProfileRunning(Selected, out var matched);
        _svc.Notifications.Info(running
            ? $"'{Selected.Name}' está EN EJECUCIÓN (proceso {matched})."
            : $"'{Selected.Name}' no se está ejecutando.");
    }

    private void DispatcherSafe(Action action)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(action);
    }
}
