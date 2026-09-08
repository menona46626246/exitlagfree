using System.Text.Json.Serialization;

namespace GameRouteOptimizer.Core.Models;

/// <summary>Configuración global de la aplicación (persistida en SQLite como JSON).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "es";

    public ProbingSettings Probing { get; set; } = new();
    public AutoSwitchSettings AutoSwitch { get; set; } = new();
    public TunnelSettings Tunnel { get; set; } = new();
    public LogSettings Logging { get; set; } = new();

    /// <summary>Detectar lanzamientos de juegos en segundo plano (procesos del perfil).</summary>
    public bool WatchGameProcesses { get; set; } = true;

    /// <summary>Directorio de datos local (se fija en tiempo de ejecución; no se persiste).</summary>
    [JsonIgnore]
    public string DataDirectory { get; set; } = string.Empty;
}

/// <summary>Parámetros de las pruebas de red (probes).</summary>
public sealed class ProbingSettings
{
    /// <summary>Permitir ICMP cuando el sistema lo permite.</summary>
    public bool UseIcmp { get; set; } = true;

    /// <summary>Si ICMP está bloqueado, ¿probar con TCP connect?</summary>
    public bool TcpFallbackWhenIcmpBlocked { get; set; } = true;

    /// <summary>Número de probes por destino en modo rápido.</summary>
    public int QuickProbeCount { get; set; } = 5;

    /// <summary>Número de probes por destino en modo profundo.</summary>
    public int DeepProbeCount { get; set; } = 20;

    /// <summary>Timeout de cada probe (ms).</summary>
    public int TimeoutMs { get; set; } = 1200;

    /// <summary>Separación mínima entre probes del mismo destino (ms).</summary>
    public int IntervalMs { get; set; } = 200;

    /// <summary>Puerto por defecto para TCP connect cuando el destino no indica puerto.</summary>
    public int TcpDefaultPort { get; set; } = 443;

    /// <summary>Probes simultáneas máximo (rate limit).</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>Pérdida (%) a partir de la cual se considera problema.</summary>
    public double LossWarningPct { get; set; } = 5;

    /// <summary>Pérdida (%) a partir de la cual un candidato se considera inutilizable.</summary>
    public double LossExtremePct { get; set; } = 25;
}

/// <summary>Parámetros del cambio automático de ruta (auto-switch).</summary>
public sealed class AutoSwitchSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>Mejora mínima (ms) para justificar un cambio de ruta (histéresis).</summary>
    public int MinImprovementMs { get; set; } = 10;

    /// <summary>Espera mínima entre cambios de ruta (s).</summary>
    public int CooldownSeconds { get; set; } = 120;

    /// <summary>El candidato debe mostrar estabilidad durante este tiempo (s).</summary>
    public int StabilityWindowSeconds { get; set; } = 30;

    /// <summary>Jitter máximo aceptable del candidato para cambiar (ms).</summary>
    public int MaxJitterForSwitchMs { get; set; } = 30;

    /// <summary>Pérdida máxima aceptable del candidato para cambiar (%).</summary>
    public double MaxLossForSwitchPct { get; set; } = 5;

    /// <summary>Volver a la ruta directa automáticamente si el túnel falla o empeora.</summary>
    public bool AutoFailback { get; set; } = true;

    /// <summary>Fallos consecutivos de salud del túnel antes de hacer failback.</summary>
    public int FailbackAfterConsecutiveFailures { get; set; } = 3;
}

/// <summary>Parámetros del túnel WireGuard.</summary>
public sealed class TunnelSettings
{
    /// <summary>MTU a usar en la configuración generada.</summary>
    public int Mtu { get; set; } = 1420;

    /// <summary>DNS a forzar en el túnel (vacío = el del relay/DHCP).</summary>
    public string? DnsOverride { get; set; }

    /// <summary>Activar kill switch (bloqueo de salida fuera del túnel) — requiere administrador.</summary>
    public bool KillSwitchEnabled { get; set; } = false;

    /// <summary>Timeout de conexión del túnel (s).</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Intervalo de comprobación de salud del túnel (s).</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 5;

    /// <summary>Directorios donde buscar la instalación oficial de WireGuard.</summary>
    public List<string> WireGuardSearchDirs { get; set; } = new()
    {
        @"C:\Program Files\WireGuard",
        @"C:\Program Files (x86)\WireGuard",
    };
}

/// <summary>Parámetros de logs locales.</summary>
public sealed class LogSettings
{
    public EventLevel MinimumLevel { get; set; } = EventLevel.Information;

    /// <summary>Tamaño máximo por archivo (MB).</summary>
    public int MaxFileMb { get; set; } = 10;

    /// <summary>Archivos rotados retenidos.</summary>
    public int RetainedFiles { get; set; } = 10;
}
