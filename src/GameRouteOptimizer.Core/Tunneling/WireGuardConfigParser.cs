using System.Text;
using System.Text.RegularExpressions;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>
/// Parser y validador de configuración WireGuard (formato de texto .conf).
/// Sin dependencias externas: solo sintaxis/validación estructural + claves base64.
/// </summary>
public static partial class WireGuardConfigParser
{
    // WireGuard base64: 44 caracteres terminados en '=' (32 bytes).
    public const int KeyLength = 44;

    [GeneratedRegex("^[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"^(\d{1,3}(\.\d{1,3}){3})[/ ](\d{1,2})$")]
    private static partial Regex Ipv4CidrRegex();

    [GeneratedRegex(@"^[0-9a-fA-F:]+(::[0-9a-fA-F:]*)?[/ ]\d{1,3}$")]
    private static partial Regex Ipv6CidrRegex();

    public static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        key.Length == KeyLength &&
        KeyRegex().IsMatch(key);

    /// <summary>Parsea texto de configuración WireGuard.</summary>
    public static WireGuardParseResult Parse(string text)
    {
        var result = new WireGuardParseResult();
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Errors.Add("La configuración está vacía.");
            return result;
        }

        var config = new WireGuardConfig();
        string section = string.Empty;
        var lines = text.Replace("\r\n", "\n").Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                result.Errors.Add($"Línea sin 'clave=valor': «{Truncate(line)}»");
                continue;
            }

            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();

            switch (section)
            {
                case "interface":
                    ApplyInterfaceKey(config, key, value, result);
                    break;
                case "peer":
                    EnsurePeer(config);
                    ApplyPeerKey(config.Peers[^1], key, value, result);
                    break;
                default:
                    result.Errors.Add($"Sección desconocida o falta [Interface]/[Peer]: «{Truncate(line)}»");
                    break;
            }
        }

        if (config.Peers.Count == 0)
        {
            result.Errors.Add("La configuración no tiene ninguna sección [Peer].");
        }

        Validate(config, result);

        if (result.Errors.Count == 0)
        {
            result.Ok = true;
            result.Config = config;
        }

