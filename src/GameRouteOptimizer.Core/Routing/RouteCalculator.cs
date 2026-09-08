using System.Net;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Privileged;

namespace GameRouteOptimizer.Core.Routing;

/// <summary>Resultado del cálculo de rutas para un túnel.</summary>
public sealed class RoutePlan
{
    /// <summary>AllowedIPs que debe declarar WireGuard para encaminar lo deseado.</summary>
    public List<string> AllowedIps { get; } = new();

    /// <summary>Rutas concretas a añadir (modo manual/avanzado, solo IPv4 en v1).</summary>
    public List<RoutePlanEntry> RoutesToAdd { get; } = new();

    /// <summary>Rutas que se eliminarán al detener (inversa exacta de RoutesToAdd).</summary>
    public List<RoutePlanEntry> RoutesToDeleteOnStop { get; } = new();

    /// <summary>Advertencias legibles (p. ej. destinos sin resolución).</summary>
    public List<string> Warnings { get; } = new();

    public bool NeedsTunnelRoutes => RoutesToAdd.Count > 0 || AllowedIps.Count > 0;
}

/// <summary>Prefijo CIDR con familia.</summary>
public sealed record Cidr(string Address, int Prefix);

/// <summary>
/// Calcula qué rutas/AllowedIPs necesita el túnel para un perfil concreto.
/// Lógica pura (sin red): las IPs de destino llegan ya resueltas o se marcan advertencias.
/// Modos:
///  - Global: 0.0.0.0/0 + ::/0 (todo por el túnel).
///  - Solo destinos del juego: /32 y /128 por cada destino autorizado.
/// </summary>
public static class RouteCalculator
{
    public static RoutePlan BuildPlan(
        RouteMode mode,
        IEnumerable<string> destinationHosts,
        IEnumerable<string> resolvedDestinationIps,
        string tunnelInterfaceName,
        IEnumerable<string>? dnsServerIps = null)
    {
        var plan = new RoutePlan();

        if (mode == RouteMode.TunnelGlobal)
        {
            plan.AllowedIps.Add("0.0.0.0/0");
            plan.AllowedIps.Add("::/0");
            plan.RoutesToAdd.Add(new RoutePlanEntry
            {
                Prefix = "0.0.0.0/1",
                InterfaceName = tunnelInterfaceName,
                Metric = 5,
            });
            plan.RoutesToAdd.Add(new RoutePlanEntry
            {
                Prefix = "128.0.0.0/1",
                InterfaceName = tunnelInterfaceName,
                Metric = 5,
            });
            foreach (var route in plan.RoutesToAdd)
            {
                plan.RoutesToDeleteOnStop.Add(new RoutePlanEntry
                {
                    Prefix = route.Prefix,
                    InterfaceName = route.InterfaceName,
                });
            }

            return plan;
        }

        // Modo solo destinos del juego.
        var hosts = destinationHosts.Select(h => h.Trim())
            .Where(h => h.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ips = resolvedDestinationIps.Select(ip => ip.Trim())
            .Where(ip => IPAddress.TryParse(ip, out _))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var host in hosts.Where(h => !IPAddress.TryParse(h, out _)))
        {
            plan.Warnings.Add($"El destino «{host}» no se pudo resolver a una IP; no se enrutará por el túnel.");
        }

        foreach (var ip in ips.OrderBy(i => i, StringComparer.Ordinal))
        {
            var address = IPAddress.Parse(ip);
            var prefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
            var cidr = $"{ip}/{prefix}";
            plan.AllowedIps.Add(cidr);
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                plan.RoutesToAdd.Add(new RoutePlanEntry { Prefix = cidr, InterfaceName = tunnelInterfaceName, Metric = 10 });
                plan.RoutesToDeleteOnStop.Add(new RoutePlanEntry { Prefix = cidr, InterfaceName = tunnelInterfaceName });
            }
        }

        // Servidores DNS a encaminar por el túnel (protección básica contra fugas DNS):
        // solo IPv4 literales que no estén ya cubiertos por los destinos.
        if (dnsServerIps is not null)
        {
            foreach (var entry in dnsServerIps.Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                if (!IPAddress.TryParse(entry, out var dnsAddress) ||
                    dnsAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    plan.Warnings.Add(
                        $"El servidor DNS «{entry}» no es una IP IPv4 válida; " +
                        "no se puede encaminar por el túnel en esta versión.");
                    continue;
                }

                var cidr = $"{entry}/32";
                if (plan.AllowedIps.Contains(cidr))
                {
                    continue;
                }

                plan.AllowedIps.Add(cidr);
                plan.RoutesToAdd.Add(new RoutePlanEntry { Prefix = cidr, InterfaceName = tunnelInterfaceName, Metric = 10 });
                plan.RoutesToDeleteOnStop.Add(new RoutePlanEntry { Prefix = cidr, InterfaceName = tunnelInterfaceName });
            }
        }

        if (plan.AllowedIps.Count == 0)
        {
            plan.Warnings.Add("No hay destinos resolubles para enrutar; el túnel no recibirá tráfico de juego.");
        }

        return plan;
    }

    /// <summary>Resuelve un conjunto de hosts a IPs (sin caché). Usado por el planificador en runtime.</summary>
    public static async Task<List<string>> ResolveDestinationsAsync(
        IEnumerable<string> hosts,
        CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var host in hosts.Select(h => h.Trim()).Where(h => h.Length > 0))
        {
            if (IPAddress.TryParse(host, out var fixedIp))
            {
                result.Add(fixedIp.ToString());
                continue;
            }

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
                foreach (var address in addresses.Where(a =>
                             a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    result.Add(address.ToString());
                }
            }
            catch (Exception)
            {
                // Sin resolución: se reporta como advertencia por BuildPlan.
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Comprueba si una IP destino está cubierta por los AllowedIPs declarados del relay (IPv4).</summary>
    public static bool Ipv4IsCovered(string ipString, IEnumerable<string> allowedIpCidrs)
    {
        if (!IPAddress.TryParse(ipString, out var ip) ||
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return true; // IPv6 no cubierto en v1: no se puede afirmar.
        }

        var value = ToUInt32(ip);
        foreach (var cidr in allowedIpCidrs)
        {
            var parts = cidr.Trim().Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var net) ||
                net.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                continue;
            }

            var prefix = int.Parse(parts[1]);
            if (prefix == 0)
            {
                return true;
            }

            var mask = prefix == 32 ? 0xFFFFFFFFu : (0xFFFFFFFFu << (32 - prefix));
            var network = ToUInt32(net) & mask;
            if ((value & mask) == network)
            {
                return true;
            }
        }

        return false;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}
