using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.Service.PrivilegedOperations;

/// <summary>Executor de firewall real sobre netsh advfirewall (Windows).</summary>
public sealed class NetshFirewallExecutor : IFirewallExecutor
{
    public Task<bool> AddRuleAsync(string name, string? programPath, string direction, string action, CancellationToken ct = default)
        => PrivilegedOps.AddFirewallRuleAsync(name, programPath, direction, action, ct);

    public Task<bool> DeleteRuleAsync(string name, CancellationToken ct = default)
        => PrivilegedOps.DeleteFirewallRuleAsync(name, ct);

    public Task<bool> RuleExistsAsync(string name, CancellationToken ct = default)
        => PrivilegedOps.FirewallRuleExistsAsync(name, ct);
}
