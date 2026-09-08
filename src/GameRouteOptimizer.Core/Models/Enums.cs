namespace GameRouteOptimizer.Core.Models;

/// <summary>Estados del programa (máquina de estados global).</summary>
public enum ProgramState
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
    Error,
}

/// <summary>Tipo de prueba de red (probe).</summary>
public enum ProbeKind
{
    Icmp,
    TcpConnect,
    Udp,
    HttpGet,
}

/// <summary>Modo de ruta de un perfil de juego.</summary>
public enum RouteMode
{
    /// <summary>Sin túnel: se usa la ruta que da el ISP.</summary>
    Direct,

    /// <summary>Todo el tráfico de la máquina pasa por el túnel.</summary>
    TunnelGlobal,

    /// <summary>Solo los destinos del juego pasan por el túnel (routing por destino).</summary>
    TunnelGameDestinations,
}

/// <summary>Tipo de candidato de ruta evaluado por el scoring.</summary>
public enum RouteCandidateKind
{
    Direct,
    Relay,
}

/// <summary>Nivel de severidad de un evento/entrada de log.</summary>
public enum EventLevel
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
}

/// <summary>Categoría de un evento de sesión.</summary>
public enum SessionEventCategory
{
    Info,
    Warning,
    Error,
    RouteChange,
    State,
    Tunnel,
}
