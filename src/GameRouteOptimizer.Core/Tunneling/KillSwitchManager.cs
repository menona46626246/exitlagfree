using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Tunneling;

/// <summary>Executor de operaciones privilegiadas de firewall (implementado por el Servicio; simulado en tests).</summary>
public interface IFirewallExecutor
{
    Task<bool> AddRuleAsync(string name, string programPath, string direction, string action, CancellationToken ct = default);

    Task<bool> DeleteRuleAsync(string name, CancellationToken ct = default);

    Task<bool> RuleExistsAsync(string name, CancellationToken ct = default);
}

/// <summary>
/// Kill switch por firewall de Windows (D7):
/// - Regla de BLOQUEO outbound global "GameRouteKS_BlockAll".
/// - Reglas de PERMISO para wireguard.exe y el servicio "GameRouteKS_Allow_*".
/// Completamente reversible: Disable elimina exactamente las reglas GameRouteKS_*.
/// ADVERTENCIA (mostrada en UI): mientras el kill switch esté activo, si el túnel cae,
/// el tráfico saliente queda bloqueado para proteger la IP real.
/// </summary>
public sealed class KillSwitchManager
{
    public const string BlockRuleName = "GameRouteKS_BlockAll";
    public const string AllowWireGuardRule = "GameRouteKS_Allow_WireGuard";
    public const string AllowServiceRule = "GameRouteKS_Allow_Service";

    private readonly IFirewallExecutor _firewall;
    private readonly Func<string> _wireGuardPath;
    private readonly Func<string> _servicePath;

    public KillSwitchManager(IFirewallExecutor firewall,
        Func<string>? wireGuardPath = null,
        Func<string>? servicePath = null)
    {
        _firewall = firewall;
        _wireGuardPath = wireGuardPath ?? (Func<string>)(() => WireGuardTunnelManager.ResolveWireGuardPath());
        _servicePath = servicePath ?? (Func<string>)(() => Path.Combine(AppContext.BaseDirectory, "GameRouteOptimizer.Service.exe"));
    }

    public bool IsActive { get; private set; }

    /// <summary>Activa el kill switch. Devuelve mensaje de estado en español.</summary>
    public async Task<string> EnableAsync(CancellationToken ct = default)
    {
        var messages = new List<string>();

        // 1) Permitir wireguard.exe (mantener el túnel).
        var wgPath = _wireGuardPath();
        if (!string.IsNullOrWhiteSpace(wgPath) && File.Exists(wgPath))
        {
            var ok = await _firewall.AddRuleAsync(AllowWireGuardRule, wgPath, "out", "allow", ct);
            messages.Add(ok ? "Permiso creado para WireGuard." : "No se pudo crear el permiso de WireGuard.");
        }

        // 2) Permitir el servicio (mantener control local).
        var svcPath = _servicePath();
        if (!string.IsNullOrWhiteSpace(svcPath) && File.Exists(svcPath))
        {
            var ok = await _firewall.AddRuleAsync(AllowServiceRule, svcPath, "out", "allow", ct);
            messages.Add(ok ? "Permiso creado para el servicio." : "No se pudo crear el permiso del servicio.");
        }

        // 3) Bloquear todo el tráfico saliente restante.
        // (El executor crea la regla block-all; los permisos anteriores tienen prioridad).
        var blockOk = await _firewall.AddRuleAsync(BlockRuleName, null!, "out", "block", ct);
        messages.Add(blockOk
            ? "Bloqueo global saliente activo: tu IP real queda protegida si el túnel cae."
            : "No se pudo crear la regla de bloqueo global.");

        IsActive = blockOk;
        Log.Information("Kill switch activado: " + string.Join(" ", messages));
        return string.Join(" ", messages);
    }

    /// <summary>Desactiva el kill switch y restaura la red normal.</summary>
    public async Task<string> DisableAsync(CancellationToken ct = default)
    {
        var ok1 = await _firewall.DeleteRuleAsync(BlockRuleName, ct);
        var ok2 = await _firewall.DeleteRuleAsync(AllowWireGuardRule, ct);
        var ok3 = await _firewall.DeleteRuleAsync(AllowServiceRule, ct);
        IsActive = !(ok1 && ok2 && ok3) && await _firewall.RuleExistsAsync(BlockRuleName, ct);
        if (!IsActive) IsActive = false;
        var message = ok1 ? "Kill switch desactivado: red restaurada." : "No se pudo eliminar la regla de bloqueo (¿se creó manualmente?).";
        Log.Information(message);
        return message;
    }

    /// <summary>Sincroniza el estado interno consultando el firewall real.</summary>
    public async Task<bool> CheckActiveAsync(CancellationToken ct = default)
    {
        IsActive = await _firewall.RuleExistsAsync(BlockRuleName, ct);
        return IsActive;
    }
}
