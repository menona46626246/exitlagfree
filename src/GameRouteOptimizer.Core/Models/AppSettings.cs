namespace GameRouteOptimizer.Core.Models;

/// <summary>
/// Configuración global de la aplicación. Persistida por ConfigStore como JSON
/// (settings.json) con escritura atómica.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    // --- Probing ---
    /// <summary>Intervalo entre probes en ms (rate limit base).</summary>
    public int ProbeIntervalMs { get; set; } = 1000;

    /// <summary>Timeout por probe en ms.</summary>
    public int ProbeTimeoutMs { get; set; } = 2000;

    /// <summary>Muestras en modo rápido.</summary>
    public int QuickSamples { get; set; } = 8;

    /// <summary>Muestras en modo profundo.</summary>
    public int DeepSamples { get; set; } = 40;

    /// <summary>Máximo de probes por segundo (protección anti-saturación).</summary>
    public double MaxProbesPerSecond { get; set; } = 5;

    /// <summary>Intervalo de monitoreo continuo en ms.</summary>
    public int MonitorIntervalMs { get; set; } = 3000;

    // --- Auto-switch ---
    public bool AutoSwitchEnabled { get; set; } = true;

    /// <summary>Mejora mínima de score para cambiar de ruta (evita flapping).</summary>
    public double MinImprovementScore { get; set; } = 8;

    /// <summary>Mejora mínima de latencia en ms para cambiar de ruta.</summary>
    public double MinImprovementLatencyMs { get; set; } = 10;

    /// <summary>Cooldown entre cambios de ruta en segundos.</summary>
    public int SwitchCooldownSeconds { get; set; } = 300;

    /// <summary>Muestras mínimas antes de aceptar un cambio.</summary>
    public int MinSamplesToSwitch { get; set; } = 5;

    /// <summary>Máximo de cambios de ruta por hora.</summary>
    public int MaxSwitchesPerHour { get; set; } = 4;

    /// <summary>Regresar a ruta directa si el túnel falla.</summary>
    public bool FailbackOnTunnelDown { get; set; } = true;

    /// <summary>Regresar a ruta directa si la calidad del túnel empeora respecto a la directa.</summary>
    public bool FailbackOnWorseQuality { get; set; } = true;

    // --- Túnel / red ---
    /// <summary>MTU del túnel (1200-1420 recomendado para WireGuard).</summary>
    public int TunnelMtu { get; set; } = 1380;

    /// <summary>DNS a usar dentro del túnel (vacío = el del relay/configuración).</summary>
    public string TunnelDns { get; set; } = string.Empty;

    public bool KillSwitchEnabled { get; set; } = false;

    /// <summary>Reintentos de conexión con backoff exponencial (máx).</summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    // --- Umbrales de scoring ---
    /// <summary>Pérdida extrema que excluye un candidato (0-1).</summary>
    public double ExtremeLossThreshold { get; set; } = 0.20;

    // --- Aplicación ---
    public string Language { get; set; } = "es";

    public string Theme { get; set; } = "dark";

    public string LogLevel { get; set; } = "Information";

    /// <summary>Directorio de datos (portable si se deja vacío: ./data).</summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>Modo seguro: nunca aplica túneles ni rutas (solo diagnóstico).</summary>
    public bool SafeMode { get; set; } = false;

    /// <summary>El usuario confirmó que revisó los ToS de sus juegos respecto a VPN/routing.</summary>
    public bool UserAcknowledgedGameToS { get; set; } = false;

    /// <summary>Indica si el asistente de primera ejecución ya fue completado u omitido.</summary>
    public bool OnboardingCompleted { get; set; } = false;

    // --- Detección de juegos ---
    /// <summary>El usuario autorizó escanear carpetas comunes para detectar juegos instalados.</summary>
    public bool AllowGameScan { get; set; } = false;

    public List<string> GameScanRoots { get; set; } = new();

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
