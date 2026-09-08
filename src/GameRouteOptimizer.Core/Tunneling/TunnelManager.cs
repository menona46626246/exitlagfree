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
/// Gestor de túnel WireGuard. Conecta usando una única operación elevada por acción
/// (secuencia: instalar → esperar interfaz → aplicar DNS) y mantiene el estado local del túnel.
/// Al desconectar, la desinstalación elimina interfaz, rutas y DNS; si falla y el túnel tenía
/// DNS estático aplicado, se intenta restaurarlo como operación aparte.
/// </summary>
public sealed class TunnelManager
{
    private readonly IPrivilegedOps _ops;
    private readonly object _gate = new();

    /// <summary>Ventana de frescura del estado consultado (evita elevar el proceso a cada tick).</summary>
    private static readonly TimeSpan StatusFreshWindow = TimeSpan.FromSeconds(15);

    private DateTimeOffset _lastStatusQueryUtc;
    private TunnelStatus? _cachedStatus;
    private bool _dnsApplied;

    public event EventHandler<TunnelStatusChangedEventArgs>? StatusChanged;

    public ActiveTunnel? Active { get; private set; }

    public TunnelProviderState LastKnownState { get; private set; } = TunnelProviderState.NotInstalled;

    public TunnelManager(IPrivilegedOps ops)
    {
        _ops = ops;
    }

    public bool OpsAvailable => _ops.IsAvailable;

    /// <summary>
    /// Conecta el túnel: una sola operación elevada hace instalar la interfaz, esperar a que esté
    /// operativa y aplicar el DNS de la configuración (si trae servidores IPv4). Si algo falla a
    /// mitad, se intenta desinstalar el túnel recién creado (rollback) antes de devolver el error.
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
        var steps = new List<PrivilegedOp>
        {
            PrivilegedOp.WithPayload(PrivilegedOpKind.InstallWireGuardTunnel,
                new WireGuardTunnelPayload { ConfigText = confText, TunnelName = tunnelName },
                reason: "instalar interfaz del túnel"),
            PrivilegedOp.WithPayload(PrivilegedOpKind.WaitWireGuardTunnel,
                new WireGuardTunnelPayload { TunnelName = tunnelName, TimeoutSeconds = 25 },
                reason: "esperar a la interfaz del túnel"),
        };

