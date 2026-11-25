using System.Diagnostics;
using System.Runtime.InteropServices;
using GameRouteOptimizer.Core.Platform;

namespace GameRouteOptimizer.Service.PrivilegedOperations;

/// <summary>
/// Operaciones privilegiadas reales sobre Windows mediante herramientas oficiales del
/// sistema (netsh, route). En Linux usa `ip route`. Todos los argumentos se pasan como
/// lista (sin shell) para evitar inyección. El servicio debe ejecutarse elevado.
/// </summary>
public static class PrivilegedOps
{
    /// <summary>¿Tenemos privilegios para tocar la red?</summary>
    public static bool HasPrivileges => PlatformInfo.IsElevated;

    // ---------- Rutas ----------

    /// <summary>Añade una ruta activa (no persistente) hacia un gateway/interfaz.</summary>
    public static async Task<bool> AddRouteAsync(string cidr, string? gateway, int? interfaceIndex, int metric, CancellationToken ct = default)
    {
        var (dest, mask) = CidrToDestMask(cidr);
        if (dest is null) return false;

        if (PlatformInfo.IsWindows)
        {
            var args = new List<string> { "interface", "ipv4", "add", "route",
                $"{dest}/{PrefixOf(cidr)}", "store=active" };
            if (!string.IsNullOrWhiteSpace(gateway)) args.Add($"nexthop={gateway}");
            if (interfaceIndex is int idx) args.Add($"interface={idx}");
            args.Add($"metric={metric}");
            var result = await CommandRunner.RunAsync("netsh", args, 15_000, ct);
            if (result.Success) return true;
            // Fallback clásico con `route add`.
            var routeArgs = new List<string> { "add", dest!, "mask", mask!, gateway ?? "0.0.0.0", "metric", metric.ToString() };
            if (interfaceIndex is int idx2) { routeArgs.Add("if"); routeArgs.Add(idx2.ToString()); }
            var r2 = await CommandRunner.RunAsync("route", routeArgs, 15_000, ct);
            LogOp($"route add {dest}", r2);
            return r2.Success;
        }
        else
        {
            var args = new List<string> { "route", "add", cidr };
            if (!string.IsNullOrWhiteSpace(gateway))
            {
                args.Add("via");
                args.Add(gateway);
            }
            var result = await CommandRunner.RunAsync("ip", args, 15_000, ct);
            LogOp($"ip route add {cidr}", result);
            return result.Success;
        }
    }

    /// <summary>Elimina una ruta activa.</summary>
    public static async Task<bool> DeleteRouteAsync(string cidr, string? gateway, int? interfaceIndex, CancellationToken ct = default)
    {
        var (dest, _) = CidrToDestMask(cidr);
        if (dest is null) return false;

        if (PlatformInfo.IsWindows)
        {
            var result = await CommandRunner.RunAsync("netsh", new[] { "interface", "ipv4", "delete", "route", $"{dest}/{PrefixOf(cidr)}", "store=active" }, 15_000, ct);
            if (result.Success) return true;
            var r2 = await CommandRunner.RunAsync("route", new[] { "delete", dest! }, 15_000, ct);
            LogOp($"route delete {dest}", r2);
            return r2.Success;
        }
        else
        {
            var result = await CommandRunner.RunAsync("ip", new[] { "route", "del", cidr }, 15_000, ct);
            LogOp($"ip route del {cidr}", result);
            return result.Success;
        }
    }

