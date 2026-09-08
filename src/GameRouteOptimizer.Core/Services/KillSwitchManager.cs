using GameRouteOptimizer.Core.Privileged;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Kill switch opcional: bloquea la salida por interfaces físicas salvo el túnel.
/// Se activa solo junto al túnel y SIEMPRE se desactiva al detener (botón de emergencia incluido).
/// Requiere elevación; si no está disponible se informa claramente.
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
        IReadOnlyCollection<string> allowedInterfaces,
        CancellationToken ct)
    {
        var allowed = allowedInterfaces.ToList();
        allowed.Add(tunnelInterfaceName);

        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.KillSwitchEnable,
            new KillSwitchPayload { AllowedInterfaceNames = allowed },
            reason: "Activar kill switch (bloqueo de salida fuera del túnel)");
        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (result.Ok)
        {
            lock (_gate)
            {
                IsEnabled = true;
            }

            Changed?.Invoke(this, new KillSwitchChangedEventArgs(true, result.Detail));
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

            Changed?.Invoke(this, new KillSwitchChangedEventArgs(false, result.Detail));
        }

        return (result.Ok, result.Ok ? null : result.Error);
    }

    /// <summary>Desactiva el kill switch ignorando errores (para botón de emergencia).</summary>
    public async Task ForceDisableAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await DisableAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Último recurso: se documenta en logs del llamador.
        }
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