        // DNS del túnel: solo servidores IPv4 literales (lo que acepta netsh).
        var dnsServers = TunnelDns.Ipv4Servers(config.Dns);
        if (dnsServers.Count > 0)
        {
            steps.Add(PrivilegedOp.WithPayload(PrivilegedOpKind.SetInterfaceDns,
                new DnsPayload { InterfaceName = tunnelName, Servers = dnsServers },
                reason: "aplicar DNS del túnel: " + string.Join(", ", dnsServers)));
        }

        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.Sequence,
            new SequencePayload { Steps = steps },
            reason: $"Activar túnel {relayName} ({mode})");

        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (!result.Ok)
        {
            RaiseStatus(TunnelProviderState.Error, tunnelName, result.Error);
            var rollbackNote = await TryRemoveLeftoverAsync(tunnelName).ConfigureAwait(false);
            return (false, result.Error + rollbackNote);
        }

        lock (_gate)
        {
            _dnsApplied = dnsServers.Count > 0;
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

    /// <summary>
    /// Desconecta el túnel: desinstala el servicio, con lo que WireGuard elimina la interfaz,
    /// sus rutas y su DNS automáticamente. Si la desinstalación falla y el túnel llegó a tener
    /// DNS estático aplicado, se intenta restaurar el DNS de la interfaz como paso aparte.
    /// </summary>
    public async Task<(bool Ok, string? Error)> DisconnectAsync(string reason, CancellationToken ct)
    {
        ActiveTunnel? tunnel;
        bool restoreDns;
        lock (_gate)
        {
            tunnel = Active;
            restoreDns = _dnsApplied;
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

        if (!result.Ok && restoreDns)
        {
            // La interfaz sigue existiendo (no se pudo desinstalar): se restaura su DNS para
            // no dejar un servidor DNS muerto configurado, y se informa del fallo real.
            try
            {
                var restoreOp = PrivilegedOp.WithPayload(PrivilegedOpKind.RestoreInterfaceDns,
                    new DnsPayload { InterfaceName = tunnel.InterfaceName },
                    reason: "restaurar DNS tras fallo de desinstalación");
                var restoreResult = await _ops.RunAsync(restoreOp, ct).ConfigureAwait(false);
                if (!restoreResult.Ok)
                {
                    result = PrivilegedOpResult.Failure(
                        result.Error + " Además no se pudo restaurar el DNS de la interfaz: " + restoreResult.Error);
                }
            }
            catch (Exception ex)
            {
                result = PrivilegedOpResult.Failure(result.Error + " Además no se pudo restaurar el DNS: " + ex.Message);
            }
        }

        lock (_gate)
        {
            if (result.Ok)
            {
                _dnsApplied = false;
                Active = null;
            }
        }

        LastKnownState = result.Ok ? TunnelProviderState.NotInstalled : TunnelProviderState.Error;
        RaiseStatus(LastKnownState, tunnel.InterfaceName, result.Error ?? "túnel detenido");
        return (result.Ok, result.Ok ? null : result.Error);
    }

    /// <summary>
    /// Consulta el estado real del túnel (wg show). Primero intenta una consulta SIN elevar el
    /// proceso (si wg.exe lo permite en esa instalación); si el sistema exige administrador,
    /// solo se eleva cuando <paramref name="force"/> es true o la caché ha caducado, para no
    /// pedir UAC en cada comprobación del bucle de monitoreo.
    /// </summary>
    public async Task<TunnelStatus> GetStatusAsync(CancellationToken ct, bool force = false)
    {
        ActiveTunnel? tunnel;
        lock (_gate)
        {
            tunnel = Active;
            if (tunnel is null)
            {
                return new TunnelStatus { State = TunnelProviderState.NotInstalled };
            }
        }

        var now = DateTimeOffset.UtcNow;
        TunnelStatus? cached;
        lock (_gate)
        {
            cached = _cachedStatus;
            if (!force && cached is not null && now - _lastStatusQueryUtc < StatusFreshWindow)
            {
                return new TunnelStatus
                {
                    State = cached.State,
                    InterfaceName = cached.InterfaceName,
                    Detail = (cached.Detail is { Length: > 0 } d ? d + " · " : string.Empty) +
                             $"consulta reciente (hace {Math.Max(0, (int)(now - _lastStatusQueryUtc).TotalSeconds)} s)",
                    AtUtc = now,
                };
            }
        }

        // 1) Intento sin privilegios (lectura): en instalaciones donde wg.exe permite consultar
        //    sin elevar, el monitoreo continuo no pide UAC.
        var unprivileged = await TryUnprivilegedQueryAsync(tunnel.InterfaceName, ct).ConfigureAwait(false);
        TunnelStatus status;
        if (unprivileged.Ok)
        {
            // La interfaz existe; ahora se mira el handshake: un túnel «up» sin handshake
            // reciente está caído aunque wg show responda (p. ej. cable roto por el medio).
            status = StatusFromWgDump(tunnel.InterfaceName, unprivileged.Output);
        }
        else if (unprivileged.NeedsElevation && !force)
        {
            // Sin permiso y sin orden de forzar: se conserva el último estado conocido.
            var lastState = LastKnownState == TunnelProviderState.Active
                ? TunnelProviderState.Active
                : TunnelProviderState.Degraded;
            status = new TunnelStatus
            {
                State = lastState,
                InterfaceName = tunnel.InterfaceName,
                Detail = "El sistema exige administrador para consultar el túnel; " +
                         "se mantiene el estado anterior (consulta completa cada cierto tiempo).",
            };
        }
        else if (unprivileged.NeedsElevation)
        {
            // 2) Consulta elevada (bajo demanda: sospecha de caída o cadencia larga).
            var op = new PrivilegedOp
            {
                Kind = PrivilegedOpKind.QueryWireGuardTunnel,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(
                    new WireGuardTunnelPayload { TunnelName = tunnel.InterfaceName }),
                Reason = "monitoreo de salud del túnel",
            };
            var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
            if (result.Ok)
            {
                status = StatusFromWgDump(tunnel.InterfaceName, result.StandardOutput);
            }
            else if (IsTransientOpError(result.Error))
            {
                var lastState2 = LastKnownState == TunnelProviderState.Active
                    ? TunnelProviderState.Active
                    : TunnelProviderState.Degraded;
                status = new TunnelStatus
                {
                    State = lastState2,
                    InterfaceName = tunnel.InterfaceName,
                    Detail = "No se pudo consultar el túnel (permiso no concedido); se mantiene el estado anterior.",
                };
            }
            else
            {
                status = new TunnelStatus
                {
                    State = TunnelProviderState.Degraded,
                    InterfaceName = tunnel.InterfaceName,
                    Detail = result.Error,
                };
            }
        }
        else if (unprivileged.Output is null)
        {
            // wg.exe no está disponible o no se pudo ejecutar: no se puede afirmar que el
            // túnel haya caído; se conserva el último estado y la sonda del destino decide.
            var lastState3 = LastKnownState == TunnelProviderState.Active
                ? TunnelProviderState.Active
                : TunnelProviderState.Degraded;
            status = new TunnelStatus
            {
                State = lastState3,
                InterfaceName = tunnel.InterfaceName,
                Detail = "No se pudo consultar el estado del túnel (wg.exe no disponible); " +
                         "se mantiene el estado anterior.",
            };
        }
        else
        {
            // wg respondió con un error real (p. ej. la interfaz ya no existe).
            status = new TunnelStatus
            {
                State = TunnelProviderState.Degraded,
                InterfaceName = tunnel.InterfaceName,
                Detail = unprivileged.Output,
            };
        }

        lock (_gate)
        {
            _lastStatusQueryUtc = DateTimeOffset.UtcNow;
            _cachedStatus = status;
        }

        return status;
    }

    /// <summary>Un túnel sin handshake renovado en este tiempo se considera caído.</summary>
    private static readonly TimeSpan HandshakeStaleAfter = TimeSpan.FromSeconds(180);

    /// <summary>
    /// Interpreta la salida de «wg show &lt;interfaz&gt; dump»: activo si algún peer tiene un
    /// handshake reciente; degradado si todos los peers llevan demasiado sin renovarlo.
    /// Si no hay datos utilizables (interfaz recién creada, sin peers o formato inesperado)
    /// no se afirma nada: se conserva Active y la sonda del destino decide.
    /// </summary>
    private static TunnelStatus StatusFromWgDump(string interfaceName, string? output)
    {
        var baseStatus = new TunnelStatus
        {
            State = TunnelProviderState.Active,
            InterfaceName = interfaceName,
            Detail = Truncate(output),
        };

        if (string.IsNullOrWhiteSpace(output))
        {
            return baseStatus;
        }

        var now = DateTimeOffset.UtcNow;
        var sawHandshake = false;
        var oldestAge = TimeSpan.Zero;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // «dump»: la 1ª línea es la interfaz (sin campo de handshake); cada línea de peer
            // trae: public_key, preshared_key, endpoint, allowed_ips, latest-handshake, rx, tx, keepalive.
            var fields = line.Split('\t');
            if (fields.Length < 5 || !long.TryParse(fields[4], out var epoch) || epoch <= 0)
            {
                continue;
            }

            sawHandshake = true;
            var age = now - DateTimeOffset.FromUnixTimeSeconds(epoch);
            if (age < oldestAge || oldestAge == TimeSpan.Zero)
            {
                oldestAge = age;
            }

            if (age <= HandshakeStaleAfter)
            {
                return baseStatus;
            }
        }

        if (sawHandshake)
        {
            // Todos los peers tienen el handshake vencido: el enlace está muerto aunque la
            // interfaz exista. Esto alimenta el contador de fallos del orquestador (failback).
            return new TunnelStatus
            {
                State = TunnelProviderState.Degraded,
                InterfaceName = interfaceName,
                Detail = $"El túnel no renueva el handshake de WireGuard (último hace " +
                         $"{(long)oldestAge.TotalSeconds} s); se considera caído. " + Truncate(output),
            };
        }

        return baseStatus;
    }

    /// <summary>
    /// Ejecuta «wg show &lt;interfaz&gt; dump» SIN elevar el proceso. Devuelve NeedsElevation=true
    /// cuando el propio wg.exe indica que hace falta administrador.
    /// </summary>
    private static async Task<UnprivilegedQueryResult> TryUnprivilegedQueryAsync(string interfaceName, CancellationToken ct)
    {
        var wgExe = WireGuardToolLocator.FindWgExe();
        if (wgExe is null)
        {
            return new UnprivilegedQueryResult(false, null, false);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = wgExe,
                Arguments = $"show {interfaceName} dump",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); } };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Ignorado.
                }

                return new UnprivilegedQueryResult(false, null, false);
            }

            var text = output.ToString().Trim();
            if (process.ExitCode == 0)
            {
                return new UnprivilegedQueryResult(true, text, false);
            }

            var needsElevation = text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                                 text.Contains("denegado", StringComparison.OrdinalIgnoreCase) ||
                                 text.Contains("elevat", StringComparison.OrdinalIgnoreCase) ||
                                 text.Contains("administrador", StringComparison.OrdinalIgnoreCase);
            return new UnprivilegedQueryResult(false,
                text.Length > 0 ? text : "wg show falló sin mensaje", needsElevation);
        }
        catch (Exception)
        {
            // Consulta no disponible: el llamador conserva el último estado conocido.
            return new UnprivilegedQueryResult(false, null, false);
        }
    }

    private sealed record UnprivilegedQueryResult(bool Ok, string? Output, bool NeedsElevation);

    /// <summary>
    /// Elimina un túnel huérfano (recuperación tras cierre inesperado) sin tocarlo como "activo".
    /// </summary>
    public async Task<(bool Ok, string? Error)> RemoveLeftoverAsync(string interfaceName, CancellationToken ct)
    {
        var op = PrivilegedOp.WithPayload(PrivilegedOpKind.UninstallWireGuardTunnel,
            new WireGuardTunnelPayload { TunnelName = interfaceName },
            reason: "recuperación: eliminar túnel huérfano");
        var result = await _ops.RunAsync(op, ct).ConfigureAwait(false);
        if (result.Ok)
        {
            RaiseStatus(TunnelProviderState.NotInstalled, interfaceName, "túnel huérfano eliminado");
        }

        return (result.Ok, result.Ok ? null : result.Error);
    }

    /// <summary>Intento best-effort de desinstalar el túnel recién creado tras un fallo parcial.</summary>
    private async Task<string> TryRemoveLeftoverAsync(string interfaceName)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var op = PrivilegedOp.WithPayload(PrivilegedOpKind.UninstallWireGuardTunnel,
                new WireGuardTunnelPayload { TunnelName = interfaceName },
                reason: "rollback tras fallo de activación");
            var result = await _ops.RunAsync(op, cts.Token).ConfigureAwait(false);
            return result.Ok
                ? " (Se revirtió la instalación parcial del túnel.)"
                : " (No se pudo revertir la instalación parcial: " + result.Error + ")";
        }
        catch (Exception ex)
        {
            return " (Rollback no disponible: " + ex.Message + ")";
        }
    }

    private static bool IsTransientOpError(string? error) =>
        error is not null &&
        (error.Contains("cancelad", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("no disponible", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("elevación", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("agente", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("IPC", StringComparison.OrdinalIgnoreCase));

    private static string? Truncate(string? text) =>
        text is { Length: > 400 } ? text[..400] : text;

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
