using System.Net;
using System.Net.Sockets;

namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>
/// Utilidades para el DNS del túnel WireGuard en Windows.
/// v1 trabaja con servidores DNS IPv4 literales (lo que acepta `netsh`).
/// </summary>
public static class TunnelDns
{
    /// <summary>
    /// Extrae los servidores DNS IPv4 válidos de un texto (p. ej. "1.1.1.1, 9.9.9.9").
    /// Devuelve lista vacía si no hay ninguno utilizable.
    /// </summary>
    public static List<string> Ipv4Servers(string? dnsText)
    {
        if (string.IsNullOrWhiteSpace(dnsText))
        {
            return new List<string>();
        }

        return dnsText.Split(',', ';', ' ')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Where(s => IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Indica si el texto DNS contiene entradas que no son IPv4 (nombres o IPv6):
    /// se usan para avisar de que no se pueden aplicar como DNS de interfaz en v1.
    /// </summary>
    public static bool HasNonIpv4Entries(string? dnsText)
    {
        if (string.IsNullOrWhiteSpace(dnsText))
        {
            return false;
        }

        return dnsText.Split(',', ';', ' ')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Any(s => !IPAddress.TryParse(s, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork);
    }
}
