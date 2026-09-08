using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;

namespace GameRouteOptimizer.Core.Privileged;

/// <summary>
/// Ejecuta operaciones privilegiadas en la máquina local usando herramientas oficiales
/// (WireGuard para el túnel; netsh/route para rutas y DNS; firewall para kill switch).
/// Este código corre dentro del proceso elevado (CLI o servicio), nunca en la UI.
/// </summary>
public sealed class OpExecutor
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

    public async Task<PrivilegedOpResult> ExecuteAsync(PrivilegedOp op, CancellationToken ct)
    {
        try
        {
            return op.Kind switch
            {
                PrivilegedOpKind.Sequence => await ExecuteSequenceAsync(op, ct),
                PrivilegedOpKind.InstallWireGuardTunnel => await InstallWireGuardTunnelAsync(op, ct),
                PrivilegedOpKind.UninstallWireGuardTunnel => await UninstallWireGuardTunnelAsync(op, ct),
                PrivilegedOpKind.QueryWireGuardTunnel => await QueryWireGuardTunnelAsync(op, ct),
                PrivilegedOpKind.WaitWireGuardTunnel => await WaitWireGuardTunnelAsync(op, ct),
                PrivilegedOpKind.ApplyRoutes => await ApplyRoutesAsync(op, ct),
                PrivilegedOpKind.RestoreRoutes => await ApplyRoutesAsync(op, ct), // misma mecánica inversa
                PrivilegedOpKind.SetInterfaceDns => await SetDnsAsync(op, ct),
                PrivilegedOpKind.RestoreInterfaceDns => await SetDnsAsync(op, ct),
                PrivilegedOpKind.KillSwitchEnable => await KillSwitchAsync(op, enable: true, ct),
                PrivilegedOpKind.KillSwitchDisable => await KillSwitchAsync(op, enable: false, ct),
                PrivilegedOpKind.KillSwitchStatus => await Task.FromResult(KillSwitchStatus()),
                _ => PrivilegedOpResult.Failure($"Operación no soportada: {op.Kind}"),
            };
        }
        catch (OperationCanceledException)
        {
            return PrivilegedOpResult.Failure("Operación cancelada.");
        }
        catch (Exception ex)
        {
            return PrivilegedOpResult.Failure($"Error interno: {ex.Message}");
        }
    }

    /// <summary>
    /// Ejecuta una secuencia de operaciones dentro de la misma sesión elevada.
    /// Se detiene en el primer fallo; el resultado agrega el detalle de cada paso para auditoría.
    /// </summary>
    private async Task<PrivilegedOpResult> ExecuteSequenceAsync(PrivilegedOp op, CancellationToken ct)
    {
        var payload = Deserialize<SequencePayload>(op.PayloadJson);
        if (payload is null || payload.Steps.Count == 0)
        {
            return PrivilegedOpResult.Failure("La secuencia está vacía.");
        }

        var log = new List<string>();
        for (var i = 0; i < payload.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = payload.Steps[i];
            var result = await ExecuteSingleAsync(step, ct).ConfigureAwait(false);
            if (result.Ok)
            {
                log.Add($"paso {i + 1}/{payload.Steps.Count} ({step.Kind}): ok");
            }
            else
            {
                log.Add($"paso {i + 1}/{payload.Steps.Count} ({step.Kind}): FALLO — {result.Error}");
                return PrivilegedOpResult.Failure(
                    result.Error ?? $"Falló el paso {i + 1} de la secuencia.",
                    string.Join(" | ", log));
            }
        }

        return PrivilegedOpResult.Success(null, string.Join(" | ", log));
    }

    private Task<PrivilegedOpResult> ExecuteSingleAsync(PrivilegedOp op, CancellationToken ct)
    {
        return op.Kind switch
        {
            PrivilegedOpKind.InstallWireGuardTunnel => InstallWireGuardTunnelAsync(op, ct),
            PrivilegedOpKind.UninstallWireGuardTunnel => UninstallWireGuardTunnelAsync(op, ct),
            PrivilegedOpKind.QueryWireGuardTunnel => QueryWireGuardTunnelAsync(op, ct),
            PrivilegedOpKind.WaitWireGuardTunnel => WaitWireGuardTunnelAsync(op, ct),
            PrivilegedOpKind.ApplyRoutes => ApplyRoutesAsync(op, ct),
            PrivilegedOpKind.RestoreRoutes => ApplyRoutesAsync(op, ct),
            PrivilegedOpKind.SetInterfaceDns => SetDnsAsync(op, ct),
            PrivilegedOpKind.RestoreInterfaceDns => SetDnsAsync(op, ct),
            PrivilegedOpKind.KillSwitchEnable => KillSwitchAsync(op, enable: true, ct),
            PrivilegedOpKind.KillSwitchDisable => KillSwitchAsync(op, enable: false, ct),
            PrivilegedOpKind.KillSwitchStatus => Task.FromResult(KillSwitchStatus()),
            _ => Task.FromResult(PrivilegedOpResult.Failure($"Operación no soportada: {op.Kind}")),
        };
    }

    // ---------- WireGuard (herramientas oficiales) ----------

    private async Task<PrivilegedOpResult> InstallWireGuardTunnelAsync(PrivilegedOp op, CancellationToken ct)
    {
        var wgExe = WireGuardToolLocator.FindWireGuardExe();
        if (wgExe is null)
        {
            return PrivilegedOpResult.Failure(
                "No se encontró wireguard.exe. Instala WireGuard oficial (https://www.wireguard.com/install/).",
                "El modo túnel necesita la instalación oficial; el modo diagnóstico funciona sin ella.");
        }

        var payload = Deserialize<WireGuardTunnelPayload>(op.PayloadJson);
        if (string.IsNullOrWhiteSpace(payload?.ConfigText))
        {
            return PrivilegedOpResult.Failure("Falta el texto de configuración.");
        }

        var confDir = Path.Combine(Path.GetTempPath(), "GameRouteOptimizer");
        Directory.CreateDirectory(confDir);
        var confFile = Path.Combine(confDir,
            string.IsNullOrWhiteSpace(payload.TunnelName) ? "gro-tunnel" : payload.TunnelName);
        if (!confFile.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
        {
            confFile += ".conf";
        }

        ProcessResult run;
        try
        {
            // El .conf temporal contiene la clave privada en claro: se escribe solo el tiempo
            // imprescindible (wireguard.exe copia la configuración a su almacén cifrado con DPAPI)
            // y se elimina SIEMPRE al terminar, con éxito o sin él.
            await File.WriteAllTextAsync(confFile, payload.ConfigText, Encoding.UTF8, ct);

            // wireguard.exe /installtunnelservice <archivo.conf>
            run = await RunProcessAsync(wgExe, $"/installtunnelservice \"{confFile}\"", ct);
        }
        finally
        {
            try
            {
                if (File.Exists(confFile))
                {
                    File.Delete(confFile);
                }
            }
            catch (Exception)
            {
                // Limpieza best-effort; el archivo está en %TEMP% del propio usuario.
            }
        }

        return run.ExitCode == 0
            ? PrivilegedOpResult.Success(run.Output, "Túnel instalado; configuración temporal eliminada.")
            : PrivilegedOpResult.Failure(run.Output.Length > 0 ? run.Output.Trim() : "wireguard.exe falló sin mensaje");
    }

    private async Task<PrivilegedOpResult> UninstallWireGuardTunnelAsync(PrivilegedOp op, CancellationToken ct)
    {
        var wgExe = WireGuardToolLocator.FindWireGuardExe();
        if (wgExe is null)
        {
            return PrivilegedOpResult.Failure("No se encontró wireguard.exe.");
        }

        var payload = Deserialize<WireGuardTunnelPayload>(op.PayloadJson);
        var name = string.IsNullOrWhiteSpace(payload?.TunnelName) ? "gro-tunnel" : payload.TunnelName!;
        var run = await RunProcessAsync(wgExe, $"/uninstalltunnelservice {name}", ct);
        return run.ExitCode == 0
            ? PrivilegedOpResult.Success(run.Output, $"Túnel {name} eliminado; rutas restauradas por WireGuard.")
            : PrivilegedOpResult.Failure(run.Output.Length > 0 ? run.Output.Trim() : "No se pudo eliminar el túnel");
    }

    private async Task<PrivilegedOpResult> QueryWireGuardTunnelAsync(PrivilegedOp op, CancellationToken ct)
    {
        var wgExe = WireGuardToolLocator.FindWgExe();
        if (wgExe is null)
        {
            return PrivilegedOpResult.Failure("No se encontró wg.exe.");
        }

        var payload = Deserialize<WireGuardTunnelPayload>(op.PayloadJson);
        // Consulta específica (una interfaz) o general (todas).
        var name = string.IsNullOrWhiteSpace(payload?.TunnelName) ? null : payload.TunnelName!.Trim();
        var run = await RunProcessAsync(wgExe,
            name is null ? "show all" : $"show {name} dump", ct);
        if (run.ExitCode != 0)
        {
            return PrivilegedOpResult.Failure(run.Output.Trim() is { Length: > 0 } o ? o : "wg show falló");
        }

        return PrivilegedOpResult.Success(run.Output);
    }

    /// <summary>
    /// Espera (dentro de la misma sesión elevada) a que la interfaz del túnel esté operativa
    /// tras instalarla. Evita reintentar contra una interfaz que aún no existe.
    /// </summary>
    private async Task<PrivilegedOpResult> WaitWireGuardTunnelAsync(PrivilegedOp op, CancellationToken ct)
    {
        var wgExe = WireGuardToolLocator.FindWgExe();
        if (wgExe is null)
        {
            return PrivilegedOpResult.Failure("No se encontró wg.exe.");
        }

        var payload = Deserialize<WireGuardTunnelPayload>(op.PayloadJson);
        var name = string.IsNullOrWhiteSpace(payload?.TunnelName) ? "gro-tunnel" : payload.TunnelName!.Trim();
        var timeoutSeconds = payload?.TimeoutSeconds > 0 ? payload.TimeoutSeconds : 25;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        string? lastOutput = null;
        try
        {
            while (true)
            {
                var run = await RunProcessAsync(wgExe, $"show {name} dump", timeoutCts.Token).ConfigureAwait(false);
                if (run.ExitCode == 0)
                {
                    return PrivilegedOpResult.Success(run.Output,
                        $"Interfaz {name} operativa tras instalarla.");
                }

                lastOutput = run.Output.Trim();
                await Task.Delay(700, timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PrivilegedOpResult.Failure(
                $"La interfaz {name} no quedó operativa en {timeoutSeconds} s tras instalarla." +
                (lastOutput is { Length: > 0 } ? " Último error: " + lastOutput : string.Empty));
        }
    }

    // ---------- rutas (netsh) ----------

    private async Task<PrivilegedOpResult> ApplyRoutesAsync(PrivilegedOp op, CancellationToken ct)
    {
        var payload = Deserialize<RoutesPayload>(op.PayloadJson);
        if (payload is null || payload.Entries.Count == 0)
        {
            return PrivilegedOpResult.Failure("La lista de rutas está vacía.");
        }

        var errors = new List<string>();
        var done = new List<string>();
        foreach (var entry in payload.Entries)
        {
            var parts = entry.Prefix.Split('/');
            if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip))
            {
                errors.Add($"Prefijo inválido: {entry.Prefix}");
                continue;
            }

            var mask = CidrToMask(ip.AddressFamily, int.Parse(parts[1]));
            var args = new StringBuilder();
            if (op.Kind == PrivilegedOpKind.ApplyRoutes)
            {
                args.Append("interface ipv4 add route prefix=").Append(entry.Prefix)
                    .Append(" interface=\"").Append(entry.InterfaceName).Append('"');
                if (!string.IsNullOrWhiteSpace(entry.Gateway))
                {
                    args.Append(" nexthop=").Append(entry.Gateway);
                }

                args.Append(" metric=").Append(entry.Metric).Append(" store=active");
            }
            else
            {
                args.Append("interface ipv4 delete route prefix=").Append(entry.Prefix)
                    .Append(" interface=\"").Append(entry.InterfaceName).Append('"');
            }

            var run = await RunProcessAsync("netsh", args.ToString(), ct);
            if (run.ExitCode != 0)
            {
                errors.Add($"Ruta {entry.Prefix}: {run.Output.Trim()}");
            }
            else
            {
                done.Add(entry.Prefix);
            }
        }

        return errors.Count == 0
            ? PrivilegedOpResult.Success($"Rutas aplicadas: {string.Join(", ", done)}")
            : PrivilegedOpResult.Failure(string.Join(" | ", errors.Take(5)), "Rutas aplicadas: " + string.Join(", ", done));
    }

    // ---------- DNS (netsh) ----------

    private async Task<PrivilegedOpResult> SetDnsAsync(PrivilegedOp op, CancellationToken ct)
    {
        var payload = Deserialize<DnsPayload>(op.PayloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.InterfaceName))
        {
            return PrivilegedOpResult.Failure("Falta el nombre de interfaz.");
        }

        var args = new StringBuilder();
        if (op.Kind == PrivilegedOpKind.SetInterfaceDns && payload.Servers.Count > 0)
        {
            args.Append("interface ipv4 set dnsservers name=\"").Append(payload.InterfaceName)
                .Append("\" static ").Append(string.Join(" ", payload.Servers)).Append(" validate=no");
        }
        else
        {
            args.Append("interface ipv4 set dnsservers name=\"").Append(payload.InterfaceName)
                .Append("\" dhcp");
        }

        var run = await RunProcessAsync("netsh", args.ToString(), ct);
        return run.ExitCode == 0
            ? PrivilegedOpResult.Success(run.Output, op.Kind == PrivilegedOpKind.SetInterfaceDns ? "DNS estático aplicado." : "DNS restaurado a DHCP.")
            : PrivilegedOpResult.Failure(run.Output.Trim());
    }

    // ---------- kill switch (firewall: bloquear salida salvo tunel + endpoint) ----------
    //
    // Diseno: se cambia la accion de salida por defecto de los perfiles de firewall a
    // «Bloquear» y se crean reglas de permitido explicitas para (1) la interfaz del tunel,
    // (2) el endpoint UDP del relay (el handshake de WireGuard sale por la interfaz fisica)
    // y (3) la subred local (LocalSubnet). Esto protege sin romper el tunel, a diferencia
    // de bloquear por interfaz fisica (que tambien cortaria el handshake con el relay).
    // El estado previo de cada perfil se guarda en %ProgramData% y se restaura al desactivar.

    private static string KillSwitchStatePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GameRouteOptimizer");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "killswitch-state.json");
    }

    private async Task<PrivilegedOpResult> KillSwitchAsync(PrivilegedOp op, bool enable, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            return PrivilegedOpResult.Failure("El kill switch solo aplica en Windows.");
        }

        const string rulePrefix = "GRO_KillSwitch";
        var statePath = KillSwitchStatePath();

        if (!enable)
        {
            var statePathPs = statePath.Replace("'", "''");
            var script =
                "$ErrorActionPreference = 'Stop'\n" +
                $"$prefix = '{rulePrefix}'\n" +
                $"$statePath = '{statePathPs}'\n" +
                "Get-NetFirewallRule -DisplayName ($prefix + '_*') -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue\n" +
                "if (Test-Path -LiteralPath $statePath) {\n" +
                "  $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json\n" +
                "  foreach ($p in 'Domain','Private','Public') {\n" +
                "    $v = $saved.$p\n" +
                "    if ($v) { Set-NetFirewallProfile -Name $p -DefaultOutboundAction ([string]$v) -ErrorAction SilentlyContinue }\n" +
                "  }\n" +
                "  Remove-Item -LiteralPath $statePath -Force\n" +
                "}\n" +
                "Write-Output 'GRO kill switch desactivado: reglas eliminadas y acciones de salida restauradas.'";
            var run = await RunPowershellAsync(script, ct).ConfigureAwait(false);
            return run.ExitCode == 0
                ? PrivilegedOpResult.Success(run.Output, "Kill switch desactivado: reglas eliminadas y firewall restaurado.")
                : PrivilegedOpResult.Failure(run.Output.Trim());
        }

        var payload = Deserialize<KillSwitchPayload>(op.PayloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.TunnelInterfaceName))
        {
            return PrivilegedOpResult.Failure("Falta la interfaz del tunel para activar el kill switch.");
        }

        var endpointIps = (payload.EndpointIps ?? new List<string>())
            .Where(ip => System.Net.IPAddress.TryParse(ip, out var parsed) &&
                         parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (endpointIps.Count == 0 || payload.EndpointPort is < 1 or > 65535)
        {
            return PrivilegedOpResult.Failure(
                "Falta el endpoint UDP del relay (IP v4 y puerto) para permitir el handshake " +
                "de WireGuard; el kill switch no se activa para no cortar el tunel.");
        }

        var tunnelName = payload.TunnelInterfaceName!.Replace("'", "''");
        var statePathPs = statePath.Replace("'", "''");
        var ipsArg = string.Join(",", endpointIps.Select(ip => $"'{ip}'"));
        var script =
            "$ErrorActionPreference = 'Stop'\n" +
            $"$statePath = '{statePathPs}'\n" +
            "function Restore-GroKillSwitch {\n" +
            "  Get-NetFirewallRule -DisplayName 'GRO_KillSwitch_*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue\n" +
            "  if (Test-Path -LiteralPath $statePath) {\n" +
            "    $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json\n" +
            "    foreach ($p in 'Domain','Private','Public') {\n" +
            "      $v = $saved.$p\n" +
            "      if ($v) { Set-NetFirewallProfile -Name $p -DefaultOutboundAction ([string]$v) -ErrorAction SilentlyContinue }\n" +
            "    }\n" +
            "    Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue\n" +
            "  }\n" +
            "}\n" +
            "try {\n" +
            "  if (-not (Test-Path -LiteralPath $statePath)) {\n" +
            "    $saved = @{}\n" +
            "    foreach ($p in 'Domain','Private','Public') {\n" +
            "      $cur = Get-NetFirewallProfile -Name $p\n" +
            "      $saved[$p] = [string]$cur.DefaultOutboundAction\n" +
            "    }\n" +
            "    ($saved | ConvertTo-Json -Compress) | Set-Content -LiteralPath $statePath -Encoding UTF8\n" +
            "  }\n" +
            "  foreach ($p in 'Domain','Private','Public') { Set-NetFirewallProfile -Name $p -DefaultOutboundAction Block }\n" +
            $"  New-NetFirewallRule -DisplayName 'GRO_KillSwitch_AllowTunnel' -Direction Outbound -InterfaceAlias '{tunnelName}' -Action Allow -Profile Any | Out-Null\n" +
            $"  New-NetFirewallRule -DisplayName 'GRO_KillSwitch_AllowEndpoint' -Direction Outbound -Protocol UDP -RemoteAddress {ipsArg} -RemotePort {payload.EndpointPort} -Action Allow -Profile Any | Out-Null\n" +
            "  New-NetFirewallRule -DisplayName 'GRO_KillSwitch_AllowLan' -Direction Outbound -RemoteAddress LocalSubnet -Action Allow -Profile Any | Out-Null\n" +
            "  Write-Output 'GRO kill switch activado: salida bloqueada salvo tunel, endpoint y LAN.'\n" +
            "}\n" +
            "catch {\n" +
            "  Restore-GroKillSwitch\n" +
            "  Write-Output ('GRO kill switch fallo y se revirtio: ' + $_.Exception.Message)\n" +
            "  exit 1\n" +
            "}";

        var run2 = await RunPowershellAsync(script, ct).ConfigureAwait(false);
        return run2.ExitCode == 0
            ? PrivilegedOpResult.Success(run2.Output, "Kill switch activado: salida bloqueada salvo tunel, endpoint y LAN.")
            : PrivilegedOpResult.Failure(run2.Output.Trim());
    }

    private static PrivilegedOpResult KillSwitchStatus()
    {
        // No hay API trivial de consulta sin elevacion; el estado se mantiene en el gestor.
        return PrivilegedOpResult.Success("consulta disponible solo en el gestor local");
    }

    /// <summary>Ejecuta un script PowerShell sin ventana; salida combinada de stdout/stderr.</summary>
    private async Task<ProcessResult> RunPowershellAsync(string script, CancellationToken ct)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return await RunProcessAsync("powershell",
            "-NoProfile -NonInteractive -EncodedCommand " + encoded, ct).ConfigureAwait(false);
    }

    // ---------- utilidades ----------

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string CidrToMask(System.Net.Sockets.AddressFamily family, int prefix)
    {
        if (family == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return string.Empty; // IPv6 se gestiona vía ruta por prefijo (netsh v6) — v1 se centra en IPv4.
        }

        var mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        return new System.Net.IPAddress(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(unchecked((int)mask))))
            .ToString();
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProcessTimeout);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };

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
                // El proceso pudo terminar solo.
            }

            return new ProcessResult(-1, "Timeout ejecutando: " + fileName);
        }

        return new ProcessResult(process.ExitCode, (stdout + stderr.ToString()).Trim());
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
