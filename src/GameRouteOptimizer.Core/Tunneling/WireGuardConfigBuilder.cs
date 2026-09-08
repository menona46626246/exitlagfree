using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Routing;

namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>
/// Construye la configuración WireGuard para un relay y perfil de juego.
/// La clave privada debe estar disponible en memoria (cargada/cifrada por RelayManager).
/// </summary>
public static class WireGuardConfigBuilder
{
    /// <summary>
    /// Construye la configuración.
    /// </summary>
    /// <param name="relay">Relay con PrivateKeyPlain cargada.</param>
    /// <param name="mode">Direct no construye túnel (devuelve null).</param>
    /// <param name="destinationIps">IPs resueltas de los destinos del juego (modo solo-juego).</param>
    /// <param name="mtu">MTU (null = el del relay o 1420).</param>
    /// <param name="dnsOverride">DNS a forzar (opcional).</param>
    public static WireGuardConfig? Build(
        RelayNode relay,
        RouteMode mode,
        IReadOnlyCollection<string>? destinationIps,
        int? mtu,
        string? dnsOverride)
    {
        if (mode == RouteMode.Direct)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(relay.PrivateKeyPlain))
        {
            throw new InvalidOperationException(
                "El relay no tiene clave privada en memoria. Impórtala desde una configuración .conf " +
                "o introdúcela manualmente (se guarda cifrada con DPAPI).");
        }

        var plan = RouteCalculator.BuildPlan(mode, Array.Empty<string>(), destinationIps ?? Array.Empty<string>(),
            tunnelInterfaceName: string.Empty);

        var allowedIps = mode == RouteMode.TunnelGlobal
            ? new List<string> { "0.0.0.0/0", "::/0" }
            : plan.AllowedIps;

        if (mode == RouteMode.TunnelGameDestinations && allowedIps.Count == 0)
        {
            throw new InvalidOperationException(
                "No hay destinos del juego resueltos para enrutar por el túnel. Revisa el perfil.");
        }

        // Verificación honesta: ¿el relay declara cubrir esos destinos?
        var relayCidrs = relay.AllowedIps.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (mode == RouteMode.TunnelGameDestinations &&
            !relayCidrs.Any(c => c is "0.0.0.0/0" or "::/0" or "0.0.0.0/0, ::/0"))
        {
            var uncovered = allowedIps.Where(ip => ip.EndsWith("/32") || ip.EndsWith("/128"))
                .Select(ip => ip[..ip.IndexOf('/')])
                .Where(ip => !RouteCalculator.Ipv4IsCovered(ip, relayCidrs))
                .ToList();
            if (uncovered.Count > 0)
            {
                throw new InvalidOperationException(
                    "El relay no declara en su AllowedIPs cubrir los destinos del juego " +
                    $"(p. ej. {string.Join(", ", uncovered.Take(3))}). " +
                    "El servidor WireGuard debe enrutar esas IPs para que el túnel funcione.");
            }
        }

        var config = new WireGuardConfig
        {
            PrivateKey = relay.PrivateKeyPlain,
            InterfaceAddresses = relay.TunnelAddresses,
            Dns = dnsOverride ?? relay.DnsInternal,
            Mtu = mtu ?? relay.Mtu ?? 1420,
            Peers = new List<WireGuardPeer>
            {
                new()
                {
                    PublicKey = relay.PublicKey,
                    AllowedIps = allowedIps,
                    EndpointHost = relay.EndpointHost,
                    EndpointPort = relay.EndpointPort,
                    PersistentKeepalive = relay.PersistentKeepalive > 0 ? relay.PersistentKeepalive : null,
                },
            },
        };

        return config;
    }
}