    /// <summary>Lee rutas IPv4 activas (parse de `route print -4` en Windows; `ip route` en Linux).</summary>
    public static async Task<List<(string Cidr, string? Gateway, int? IfIndex)>> ReadRoutesAsync(CancellationToken ct = default)
    {
        var routes = new List<(string, string?, int?)>();
        if (PlatformInfo.IsWindows)
        {
            var result = await CommandRunner.RunAsync("route", new[] { "print", "-4" }, 15_000, ct);
            foreach (var raw in result.StdOut.Split('\n'))
            {
                var line = raw.Trim();
                // Formato: Network Destination        Netmask          Gateway       Interface  Metric
                //          0.0.0.0          0.0.0.0      192.168.1.1    192.168.1.10     35
                var m = System.Text.RegularExpressions.Regex.Match(line,
                    @"^(\d{1,3}(?:\.\d{1,3}){3})\s+(\d{1,3}(?:\.\d{1,3}){3})\s+(\d{1,3}(?:\.\d{1,3}){3})\s+(\d{1,3}(?:\.\d{1,3}){3})\s+(\d+)");
                if (!m.Success) continue;
                var dest = m.Groups[1].Value;
                var mask = m.Groups[2].Value;
                var gateway = m.Groups[3].Value == "On-link" ? null : m.Groups[3].Value;
                var parsedIf = int.TryParse(m.Groups[5].Value, out var i) ? i : (int?)null;
                var prefix = MaskToPrefix(mask);
                routes.Add(($"{dest}/{prefix}", gateway, parsedIf));
            }
        }
        else
        {
            var result = await CommandRunner.RunAsync("ip", new[] { "-4", "route", "show" }, 15_000, ct);
            foreach (var line in result.StdOut.Split('\n'))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                var cidr = parts[0] switch
                {
                    "default" => "0.0.0.0/0",
                    _ => parts[0]
                };
                if (!cidr.Contains('/')) cidr += "/32";
                string? gateway = null;
                for (var i = 1; i < parts.Length - 1; i++)
                    if (parts[i] == "via") gateway = parts[i + 1];
                routes.Add((cidr, gateway, null));
            }
        }
        return routes;
    }

    // ---------- DNS ----------

    /// <summary>Establece DNS de un adaptador; devuelve los valores previos para restaurar.</summary>
    public static async Task<string[]?> SetDnsAsync(string interfaceName, string dns, CancellationToken ct = default)
    {
        if (!PlatformInfo.IsWindows) return null;
        var prev = await CommandRunner.RunAsync("netsh", new[] { "interface", "ipv4", "show", "dnsservers", $"name={interfaceName}" }, 15_000, ct);
        var setResult = await CommandRunner.RunAsync("netsh", new[] { "interface", "ipv4", "set", "dnsservers", $"name={interfaceName}", "source=static", $"address={dns}", "register=primary" }, 15_000, ct);
        return setResult.Success ? prev.StdOut.Split('\n') : null;
    }

    public static async Task FlushDnsAsync(CancellationToken ct = default)
    {
        if (PlatformInfo.IsWindows)
            await CommandRunner.RunAsync("ipconfig", new[] { "/flushdns" }, 15_000, ct);
    }

    // ---------- Firewall (kill switch) ----------

    public static async Task<bool> AddFirewallRuleAsync(string name, string? program, string direction, string action, CancellationToken ct = default)
    {
        if (!PlatformInfo.IsWindows) return false;
        var args = new List<string> { "advfirewall", "firewall", "add", "rule",
            $"name={name}", $"dir={direction}", $"action={action}", "enable=yes" };
        if (!string.IsNullOrWhiteSpace(program)) args.Add($"program={program}");
        var result = await CommandRunner.RunAsync("netsh", args, 20_000, ct);
        LogOp($"firewall add rule {name}", result);
        return result.Success;
    }

    public static async Task<bool> DeleteFirewallRuleAsync(string name, CancellationToken ct = default)
    {
        if (!PlatformInfo.IsWindows) return false;
        var result = await CommandRunner.RunAsync("netsh", new[] { "advfirewall", "firewall", "delete", "rule", $"name={name}" }, 20_000, ct);
        LogOp($"firewall delete rule {name}", result);
        return result.Success;
    }

    public static async Task<bool> FirewallRuleExistsAsync(string name, CancellationToken ct = default)
    {
        if (!PlatformInfo.IsWindows) return false;
        var result = await CommandRunner.RunAsync("netsh", new[] { "advfirewall", "firewall", "show", "rule", $"name={name}" }, 20_000, ct);
        return result.Success && result.StdOut.Contains(name, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Helpers ----------

    private static (string?, string?) CidrToDestMask(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length == 0 || !System.Net.IPAddress.TryParse(parts[0], out var ip)) return (null, null);
        var prefix = parts.Length == 2 && int.TryParse(parts[1], out var p) ? p : 32;
        var mask = PrefixToMask(prefix);
        return (ip.ToString(), mask);
    }

    private static int PrefixOf(string cidr)
    {
        var parts = cidr.Split('/');
        return parts.Length == 2 && int.TryParse(parts[1], out var p) ? p : 32;
    }

    private static string PrefixToMask(int prefix)
    {
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var take = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] = (byte)(take == 0 ? 0 : 0xFF << (8 - take) & 0xFF);
        }
        return string.Join('.', bytes);
    }

    private static int MaskToPrefix(string mask)
    {
        var count = 0;
        foreach (var b in mask.Split('.'))
            if (byte.TryParse(b, out var v))
                count += System.Numerics.BitOperations.PopCount(v);
        return count;
    }

    private static void LogOp(string op, CommandResult result)
    {
        if (result.Success)
            Core.Services.Log.Information($"Op privilegiada OK: {op}");
        else
            Core.Services.Log.Warning($"Op privilegiada falló: {op} → {result.StdErr.Trim()[..Math.Min(200, result.StdErr.Length)]}");
    }
}
