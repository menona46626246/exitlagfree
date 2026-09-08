namespace GameRouteOptimizer.Core.Privileged;

/// <summary>
/// Localiza las herramientas oficiales de WireGuard para Windows (wireguard.exe y wg.exe).
/// Si no están instaladas, el modo túnel ofrece generación de configuración manual.
/// </summary>
public static class WireGuardToolLocator
{
    public static IReadOnlyList<string> DefaultSearchPaths { get; } = new[]
    {
        @"C:\Program Files\WireGuard\wireguard.exe",
        @"C:\Program Files\WireGuard\wg.exe",
        @"C:\Program Files (x86)\WireGuard\wireguard.exe",
        @"C:\Program Files (x86)\WireGuard\wg.exe",
    };

    public static bool IsWireGuardInstalled(IEnumerable<string>? searchPaths = null)
        => FindWireGuardExe(searchPaths) is not null;

    /// <summary>Devuelve la ruta de wireguard.exe o null.</summary>
    public static string? FindWireGuardExe(IEnumerable<string>? searchPaths = null)
        => Find(searchPaths ?? DefaultSearchPaths, "wireguard.exe");

    /// <summary>Devuelve la ruta de wg.exe o null.</summary>
    public static string? FindWgExe(IEnumerable<string>? searchPaths = null)
        => Find(searchPaths ?? DefaultSearchPaths, "wg.exe");

    private static string? Find(IEnumerable<string> paths, string fileName)
    {
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            if (string.Equals(Path.GetFileName(full), fileName, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(full))
            {
                return full;
            }

            // También se aceptan directorios en la lista.
            var candidate = Path.Combine(full, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
