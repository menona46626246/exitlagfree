using System.Collections.Concurrent;
using System.Net;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>
/// Resolución DNS con caché corta y preferencia IPv4 (los juegos suelen usar IPv4).
/// Solo resuelve dominios aportados por el usuario.
/// </summary>
public sealed class EndpointResolver
{
    private sealed record CacheEntry(DateTimeOffset ExpiresUtc, IReadOnlyList<IPAddress> Addresses);

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        var key = host.Trim().ToLowerInvariant();

        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTimeOffset.UtcNow)
        {
            return cached.Addresses;
        }

        var addresses = await ResolveCoreAsync(host, ct);
        _cache[key] = new CacheEntry(DateTimeOffset.UtcNow + CacheTtl, addresses);
        return addresses;
    }

    public IPAddress? PreferIpv4(IReadOnlyList<IPAddress> addresses)
    {
        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        return ipv4 ?? addresses.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveCoreAsync(string host, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            return addresses.Where(a => a.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                    or System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Distinct()
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<IPAddress>();
        }
    }
}
