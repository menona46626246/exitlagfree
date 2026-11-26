using System.Text.RegularExpressions;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>Excepción de validación de configuración WireGuard.</summary>
public sealed class WireGuardConfigException(string message) : Exception(message) { }

/// <summary>
/// Parser y validador de archivos .conf de WireGuard (formato INI de wg-quick).
/// Prevalida TODO antes de aplicar: claves base64 32 bytes, CIDR válidos, MTU en rango,
/// endpoint host:puerto. La clave privada se mantiene en el objeto pero nunca en logs
/// (ToConfString la enmascara).
/// </summary>
public static partial class WireGuardConfigParser
{
    [GeneratedRegex(@"^\s*\[(\w+)\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"^\s*([A-Za-z]+)\s*=\s*(.*?)\s*$")]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"^(?:\d{1,3}\.){3}\d{1,3}$")]
    private static partial Regex Ipv4Regex();

    /// <summary>Prefijos de CIDR válidos para IPv4.</summary>
    public static bool IsValidIpv4Cidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var parts = cidr.Split('/');
        if (parts.Length > 2)
        {
            return false;
        }

        if (!System.Net.IPAddress.TryParse(parts[0], out var ip))
        {
            return false;
        }

        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsValidEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var idx = endpoint.LastIndexOf(':');
        if (idx <= 0)
        {
            return false;
        }

        var host = endpoint[..idx];
        if (!int.TryParse(endpoint[(idx + 1)..], out var port) || port is < 1 or > 65535)
        {
            return false;
        }

        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            return true; // IPv6 literal
        }

        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    /// <summary>Parsea el contenido de un .conf. Lanza WireGuardConfigException ante errores estructurales.</summary>
    public static TunnelProfile Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new WireGuardConfigException("El archivo de configuración está vacío.");
        }

        var tunnel = new TunnelProfile();
        string? section = null;
        var sawInterface = false;
        var sawPeer = false;

        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            var secMatch = SectionRegex().Match(line);
            if (secMatch.Success)
            {
                section = secMatch.Groups[1].Value.ToLowerInvariant();
                switch (section)
                {
                    case "interface": sawInterface = true; break;
                    case "peer": sawPeer = true; break;
                    default:
                        throw new WireGuardConfigException($"Sección desconocida [{section}] en el archivo .conf.");
                }
                continue;
            }

            var kv = KeyValueRegex().Match(line);
            if (!kv.Success)
            {
                throw new WireGuardConfigException($"Línea no reconocida: \"{Truncate(line)}\".");
            }

            var key = kv.Groups[1].Value.ToLowerInvariant();
            var value = kv.Groups[2].Value;

            switch (section, key)
            {
                case ("interface", "privatekey"): tunnel.PrivateKey = value; break;
                case ("interface", "address"): tunnel.Address = value; break;
                case ("interface", "dns"): tunnel.Dns = value; break;
                case ("interface", "mtu"): tunnel.Mtu = ParseInt(key, value); break;
                case ("interface", "listenport"): tunnel.ListenPort = ParseInt(key, value); break;
                case ("peer", "publickey"): tunnel.PublicKey = value; break;
                case ("peer", "presharedkey"): tunnel.PresharedKey = value; break;
                case ("peer", "allowedips"): tunnel.AllowedIps = value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(); break;
                case ("peer", "endpoint"): tunnel.Endpoint = value; break;
                case ("peer", "persistentkeepalive"): tunnel.PersistentKeepalive = ParseInt(key, value); break;
                default:
                    throw new WireGuardConfigException($"Clave \"{key}\" no soportada en [{section}].");
            }
        }

        if (!sawInterface)
        {
            throw new WireGuardConfigException("Falta la sección [Interface].");
        }

        if (!sawPeer)
        {
            throw new WireGuardConfigException("Falta la sección [Peer].");
        }

        tunnel.Name = "tunnel-" + Math.Abs((tunnel.Endpoint + tunnel.PublicKey).GetHashCode());
        return tunnel;
    }

    /// <summary>Valida semánticamente el túnel; devuelve errores en español (vacío = válido).</summary>
    public static List<string> Validate(TunnelProfile tunnel)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(tunnel.PrivateKey))
        {
            errors.Add("Falta PrivateKey en [Interface].");
        }
        else if (!WireGuardKey.TryValidate(tunnel.PrivateKey, out var keyErr))
        {
            errors.Add("PrivateKey inválida: " + keyErr);
        }

        if (string.IsNullOrWhiteSpace(tunnel.Address))
        {
            errors.Add("Falta Address en [Interface] (IP del túnel, p. ej. 10.66.66.2/32).");
        }
        else
        {
            foreach (var addr in SplitList(tunnel.Address))
            {
                if (!IsValidIpv4Cidr(addr))
                {
                    errors.Add($"Address inválido: \"{addr}\".");
                }
            }
        }

        if (tunnel.Mtu is < 1200 or > 1500)
        {
            errors.Add($"MTU fuera de rango (1200-1500): {tunnel.Mtu}.");
        }

        if (!WireGuardKey.TryValidate(tunnel.PublicKey, out var pubErr))
        {
            errors.Add("PublicKey del peer inválida: " + pubErr);
        }

        if (tunnel.AllowedIps.Count == 0)
        {
            errors.Add("Falta AllowedIPs en [Peer] (prefijos que se enrutarán por el túnel).");
        }
        else
        {
            foreach (var cidr in tunnel.AllowedIps)
            {
                if (!IsValidIpv4Cidr(cidr))
                {
                    errors.Add($"AllowedIP inválido: \"{cidr}\".");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(tunnel.Endpoint))
        {
            errors.Add("Falta Endpoint en [Peer] (host:puerto del relay).");
        }
        else if (!IsValidEndpoint(tunnel.Endpoint))
        {
            errors.Add($"Endpoint inválido (se espera host:puerto): \"{tunnel.Endpoint}\".");
        }

        if (!string.IsNullOrWhiteSpace(tunnel.Dns))
        {
            foreach (var dns in SplitList(tunnel.Dns))
            {
                if (!Ipv4Regex().IsMatch(dns) && Uri.CheckHostName(dns) == UriHostNameType.Unknown)
                {
                    errors.Add($"DNS inválido: \"{dns}\".");
                }
            }
        }

        return errors;
    }

    /// <summary>Valida y lanza si hay errores (uso antes de aplicar el túnel).</summary>
    public static void EnsureValid(TunnelProfile tunnel)
    {
        var errors = Validate(tunnel);
        if (errors.Count > 0)
        {
            throw new WireGuardConfigException("Configuración inválida: " + string.Join(" ", errors));
        }
    }

    private static IEnumerable<string> SplitList(string value)
        => value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);

    private static int ParseInt(string key, string value)
        => int.TryParse(value, out var v)
            ? v
            : throw new WireGuardConfigException($"Valor numérico inválido para {key}: \"{value}\".");

    private static string Truncate(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
