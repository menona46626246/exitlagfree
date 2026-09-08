using System.Text.Json.Serialization;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Privileged;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>
/// Información del túnel activo.
/// </summary>
public sealed class ActiveTunnel
{
    public required string RelayId { get; init; }
    public required string RelayName { get; init; }
    public required string InterfaceName { get; init; }
    public required RouteMode Mode { get; init; }
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? ConfigPath { get; init; }

    [JsonIgnore]
    public string DisplayName => $"{RelayName} ({(Mode == RouteMode.TunnelGlobal ? "global" : "solo juego")})";
}

/// <summary>
/// Gestor de túnel WireGuard. Conecta/desconecta usando operaciones privilegiadas
/// (herramientas oficiales) y mantiene el estado local del túnel activo.
/// </summary>
public sealed class TunnelManager
{
    private readonly IPrivilegedOps _ops;
    private readonly object _gate = new();

    public event EventHandler<TunnelStatusChangedEventArgs>? StatusChanged;

    public ActiveTunnel? Active { get; private set; }

    public TunnelProviderState LastKnownState { get; private set; } = TunnelProviderState.NotInstalled;

    public TunnelManager(IPrivilegedOps ops)
    {
        _ops = ops;
    }

    public bool OpsAvailable => _ops.IsAvailable;

    /// <summary>
    /// Conecta el túnel: construye la configuración y pide su instalación (crea interfaz y rutas).
    /// </summary>
    public async Task<(bool Ok, string? Error)> ConnectAsync(
        WireGuardConfig config,
        string tunnelName,
        RouteMode mode,
        string relayId,
        string relayName,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (Active is not null)
            {
                return (false, "Ya hay un túnel activo; deténlo antes de conectar otro.");
            }
        }

        if (!_ops.IsAvailable)
        {
            return (false,
                "Las operaciones de túnel requieren un proceso elevado. " +
                "Si estás en modo diagnóstico, el túnel no puede activarse.");
        }

        // Validación estructural antes de tocar el sistema.
        var validation = new WireGuardParseResult();
        WireGuardConfigParser.Validate(config, validation);
        if (validation.Errors.Count > 0)
        {
            return (false, "Configuración inválida: " + string.Join(" | ", validation.Errors));
        }

        var confText = WireGuardConfigParser.ToConfText(config);
        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.InstallWireGuardTunnel,
            new WireGuardTunnelPayload { ConfigText = confText, TunnelName = tunnelName },
            reason: $"Activar túnel {relayName} ({mode})");

        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (!result.Ok)
        {
            RaiseStatus(TunnelProviderState.Error, tunnelName, result.Error);
            return (false, result.Error ?? "No se pudo crear el túnel.");
        }

        lock (_gate)
        {
            Active = new ActiveTunnel
            {
                RelayId = relayId,
                RelayName = relayName,
                InterfaceName = tunnelName,
                Mode = mode,
                ConfigPath = result.Detail,
            };
        }

        LastKnownState = TunnelProviderState.Active;
        RaiseStatus(TunnelProviderState.Active, tunnelName, result.Detail);
        return (true, null);
    }

    /// <summary>Desconecta el túnel (WireGuard elimina interfaz y rutas al desinstalar el servicio).</summary>
    public async Task<(bool Ok, string? Error)> DisconnectAsync(string reason, CancellationToken ct)
    {
        ActiveTunnel? tunnel;
        lock (_gate)
        {
            tunnel = Active;
            Active = null;
        }

        if (tunnel is null)
        {
            LastKnownState = TunnelProviderState.NotInstalled;
            RaiseStatus(TunnelProviderState.NotInstalled, null, "sin túnel activo");
            return (true, null);
        }

        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.UninstallWireGuardTunnel,
            new WireGuardTunnelPayload { TunnelName = tunnel.InterfaceName },
            reason: $"Detener túnel: {reason}");
        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);

        LastKnownState = result.Ok ? TunnelProviderState.NotInstalled : TunnelProviderState.Error;
        RaiseStatus(LastKnownState, tunnel.InterfaceName, result.Error ?? "túnel detenido");
        return (result.Ok, result.Ok ? null : result.Error);
    }

    /// <summary>Consulta el estado real del túnel (wg show).</summary>
    public async Task<TunnelStatus> GetStatusAsync(CancellationToken ct)
    {
        if (Active is null)
        {
            return new TunnelStatus { State = TunnelProviderState.NotInstalled };
        }

        var op = new PrivilegedOp
        {
            Kind = PrivilegedOpKind.QueryWireGuardTunnel,
            Reason = "monitoreo de salud del túnel",
        };
        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (!result.Ok)
        {
            return new TunnelStatus { State = TunnelProviderState.Error, Detail = result.Error };
        }

        // wg show all imprime "interfaz: <nombre>" para cada túnel existente.
        var output = result.StandardOutput ?? string.Empty;
        var up = output.Contains($" {Active.InterfaceName}:", StringComparison.OrdinalIgnoreCase)
                 || output.Contains($"{Active.InterfaceName}:", StringComparison.OrdinalIgnoreCase);

        return new TunnelStatus
        {
            State = up ? TunnelProviderState.Active : TunnelProviderState.Degraded,
            InterfaceName = Active.InterfaceName,
            Detail = output.Length > 400 ? output[..400] : output,
        };
    }

    private void RaiseStatus(TunnelProviderState state, string? interfaceName, string? detail)
    {
        LastKnownState = state;
        StatusChanged?.Invoke(this,
            new TunnelStatusChangedEventArgs(state, interfaceName, detail));
    }
}

public sealed class TunnelStatusChangedEventArgs : EventArgs
{
    public TunnelProviderState State { get; }
    public string? InterfaceName { get; }
    public string? Detail { get; }

    public TunnelStatusChangedEventArgs(TunnelProviderState state, string? interfaceName, string? detail)
    {
        State = state;
        InterfaceName = interfaceName;
        Detail = detail;
    }
}
