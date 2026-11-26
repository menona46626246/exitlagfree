using System.Net;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Routing;

/// <summary>Una entrada de ruta (prefijo CIDR → gateway/interfaz).</summary>
public sealed record RouteEntry(string DestinationCidr, string? Gateway, int? InterfaceIndex, string? InterfaceName, int Metric = 0);

/// <summary>Plan de rutas: qué se añade, qué se restaura y datos del túnel.</summary>
public sealed class RoutePlan
{
    /// <summary>Prefijos que deben enrutar por el túnel (destinos del juego).</summary>
    public List<string> AddRoutes { get; set; } = new();

    /// <summary>Estado original capturado para esos prefijos (para restaurar).</summary>
    public List<RouteEntry> OriginalRoutes { get; set; } = new();

    /// <summary>IP del túnel (gateway para las nuevas rutas).</summary>
    public string? TunnelAddress { get; set; }

    /// <summary>Índice de la interfaz del túnel.</summary>
    public int? TunnelInterfaceIndex { get; set; }

    public string TunnelName { get; set; } = string.Empty;

    public int Mtu { get; set; } = 1380;

    public string Dns { get; set; } = string.Empty;

    public bool GlobalTunnel { get; set; }
}

/// <summary>Lee y aplica rutas del sistema (implementado en el Servicio; simulado en tests).</summary>
public interface IRouteApplier
{
    /// <summary>Lee las rutas activas actuales (IPv4).</summary>
    Task<List<RouteEntry>> GetCurrentRoutesAsync(CancellationToken ct = default);

    /// <summary>Añade una ruta activa (no persistente).</summary>
    Task<bool> AddRouteAsync(RouteEntry entry, CancellationToken ct = default);

