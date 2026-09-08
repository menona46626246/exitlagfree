using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using GameRouteOptimizer.App.Views;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using Microsoft.Win32;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Gestión de juegos: lista, editor, detección e import/export JSON.</summary>
public sealed class GamesViewModel : SectionViewModel
{
    private GameProfile? _selected;
    private bool _isRunning;

    public GamesViewModel(AppServices app) : base(app)
    {
        App.Games.ProfilesChanged += (_, _) => RunOnUi(Reload);
        App.Relays.RelaysChanged += (_, _) => RunOnUi(OnPropertyChangedRelays);
        Reload();
    }

    public ObservableCollection<GameProfile> Profiles { get; } = new();

    public ObservableCollection<RelayNode> Relays { get; } = new();

    public GameProfile? SelectedProfile
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(IsEditing));
                RefreshRunningState();
            }
        }
    }

    public bool IsEditing => SelectedProfile is not null;

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public ObservableCollection<TargetRowViewModel> Targets { get; } = new();

    private void Reload()
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in App.Games.GetAll())
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();
        SyncEditor();
        OnPropertyChangedRelays();
    }

    private void OnPropertyChangedRelays()
    {
        var selected = SelectedProfile?.PreferredRelayId;
        Relays.Clear();
        foreach (var relay in App.Relays.GetEnabled())
        {
            Relays.Add(relay);
        }

        if (SelectedProfile is { } profile)
        {
            if (selected is not null && !Relays.Any(r => r.Id == selected))
            {
                profile.PreferredRelayId = null;
            }
        }
    }

    private void SyncEditor()
    {
        Targets.Clear();
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        foreach (var target in profile.Targets)
        {
            Targets.Add(new TargetRowViewModel(target, this));
        }
    }

    private void OnEditorFieldChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
    }

    public void AddTarget()
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var target = new GameServerTarget { DefaultProbe = ProbeKind.Icmp };
        profile.Targets.Add(target);
        var row = new TargetRowViewModel(target, this);
        Targets.Add(row);
    }

    public void RemoveTarget(TargetRowViewModel row)
    {
        if (SelectedProfile is { } profile && profile.Targets.Remove(row.Target))
        {
            Targets.Remove(row);
        }
    }

    public void RefreshRunningState()
    {
        IsRunning = SelectedProfile is { } profile && GameManager.IsGameRunning(profile);
    }

    // ----- comandos -----

    public Mvvm.AsyncRelayCommand AddTargetCommand => new(_ =>
    {
        AddTarget();
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand RemoveTargetCommand => new(parameter =>
    {
        if (parameter is TargetRowViewModel row)
        {
            RemoveTarget(row);
        }

        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand NewGameCommand => new(_ =>
    {
        // Un perfil nuevo se guarda sin servidores objetivo (válido para detectar el
        // proceso); el usuario añade servidores en el editor antes de optimizar.
        var profile = new GameProfile { Name = "Nuevo juego" };
        var saved = App.Games.Save(profile, out var error);
        if (!saved)
        {
            MessageBox.Show(error, "No se pudo crear el juego", MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        Reload();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        AddTarget();
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand SaveCommand => new(_ =>
    {
        if (SelectedProfile is not { } profile)
        {
            return Task.CompletedTask;
        }

        if (!App.Games.Save(profile, out var error))
        {
            MessageBox.Show(error, "No se pudo guardar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        MessageBox.Show("Perfil guardado.", "GameRoute Optimizer", MessageBoxButton.OK,
            MessageBoxImage.Information);
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand DeleteCommand => new(_ =>
    {
        if (SelectedProfile is not { } profile)
        {
            return Task.CompletedTask;
        }

        if (MessageBox.Show($"¿Eliminar el perfil «{profile.Name}»?", "Eliminar juego",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        App.Games.Delete(profile.Id);
        Reload();
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand DetectCommand => new(_ =>
    {
        if (SelectedProfile is not { } profile)
        {
            return Task.CompletedTask;
        }

        var names = profile.ExecutableNames.Count > 0
            ? profile.ExecutableNames
            : string.IsNullOrWhiteSpace(profile.ExecutablePath)
                ? new List<string> { profile.Name }
                : new List<string> { Path.GetFileName(profile.ExecutablePath) };

        if (MessageBox.Show(
                "Se buscará el ejecutable en rutas comunes (Program Files, Steam, Epic…). " +
                "Nunca se escanea todo el disco.\n¿Autorizas la búsqueda?",
                "Detectar juego instalado", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK)
        {
            return Task.CompletedTask;
        }

        var found = GameManager.FindInstalledExecutables(names);
        if (found.Count == 0)
        {
            MessageBox.Show("No se encontró el ejecutable en las rutas comunes. " +
                            "Escribe la ruta manualmente.", "Detección",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        var chosen = found[0];
        profile.ExecutablePath = chosen.FullPath;
        if (!profile.ExecutableNames.Contains(chosen.FileName))
        {
            profile.ExecutableNames.Add(chosen.FileName);
        }

        MessageBox.Show($"Encontrado: {chosen.FullPath}\nSe usará para detectar el proceso.",
            "Detección", MessageBoxButton.OK, MessageBoxImage.Information);
        OnPropertyChanged(nameof(SelectedProfile));
        App.Games.Save(profile, out var ignoredSaveError);
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand ImportCommand => new(_ =>
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON de juegos (*.json)|*.json",
            Title = "Importar perfiles de juego",
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var imported = GameManager.ImportFromJson(json, out var warnings);
            var importedCount = 0;
            foreach (var profile in imported)
            {
                if (App.Games.Save(profile, out var error))
                {
                    importedCount++;
                }
                else
                {
                    warnings.Add($"«{profile.Name}»: {error}");
                }
            }

            var message = $"Se importaron {importedCount} perfil(es)." +
                          (warnings.Count > 0 ? "\n\nAvisos:\n" + string.Join("\n", warnings.Take(8)) : string.Empty);
            MessageBox.Show(message, "Importación", MessageBoxButton.OK,
                warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo importar: " + ex.Message, "Importación",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand ExportCommand => new(_ =>
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = "juegos.json",
            Title = "Exportar perfiles de juego",
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            File.WriteAllText(dialog.FileName,
                GameManager.ExportToJson(App.Games.GetAll()));
            MessageBox.Show("Perfiles exportados (sin secretos: los perfiles no los contienen).",
                "Exportación", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo exportar: " + ex.Message, "Exportación",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand RefreshProcessCommand => new(_ =>
    {
        RefreshRunningState();
        return Task.CompletedTask;
    });

    public Mvvm.AsyncRelayCommand LaunchGameCommand => new(_ =>
    {
        if (SelectedProfile is not { ExecutablePath: { Length: > 0 } exe } profile)
        {
            MessageBox.Show("Este perfil no tiene ruta de ejecutable.", "Iniciar juego",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            };
            if (!string.IsNullOrWhiteSpace(profile.Arguments))
            {
                psi.Arguments = profile.Arguments;
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo iniciar: " + ex.Message, "Iniciar juego",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    });

    protected override System.Windows.FrameworkElement BuildView() => new GamesView { DataContext = this };

    public void NotifyEditorChanged() => OnEditorFieldChanged();
}

/// <summary>Fila editable de servidor objetivo (un target del perfil).</summary>
public sealed class TargetRowViewModel : ObservableObjectBase, INotifyPropertyChanged
{
    private readonly GamesViewModel _owner;

    public TargetRowViewModel(GameServerTarget target, GamesViewModel owner)
    {
        Target = target;
        _owner = owner;
    }

    public GameServerTarget Target { get; }

    public string Domain
    {
        get => Target.Domain ?? string.Empty;
        set
        {
            Target.Domain = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _owner.NotifyEditorChanged();
            OnPropertyChanged(nameof(RowTitle));
        }
    }

    public string IpAddress
    {
        get => Target.IpAddress ?? string.Empty;
        set
        {
            Target.IpAddress = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _owner.NotifyEditorChanged();
            OnPropertyChanged(nameof(RowTitle));
        }
    }

    public string PortsText
    {
        get => string.Join(", ", Target.Ports);
        set
        {
            var ports = new List<int>();
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var port) && port is > 0 and <= 65535)
                {
                    ports.Add(port);
                }
            }

            Target.Ports = ports;
            _owner.NotifyEditorChanged();
        }
    }

    public string Region
    {
        get => Target.Region ?? string.Empty;
        set
        {
            Target.Region = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _owner.NotifyEditorChanged();
        }
    }

    public string HttpHealthUrl
    {
        get => Target.HttpHealthUrl ?? string.Empty;
        set
        {
            Target.HttpHealthUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _owner.NotifyEditorChanged();
        }
    }

    public string[] ProbeTypes { get; } = { "ICMP", "TCP", "UDP", "HTTP/S" };

    public int ProbeIndex
    {
        get => Target.DefaultProbe switch
        {
            ProbeKind.TcpConnect => 1,
            ProbeKind.Udp => 2,
            ProbeKind.HttpGet => 3,
            _ => 0,
        };
        set
        {
            Target.DefaultProbe = value switch
            {
                1 => ProbeKind.TcpConnect,
                2 => ProbeKind.Udp,
                3 => ProbeKind.HttpGet,
                _ => ProbeKind.Icmp,
            };
            _owner.NotifyEditorChanged();
        }
    }

    public string RowTitle =>
        string.IsNullOrWhiteSpace(Target.Domain) && string.IsNullOrWhiteSpace(Target.IpAddress)
            ? "(nuevo servidor)"
            : Target.DisplayHost;

    public void RefreshTitle() => OnPropertyChanged(nameof(RowTitle));
}
