using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Service.PrivilegedOperations;

namespace GameRouteOptimizer.Service.RouteService;

/// <summary>
/// Implementación real de IRouteApplier sobre herramientas del sistema (netsh/route/ip)
/// a través de PrivilegedOps. Requiere que el servicio corra elevado.
/// </summary>
public sealed class SystemRouteApplier : IRouteApplier
{
    public async Task<List<RouteEntry>> GetCurrentRoutesAsync(CancellationToken ct = default)
    {
        var routes = await PrivilegedOps.ReadRoutesAsync(ct);
        return routes.Select(r => new RouteEntry(r.Cidr, r.Gateway, r.IfIndex, null)).ToList();
    }

    public async Task<bool> AddRouteAsync(RouteEntry entry, CancellationToken ct = default)
        => await PrivilegedOps.AddRouteAsync(entry.DestinationCidr, entry.Gateway, entry.InterfaceIndex, entry.Metric, ct);

    public async Task<bool> DeleteRouteAsync(RouteEntry entry, CancellationToken ct = default)
        => await PrivilegedOps.DeleteRouteAsync(entry.DestinationCidr, entry.Gateway, entry.InterfaceIndex, ct);
}
