namespace GameRouteOptimizer.Core.Models;

/// <summary>
/// Configuración de túnel WireGuard (modelo tipado de un archivo .conf).
/// La clave privada NUNCA se registra en logs; WireGuardConfig.WriteLine enmascara.
/// </summary>
public sealed class TunnelProfile
{
    public string Name { get; set; } = string.Empty;

    // [Interface]
    public string PrivateKey { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Dns { get; set; } = string.Empty;

    public int Mtu { get; set; } = 1380;

    public int ListenPort { get; set; }

    // [Peer]
    public string PublicKey { get; set; } = string.Empty;

    public string PresharedKey { get; set; } = string.Empty;

    /// <summary>Prefijos CIDR que se enrutan por el túnel (AllowedIPs).</summary>
    public List<string> AllowedIps { get; set; } = new();

    /// <summary>Endpoint del relay (host:puerto).</summary>
    public string Endpoint { get; set; } = string.Empty;

    public int PersistentKeepalive { get; set; } = 25;

    /// <summary>Serializa a formato .conf de WireGuard (clave privada enmascarada).</summary>
    public string ToConfString(bool includePrivateKey = false)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine(includePrivateKey
            ? $"PrivateKey = {SecretMasker.Mask(PrivateKey)}"
            : "PrivateKey = ***");
        sb.AppendLine($"Address = {Address}");
        if (!string.IsNullOrWhiteSpace(Dns)) sb.AppendLine($"DNS = {Dns}");
        if (Mtu > 0) sb.AppendLine($"MTU = {Mtu}");
        if (ListenPort > 0) sb.AppendLine($"ListenPort = {ListenPort}");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {(string.IsNullOrWhiteSpace(PublicKey) ? "(vacía)" : PublicKey)}");
        if (!string.IsNullOrWhiteSpace(PresharedKey)) sb.AppendLine("PresharedKey = ***");
        if (AllowedIps.Count > 0) sb.AppendLine($"AllowedIPs = {string.Join(", ", AllowedIps)}");
        if (!string.IsNullOrWhiteSpace(Endpoint)) sb.AppendLine($"Endpoint = {Endpoint}");
        if (PersistentKeepalive > 0) sb.AppendLine($"PersistentKeepalive = {PersistentKeepalive}");
        return sb.ToString();
    }
}

/// <summary>Enmascara secretos para logs/exports.</summary>
public static class SecretMasker
{
    public static string Mask(string secret)
        => string.IsNullOrWhiteSpace(secret) ? "(no definida)" : "***";
}
