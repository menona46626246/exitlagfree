using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>
/// Wrapper de edición para el diálogo de perfil: expone propiedades amigables
/// (lista de ejecutables como texto, servidores como JSON) sin contaminar el modelo.
/// </summary>
public sealed class GameProfileEditor : INotifyPropertyChanged
{
    private readonly GameProfile _profile;
    private string _executablesText;
    private string _serversJson;
    private string _serversError = string.Empty;

    public GameProfileEditor(GameProfile profile)
    {
        _profile = profile;
        _executablesText = string.Join(Environment.NewLine, profile.Executables);
        var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        _serversJson = JsonSerializer.Serialize(profile.Servers, opts);
    }

    /// <summary>Aplica los campos de UI al modelo subyacente.</summary>
    public void Apply()
    {
        _profile.Executables = _executablesText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0).ToList();
        try
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
            var servers = JsonSerializer.Deserialize<List<GameServer>>(_serversJson, opts);
            if (servers is not null)
            {
                _profile.Servers = servers;
            }

            ServersError = string.Empty;
        }
        catch (JsonException ex)
        {
            ServersError = "JSON de servidores inválido: " + ex.Message;
        }
    }

    public bool HasServersError => ServersError.Length > 0;

    public string ServersError
    {
        get => _serversError;
        private set
        {
            _serversError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasServersError));
        }
    }

    public string Name
    {
        get => _profile.Name;
        set { _profile.Name = value; OnPropertyChanged(); }
    }

    public string ExecutablesText
    {
        get => _executablesText;
        set { _executablesText = value; OnPropertyChanged(); }
    }

    public string ExecutablePath
    {
        get => _profile.ExecutablePath;
        set { _profile.ExecutablePath = value; OnPropertyChanged(); }
    }

    public string LaunchArguments
    {
        get => _profile.LaunchArguments;
        set { _profile.LaunchArguments = value; OnPropertyChanged(); }
    }

    public string ServersJson
    {
        get => _serversJson;
        set { _serversJson = value; OnPropertyChanged(); }
    }

    public ProbeProtocol PreferredProtocol
    {
        get => _profile.PreferredProtocol;
        set { _profile.PreferredProtocol = value; OnPropertyChanged(); }
    }

    public RoutingMode RoutingMode
    {
        get => _profile.RoutingMode;
        set { _profile.RoutingMode = value; OnPropertyChanged(); }
    }

    public bool AutoStartOptimization
    {
        get => _profile.AutoStartOptimization;
        set { _profile.AutoStartOptimization = value; OnPropertyChanged(); }
    }

    public bool StopOnGameExit
    {
        get => _profile.StopOnGameExit;
        set { _profile.StopOnGameExit = value; OnPropertyChanged(); }
    }

    public string PreferredRelayId
    {
        get => _profile.PreferredRelayId;
        set { _profile.PreferredRelayId = value; OnPropertyChanged(); }
    }

    public string SpecialRules
    {
        get => _profile.SpecialRules;
        set { _profile.SpecialRules = value; OnPropertyChanged(); }
    }

    public string Notes
    {
        get => _profile.Notes;
        set { _profile.Notes = value; OnPropertyChanged(); }
    }

    public Array ProtocolOptions { get; } = Enum.GetValues<ProbeProtocol>();
    public Array RoutingModeOptions { get; } = Enum.GetValues<RoutingMode>();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
