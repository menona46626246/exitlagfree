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
                PrivilegedOpKind.InstallWireGuardTunnel => await InstallWireGuardTunnelAsync(op, ct),
                PrivilegedOpKind.UninstallWireGuardTunnel => await UninstallWireGuardTunnelAsync(op, ct),
                PrivilegedOpKind.QueryWireGuardTunnel => await QueryWireGuardTunnelAsync(op, ct),
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

        await File.WriteAllTextAsync(confFile, payload.ConfigText, Encoding.UTF8, ct);

        // wireguard.exe /installtunnelservice <archivo.conf>
        var run = await RunProcessAsync(wgExe, $"/installtunnelservice \"{confFile}\"", ct);
        return run.ExitCode == 0
            ? PrivilegedOpResult.Success(run.Output, $"Túnel instalado desde {confFile}")
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

        var run = await RunProcessAsync(wgExe, "show all", ct);
        if (run.ExitCode != 0)
        {
            return PrivilegedOpResult.Failure(run.Output.Trim() is { Length: > 0 } o ? o : "wg show falló");
        }

        return PrivilegedOpResult.Success(run.Output);
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

    // ---------- kill switch (firewall por interfaz) ----------

    private async Task<PrivilegedOpResult> KillSwitchAsync(PrivilegedOp op, bool enable, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            return PrivilegedOpResult.Failure("El kill switch solo aplica en Windows.");
        }

        var payload = Deserialize<KillSwitchPayload>(op.PayloadJson);
        var allowed = new HashSet<string>(
            (payload?.AllowedInterfaceNames ?? new List<string>()).Concat(new[] { "Loopback" }),
            StringComparer.OrdinalIgnoreCase);

        const string rulePrefix = "GRO_KillSwitch";

        if (!enable)
        {
            var run = await RunProcessAsync("powershell",
                $"-NoProfile -Command \"Get-NetFirewallRule -DisplayName '{rulePrefix}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule\"",
                ct);
            return run.ExitCode == 0
                ? PrivilegedOpResult.Success(run.Output, "Kill switch desactivado: reglas de firewall eliminadas.")
                : PrivilegedOpResult.Failure(run.Output.Trim());
        }

        // Bloquea salida en cada interfaz física activa excepto las permitidas (túnel).
        var ifaceRun = await RunProcessAsync("powershell",
            "-NoProfile -Command \"Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Select-Object -ExpandProperty Name\"",
            ct);
        if (ifaceRun.ExitCode != 0)
        {
            return PrivilegedOpResult.Failure(ifaceRun.Output.Trim());
        }

        var errors = new List<string>();
        foreach (var iface in ifaceRun.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (allowed.Contains(iface))
            {
                continue;
            }

            var run = await RunProcessAsync("powershell",
                $"-NoProfile -Command \"New-NetFirewallRule -DisplayName '{rulePrefix}_{SanitizeForRule(iface)}' -Direction Outbound -InterfaceAlias '{iface.Replace("'", "''")}' -Action Block -Profile Any | Out-Null\"",
                ct);
            if (run.ExitCode != 0)
            {
                errors.Add(iface);
            }
        }

        return errors.Count == 0
            ? PrivilegedOpResult.Success(null, "Kill switch activado: sin salida fuera del túnel (salvo interfaces permitidas).")
            : PrivilegedOpResult.Failure($"No se pudo bloquear: {string.Join(", ", errors)}");
    }

    private static PrivilegedOpResult KillSwitchStatus()
    {
        // No hay API trivial de consulta sin elevación; el estado se mantiene en el gestor.
        return PrivilegedOpResult.Success("consulta disponible solo en el gestor local");
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

    private static string SanitizeForRule(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());

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