        return result;
    }

    /// <summary>Validación estructural de una configuración ya construida.</summary>
    public static void Validate(WireGuardConfig config, WireGuardParseResult result)
    {
        if (string.IsNullOrWhiteSpace(config.PrivateKey))
        {
            result.Errors.Add("Falta PrivateKey en [Interface].");
        }
        else if (!IsValidKey(config.PrivateKey))
        {
            result.Errors.Add("PrivateKey no es una clave WireGuard base64 válida (44 caracteres).");
        }

        if (config.Peers.Count == 0)
        {
            result.Errors.Add("No hay ningún [Peer].");
            return;
        }

        foreach (var peer in config.Peers)
        {
            if (!IsValidKey(peer.PublicKey))
            {
                result.Errors.Add("PublicKey de un [Peer] no es válida (44 caracteres base64).");
            }

            if (peer.AllowedIps.Count == 0)
            {
                result.Errors.Add("Un [Peer] no tiene AllowedIPs.");
            }

            foreach (var cidr in peer.AllowedIps)
            {
                if (!IsValidCidr(cidr))
                {
                    result.Errors.Add($"AllowedIPs inválida: «{cidr}»");
                }
            }

            if (string.IsNullOrWhiteSpace(peer.EndpointHost))
            {
                result.Warnings.Add("Un [Peer] no tiene Endpoint; no se podrá conectar.");
            }
            else
            {
                ValidateEndpointHost(peer.EndpointHost, result);
                if (peer.EndpointPort is < 1 or > 65535)
                {
                    result.Errors.Add($"Puerto de Endpoint inválido: {peer.EndpointPort}");
                }
            }

            if (peer.PersistentKeepalive is < 0 or > 65535)
            {
                result.Errors.Add("PersistentKeepalive fuera de rango.");
            }
        }

        if (config.Mtu is < 576 or > 65535)
        {
            result.Errors.Add($"MTU fuera de rango: {config.Mtu}");
        }
    }

    public static bool IsValidCidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        cidr = cidr.Trim();
        if (cidr.Contains(':'))
        {
            return Ipv6CidrRegex().IsMatch(cidr);
        }

        return Ipv4CidrRegex().IsMatch(cidr);
    }

    /// <summary>Comprueba que un host de endpoint sea IP o dominio razonable.</summary>
    public static bool IsValidEndpointHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return true;
        }

        // Dominio simple: etiquetas alfanuméricas separadas por puntos.
        return host.Length is >= 1 and <= 253 &&
               host.Split('.').All(l =>
                   l.Length > 0 &&
                   l.All(char.IsLetterOrDigit) ||
                   (l.Length > 0 && l.All(c => char.IsLetterOrDigit(c) || c == '-') &&
                    !l.StartsWith('-') && !l.EndsWith('-')));
    }

    /// <summary>Genera el texto .conf a partir del modelo.</summary>
    public static string ToConfText(WireGuardConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        if (config.InterfaceAddresses.Count > 0)
        {
            sb.AppendLine($"Address = {string.Join(", ", config.InterfaceAddresses)}");
        }

        if (config.ListenPort.HasValue)
        {
            sb.AppendLine($"ListenPort = {config.ListenPort}");
        }

        if (!string.IsNullOrWhiteSpace(config.PrivateKey))
        {
            sb.AppendLine($"PrivateKey = {config.PrivateKey}");
        }

        if (!string.IsNullOrWhiteSpace(config.Dns))
        {
            sb.AppendLine($"DNS = {config.Dns}");
        }

        if (config.Mtu.HasValue)
        {
            sb.AppendLine($"MTU = {config.Mtu}");
        }

        foreach (var peer in config.Peers)
        {
            sb.AppendLine();
            sb.AppendLine("[Peer]");
            sb.AppendLine($"PublicKey = {peer.PublicKey}");
            if (!string.IsNullOrWhiteSpace(peer.PresharedKey))
            {
                sb.AppendLine($"PresharedKey = {peer.PresharedKey}");
            }

            sb.AppendLine($"AllowedIPs = {string.Join(", ", peer.AllowedIps)}");
            if (!string.IsNullOrWhiteSpace(peer.EndpointHost))
            {
                sb.AppendLine($"Endpoint = {peer.EndpointHost}:{peer.EndpointPort}");
            }

            if (peer.PersistentKeepalive is > 0)
            {
                sb.AppendLine($"PersistentKeepalive = {peer.PersistentKeepalive}");
            }
        }

        return sb.ToString();
    }

    private static void ApplyInterfaceKey(WireGuardConfig config, string key, string value, WireGuardParseResult result)
    {
        switch (key)
        {
            case "address":
                config.InterfaceAddresses.AddRange(SplitList(value));
                break;
            case "listenport":
                if (int.TryParse(value, out var port) && port is > 0 and <= 65535)
                {
                    config.ListenPort = port;
                }
                else
                {
                    result.Errors.Add($"ListenPort inválido: «{value}»");
                }

                break;
            case "privatekey":
                config.PrivateKey = value;
                break;
            case "dns":
                config.Dns = value;
                break;
            case "mtu":
                if (int.TryParse(value, out var mtu))
                {
                    config.Mtu = mtu;
                }
                else
                {
                    result.Errors.Add($"MTU inválido: «{value}»");
                }

                break;
            case "table":
            case "postup":
            case "postdown":
                // Claves de wg-quick no aplicables en Windows vía servicio oficial; se ignoran con aviso.
                result.Warnings.Add($"Clave «{key}» de wg-quick ignorada.");
                break;
            default:
                result.Warnings.Add($"Clave desconocida en [Interface]: «{key}»");
                break;
        }
    }

    private static void ApplyPeerKey(WireGuardPeer peer, string key, string value, WireGuardParseResult result)
    {
        switch (key)
        {
            case "publickey":
                peer.PublicKey = value;
                break;
            case "presharedkey":
                peer.PresharedKey = value;
                break;
            case "allowedips":
                peer.AllowedIps.AddRange(SplitList(value));
                break;
            case "endpoint":
                var idx = value.LastIndexOf(':');
                if (idx > 0 && int.TryParse(value[(idx + 1)..], out var port))
                {
                    peer.EndpointHost = value[..idx].Trim('[', ']', ' ');
                    peer.EndpointPort = port;
                }
                else
                {
                    result.Errors.Add($"Endpoint mal formado: «{value}» (se espera host:puerto)");
                }

                break;
            case "persistentkeepalive":
                if (int.TryParse(value, out var keepalive))
                {
                    peer.PersistentKeepalive = keepalive;
                }
                else
                {
                    result.Errors.Add($"PersistentKeepalive inválido: «{value}»");
                }

                break;
            default:
                result.Warnings.Add($"Clave desconocida en [Peer]: «{key}»");
                break;
        }
    }

    private static void EnsurePeer(WireGuardConfig config)
    {
        if (config.Peers.Count == 0 || config.Peers[^1].PublicKey.Length > 0)
        {
            config.Peers.Add(new WireGuardPeer());
        }
    }

    private static void ValidateEndpointHost(string host, WireGuardParseResult result)
    {
        // Acepta IPv4/IPv6 [::1] o dominio.
        var clean = host.Trim('[', ']', ' ');
        if (!System.Net.IPAddress.TryParse(clean, out _) && !IsValidEndpointHost(clean))
        {
            result.Errors.Add($"Endpoint host inválido: «{host}»");
        }
    }

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Truncate(string s, int max = 60) =>
        s.Length <= max ? s : s[..max] + "…";
}