    /// <summary>Elimina una ruta activa.</summary>
    Task<bool> DeleteRouteAsync(RouteEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Calcula planes de rutas para un perfil: modo directo (nada), túnel solo-juego
/// (prefijos de servidores) o global (túnel con 0.0.0.0/0). Captura el estado original
/// de rutas para poder restaurarlo. Operaciones de red puramente calculadas aquí;
/// la aplicación se delega en IRouteApplier (servicio elevado).
/// </summary>
public sealed class RouteCalculator
{
    private readonly IRouteApplier _applier;

    public RouteCalculator(IRouteApplier applier) => _applier = applier;

    /// <summary>
    /// Construye el plan de rutas para los destinos del juego según el modo de ruta.
    /// Captura las rutas originales de los prefijos afectados.
    /// </summary>
    public async Task<RoutePlan> BuildPlanAsync(GameProfile profile, TunnelProfile tunnel,
        RoutingMode mode, CancellationToken ct = default)
    {
        var current = await _applier.GetCurrentRoutesAsync(ct);
        var plan = new RoutePlan
        {
            TunnelAddress = ExtractTunnelAddress(tunnel.Address),
            TunnelName = tunnel.Name,
            Mtu = tunnel.Mtu,
            Dns = tunnel.Dns,
            GlobalTunnel = mode == RoutingMode.TunnelGlobal
        };

        if (mode == RoutingMode.Direct)
        {
            return plan;
        }

        // Prefijos objetivo.
        var prefixes = new List<string>();
        if (mode == RoutingMode.TunnelGlobal)
        {
            // Global: el túnel ya enruta 0.0.0.0/0 vía su propio AllowedIPs.
            prefixes.AddRange(tunnel.AllowedIps);
        }
        else
        {
            // Solo juego: prefijos de los servidores del perfil + AllowedIPs relevantes del relay.
            foreach (var server in profile.Servers)
            {
                var ip = !string.IsNullOrWhiteSpace(server.ResolvedIp) ? server.ResolvedIp : server.Ip;
                if (System.Net.IPAddress.TryParse(ip, out var parsed))
                {
                    prefixes.Add($"{parsed}/32");
                }
            }
        }

        // Deduplicar prefijos.
        plan.AddRoutes = prefixes.Distinct().ToList();

        // Capturar rutas originales para los prefijos que ya tienen entrada.
        foreach (var prefix in plan.AddRoutes)
        {
            var originals = current.Where(r => Covers(r.DestinationCidr, prefix)).ToList();
            plan.OriginalRoutes.AddRange(originals);
        }

        return plan;
    }

    /// <summary>Aplica el plan: añade rutas por el túnel. Devuelve nº de rutas aplicadas.</summary>
    public async Task<int> ApplyPlanAsync(RoutePlan plan, CancellationToken ct = default)
    {
        var applied = 0;
        foreach (var prefix in plan.AddRoutes)
        {
            var entry = new RouteEntry(prefix, plan.TunnelAddress, plan.TunnelInterfaceIndex, plan.TunnelName, Metric: 1);
            if (await _applier.AddRouteAsync(entry, ct))
            {
                applied++;
            }
        }
        return applied;
    }

    /// <summary>
    /// Restaura el estado original: elimina las rutas añadidas y re-agrega las originales
    /// capturadas. Seguro de llamar múltiples veces.
    /// </summary>
    public async Task<int> RestorePlanAsync(RoutePlan plan, CancellationToken ct = default)
    {
        var restored = 0;
        foreach (var prefix in plan.AddRoutes)
        {
            var added = new RouteEntry(prefix, plan.TunnelAddress, plan.TunnelInterfaceIndex, plan.TunnelName, Metric: 1);
            await _applier.DeleteRouteAsync(added, ct);
        }
        foreach (var original in plan.OriginalRoutes)
        {
            if (await _applier.AddRouteAsync(original, ct))
            {
                restored++;
            }
        }
        return restored;
    }

    /// <summary>Extrae la primera IP (sin prefijo) de "10.66.66.2/32, fd42::2/128".</summary>
    public static string? ExtractTunnelAddress(string addressField)
    {
        foreach (var part in (addressField ?? string.Empty).Split(','))
        {
            var trimmed = part.Trim();
            var idx = trimmed.IndexOf('/');
            var ipText = idx > 0 ? trimmed[..idx] : trimmed;
            if (IPAddress.TryParse(ipText, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return ipText;
            }
        }
        return null;
    }

    /// <summary>¿La ruta `routeCidr` cubre el prefijo `target`? (misma dirección o superred).</summary>
    public static bool Covers(string routeCidr, string target)
    {
        if (routeCidr == target)
        {
            return true;
        }

        if (routeCidr == "0.0.0.0/0")
        {
            return true;
        }

        try
        {
            var routeParts = routeCidr.Split('/');
            var targetParts = target.Split('/');
            if (routeParts.Length != 2 || targetParts.Length != 2)
            {
                return false;
            }

            if (!IPAddress.TryParse(routeParts[0], out var routeIp) || !IPAddress.TryParse(targetParts[0], out var targetIp))
            {
                return false;
            }

            if (routeIp.AddressFamily != targetIp.AddressFamily)
            {
                return false;
            }

            var routePrefix = int.Parse(routeParts[1]);
            var targetPrefix = int.Parse(targetParts[1]);
            if (routePrefix > targetPrefix)
            {
                return false;
            }
            // target está dentro de routeCidr si los primeros routePrefix bits coinciden.
            var routeBytes = routeIp.GetAddressBytes();
            var targetBytes = targetIp.GetAddressBytes();
            var fullBytes = routePrefix / 8;
            for (var i = 0; i < fullBytes && i < routeBytes.Length; i++)
            {
                if (routeBytes[i] != targetBytes[i])
                {
                    return false;
                }
            }

            var remBits = routePrefix % 8;
            if (remBits > 0 && fullBytes < routeBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - remBits));
                if ((routeBytes[fullBytes] & mask) != (targetBytes[fullBytes] & mask))
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
