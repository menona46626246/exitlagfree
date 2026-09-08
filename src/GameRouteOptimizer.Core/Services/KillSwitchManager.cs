using GameRouteOptimizer.Core.Privileged;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Kill switch opcional: bloquea la salida de red salvo el túnel, su endpoint (handshake)
/// y la subred local. Mecanismo real en el proceso elevado (cambia la acción de salida por
/// defecto del firewall y añade reglas de permitido; ver OpExecutor).
/// Se activa solo junto al túnel y SIEMPRE se desactiva al detener (botón de emergencia incluido).
/// Si la elevación no está disponible o falla, el túnel sigue funcionando SIN bloqueo y se avisa.
/// </summary>
public sealed class KillSwitchManager
{
    private readonly IPrivilegedOps _ops;
    private readonly object _gate = new();

    public event EventHandler<KillSwitchChangedEventArgs>? Changed;

    public bool IsEnabled { get; private set; }
    public bool SupportsRealKillSwitch => _ops.IsAvailable;

    public KillSwitchManager(IPrivilegedOps ops)
    {
        _ops = ops;
    }

    public async Task<(bool Ok, string? Error)> EnableAsync(
        string tunnelInterfaceName,
        IReadOnlyCollection<string> endpointIps,
        int endpointPort,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (IsEnabled)
            {
                return (true, null);
            }
        }

        if (!_ops.IsAvailable)
        {
            return (false,
                "El kill switch requiere un proceso elevado; no está disponible en este modo.");
        }

        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.KillSwitchEnable,
            new KillSwitchPayload
            {
                TunnelInterfaceName = tunnelInterfaceName,
                EndpointIps = endpointIps.ToList(),
                EndpointPort = endpointPort,
            },
            reason: "Activar kill switch (bloquear salida salvo túnel/endpoint/LAN)");
        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (result.Ok)
        {
            lock (_gate)
            {
                IsEnabled = true;
            }

            Changed?.Invoke(this, new KillSwitchChangedEventArgs(true, result.Detail ?? result.StandardOutput));
        }

        return (result.Ok, result.Ok ? null : result.Error);
    }

    public async Task<(bool Ok, string? Error)> DisableAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (!IsEnabled)
            {
                return (true, null);
            }
        }

        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.KillSwitchDisable,
            new KillSwitchPayload(),
            reason: "Desactivar kill switch (restaurar red normal)");
        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (result.Ok)
        {
            lock (_gate)
            {
                IsEnabled = false;
            }

            Changed?.Invoke(this, new KillSwitchChangedEventArgs(false, result.Detail ?? result.StandardOutput));
        }

        return (result.Ok, result.Ok ? null : result.Error);
    }

    /// <summary>Desactiva el kill switch ignorando errores (para botón de emergencia y arranque).</summary>
    public async Task ForceDisableAsync()
    {
        await ForceDisableWithResultAsync().ConfigureAwait(false);
    }

    /// <summary>Desactiva el kill switch devolviendo el resultado (emergencia/recuperación, con logs).</summary>
    public async Task<(bool Ok, string? Error)> ForceDisableWithResultAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            return await DisableAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Ejecuta la desactivación SIEMPRE, aunque este proceso no tenga el kill switch marcado
    /// como activo (p. ej. recuperación tras un cierre inesperado: el firewall quedó bloqueado
    /// por una ejecución anterior). La operación es idempotente en el lado elevado.
    /// </summary>
    public async Task<(bool Ok, string? Error)> CleanupAndDisableAsync(CancellationToken ct)
    {
        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.KillSwitchDisable,
            new KillSwitchPayload(),
            reason: "recuperación: desactivar kill switch de una sesión anterior");
        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (result.Ok)
        {
            lock (_gate)
            {
                IsEnabled = false;
            }

            Changed?.Invoke(this, new KillSwitchChangedEventArgs(false, result.Detail ?? result.StandardOutput));
        }

        return (result.Ok, result.Ok ? null : result.Error);
    }
}

public sealed class KillSwitchChangedEventArgs : EventArgs
{
    public bool Enabled { get; }
    public string? Detail { get; }

    public KillSwitchChangedEventArgs(bool enabled, string? detail)
    {
        Enabled = enabled;
        Detail = detail;
    }
}
