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
    /// <param name="warnings">
    /// Lista opcional donde se añaden avisos legibles (p. ej. DNS que no puede enrutarse).
    /// </param>
    public static WireGuardConfig? Build(
        RelayNode relay,
        RouteMode mode,
        IReadOnlyCollection<string>? destinationIps,
        int? mtu,
        string? dnsOverride,
        List<string>? warnings = null)
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

        // Protección básica contra fugas DNS: si el usuario fuerza un DNS, se intenta encaminar
        // también hacia el túnel en modo solo-juego (el modo global ya lo cubre con 0.0.0.0/0).
        var dnsServerIps = mode == RouteMode.TunnelGameDestinations
            ? TunnelDns.Ipv4Servers(dnsOverride)
            : new List<string>();

        var plan = RouteCalculator.BuildPlan(mode, Array.Empty<string>(), destinationIps ?? Array.Empty<string>(),
            tunnelInterfaceName: string.Empty, dnsServerIps: dnsServerIps);

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
        var relayCoversEverything = relayCidrs.Any(c => c is "0.0.0.0/0" or "::/0");

        if (mode == RouteMode.TunnelGameDestinations && !relayCoversEverything)
        {
            var uncovered = allowedIps.Where(ip => ip.EndsWith("/32") || ip.EndsWith("/128"))
                .Select(ip => ip[..ip.IndexOf('/')])
                .Where(ip => !RouteCalculator.Ipv4IsCovered(ip, relayCidrs))
                .ToList();
            if (uncovered.Count > 0)
            {
                // Separar servidores DNS (se omiten con aviso) de destinos del juego (error duro):
                // sin el relay no se puede enrutar el juego; el DNS solo pierde su protección.
                var uncoveredDns = dnsServerIps
                    .Where(dnsIp => !RouteCalculator.Ipv4IsCovered(dnsIp, relayCidrs))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var uncoveredGame = uncovered
                    .Where(ip => !uncoveredDns.Contains(ip))
                    .ToList();

                if (uncoveredDns.Count > 0)
                {
                    // Nunca retirar la ruta de un destino del juego que coincida con la IP del DNS.
                    var destinationSet = (destinationIps ?? Array.Empty<string>())
                        .Select(d => d.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var dnsIp in uncoveredDns)
                    {
                        if (!destinationSet.Contains(dnsIp))
                        {
                            allowedIps.Remove($"{dnsIp}/32");
                        }

                        warnings?.Add(
                            $"El servidor DNS «{dnsIp}» no está cubierto por el AllowedIPs del relay; " +
                            "no se encamina por el túnel y las consultas podrían salir por la ruta directa " +
                            "(fuga DNS). Usa un DNS alcanzable por el relay o activa el kill switch.");
                    }
                }

                if (uncoveredGame.Count > 0)
                {
                    throw new InvalidOperationException(
                        "El relay no declara en su AllowedIPs cubrir los destinos del juego " +
                        $"(p. ej. {string.Join(", ", uncoveredGame.Take(3))}). " +
                        "El servidor WireGuard debe enrutar esas IPs para que el túnel funcione.");
                }
            }
        }
        else if (dnsServerIps.Count > 0 && mode == RouteMode.TunnelGameDestinations)
        {
            warnings?.Add(
                "Los servidores DNS configurados se encaminarán por el túnel (protección contra fugas DNS).");
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
