namespace GameRouteOptimizer.Core.Models;

/// <summary>Protocolos de prueba disponibles.</summary>
public enum ProbeProtocol
{
    Icmp,
    Tcp,
    Udp,
    Http,
    Auto
}

/// <summary>Modo de prueba: rápida (pocas muestras) o profunda (más muestras y estadística).</summary>
public enum ProbeMode
{
    Quick,
    Deep
}

/// <summary>Modo de enrutamiento de un perfil de juego.</summary>
public enum RoutingMode
{
    /// <summary>Sin túnel; solo diagnóstico y recomendaciones.</summary>
    Direct,

    /// <summary>Túnel global: todo el tráfico pasa por el relay.</summary>
    TunnelGlobal,

    /// <summary>Túnel solo para los destinos (IP/prefijos) del juego.</summary>
    TunnelGameOnly,

    /// <summary>Split tunneling por proceso cuando el sistema lo permite; si no, se advierte y se usa por destino.</summary>
    SplitTunnel
}

/// <summary>Estado global del optimizador.</summary>
public enum OptimizerState
{
    Idle,
    DetectingGame,
    ProbingDirect,
    ProbingRelays,
    SelectingRoute,
    WaitingUser,
    Connecting,
    Active,
    Monitoring,
    Degraded,
    Switching,
    FailingBack,
    Stopping,
    Error
}

/// <summary>Estado de un túnel.</summary>
public enum TunnelState
{
    Unknown,
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed,
    Stopping
}

/// <summary>Salud de un relay.</summary>
public enum RelayHealth
{
    Unknown,
    Up,
    Degraded,
    Down
}

/// <summary>Resultado de la evaluación de auto-switch.</summary>
public enum AutoSwitchDecision
{
    NoChange,
    Switch,
    Failback,
    EmergencyRestore
}

/// <summary>Severidad de un evento.</summary>
public enum EventSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Categoría de evento de sesión (para UI y filtrado).</summary>
public enum EventCategory
{
    Session,
    Probe,
    Route,
    Tunnel,
    Game,
    Security,
    System
}
