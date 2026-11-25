namespace GameRouteOptimizer.Core.Models;

/// <summary>
/// Perfil de juego: ejecutables, servidores objetivo, modo de ruta y preferencias.
/// </summary>
public sealed class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Nombres de ejecutables (sin ruta), p. ej. ["valorant.exe"].</summary>
    public List<string> Executables { get; set; } = new();

    /// <summary>Ruta completa del ejecutable principal (opcional).</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Argumentos opcionales de lanzamiento.</summary>
    public string LaunchArguments { get; set; } = string.Empty;

    public List<GameServer> Servers { get; set; } = new();

    public ProbeProtocol PreferredProtocol { get; set; } = ProbeProtocol.Auto;

    public RoutingMode RoutingMode { get; set; } = RoutingMode.TunnelGameOnly;

    /// <summary>Id del relay preferido (opcional; vacío = automático).</summary>
    public string PreferredRelayId { get; set; } = string.Empty;

    /// <summary>Ids de relays bloqueados por el usuario.</summary>
    public List<string> BlockedRelayIds { get; set; } = new();

    /// <summary>Reglas especiales en texto libre (documentadas en UI; no automatizan nada peligroso).</summary>
    public string SpecialRules { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>Iniciar optimización automáticamente cuando el juego arranque (opcional).</summary>
    public bool AutoStartOptimization { get; set; }

    /// <summary>Detener optimización cuando el juego se cierre (opcional).</summary>
    public bool StopOnGameExit { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Texto resumido de ejecutables (para la UI).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ExecutablesText => string.Join(", ", Executables);

    /// <summary>Texto del modo de ruta (para la UI).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string RoutingModeText => RoutingMode switch
    {
        RoutingMode.Direct => "Directo",
        RoutingMode.TunnelGlobal => "Túnel global",
        RoutingMode.TunnelGameOnly => "Túnel solo juego",
        RoutingMode.SplitTunnel => "Split tunneling",
        _ => RoutingMode.ToString()
    };

    public GameProfile Clone()
    {
        var copy = (GameProfile)MemberwiseClone();
        copy.Executables = new List<string>(Executables);
        copy.Servers = Servers.Select(s => s.Clone()).ToList();
        copy.BlockedRelayIds = new List<string>(BlockedRelayIds);
        return copy;
    }

    /// <summary>Valida el perfil y devuelve errores (lista vacía = válido).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("El nombre del juego es obligatorio.");
        if (Executables.Count == 0 && string.IsNullOrWhiteSpace(ExecutablePath))
            errors.Add("Define al menos un ejecutable o la ruta del ejecutable.");
        if (Servers.Count == 0) errors.Add("Añade al menos un servidor objetivo.");
        foreach (var s in Servers.Where(s => !s.IsValid))
            errors.Add($"El servidor '{s.Name}' necesita dominio o IP.");
        return errors;
    }
}
