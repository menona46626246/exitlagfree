using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.App.ViewModels;

/// <summary>Wrapper de edición para el diálogo de relay (AllowedIPs como texto multilínea).</summary>
public sealed class RelayEditor : INotifyPropertyChanged
{
    private readonly RelayNode _relay;
    private string _allowedIpsText;

    public RelayEditor(RelayNode relay)
    {
        _relay = relay;
        _allowedIpsText = string.Join(Environment.NewLine, relay.AllowedIps);
    }

    public void Apply()
    {
        _relay.AllowedIps = _allowedIpsText
            .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0).ToList();
    }

    public string Name
    {
        get => _relay.Name;
        set { _relay.Name = value; OnPropertyChanged(); }
    }

    public string EndpointHost
    {
        get => _relay.EndpointHost;
        set { _relay.EndpointHost = value; OnPropertyChanged(); }
    }

    public int EndpointPort
    {
        get => _relay.EndpointPort;
        set { _relay.EndpointPort = value; OnPropertyChanged(); }
    }

    public string PublicKey
    {
        get => _relay.PublicKey;
        set { _relay.PublicKey = value; OnPropertyChanged(); }
    }

    public string AllowedIpsText
    {
        get => _allowedIpsText;
        set { _allowedIpsText = value; OnPropertyChanged(); }
    }

    public string Country
    {
        get => _relay.Country;
        set { _relay.Country = value; OnPropertyChanged(); }
    }

    public string City
    {
        get => _relay.City;
        set { _relay.City = value; OnPropertyChanged(); }
    }

    public string Provider
    {
        get => _relay.Provider;
        set { _relay.Provider = value; OnPropertyChanged(); }
    }

    public double? RelayToGameMs
    {
        get => _relay.RelayToGameMs;
        set { _relay.RelayToGameMs = value; OnPropertyChanged(); }
    }

    public double? LoadPercent
    {
        get => _relay.LoadPercent;
        set { _relay.LoadPercent = value; OnPropertyChanged(); }
    }

    public int Priority
    {
        get => _relay.Priority;
        set { _relay.Priority = value; OnPropertyChanged(); }
    }

    public string InternalDns
    {
        get => _relay.InternalDns;
        set { _relay.InternalDns = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _relay.IsEnabled;
        set { _relay.IsEnabled = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
