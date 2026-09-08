using System.Text.Json.Serialization;

namespace GameRouteOptimizer.Core.Models;

/// <summary>Perfil de un juego: ejecutables, servidores objetivo y preferencias de ruta.</summary>
public sealed class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Ruta completa al ejecutable principal (opcional; se usa para auto-arranque/detección).</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Nombres alternativos de ejecutables para detectar el proceso (p. ej. "game.exe").</summary>
    public List<string> ExecutableNames { get; set; } = new();

    /// <summary>Argumentos opcionales al lanzar el juego.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Servidores/endpoints objetivo del juego.</summary>
    public List<GameServerTarget> Targets { get; set; } = new();

    /// <summary>Modo de ruta preferido.</summary>
    public RouteMode RouteMode { get; set; } = RouteMode.Direct;

    /// <summary>Id del relay preferido (opcional).</summary>
    public string? PreferredRelayId { get; set; }

    /// <summary>Relays bloqueados para este juego.</summary>
    public List<string> BlockedRelayIds { get; set; } = new();

    /// <summary>Preferencia de protocolo de prueba: Auto, ICMP, TCP, UDP, HTTP.</summary>
    public string ProtocolPreference { get; set; } = "Auto";

    /// <summary>Reglas especiales en lenguaje natural (informativas).</summary>
    public string? SpecialRules { get; set; }

    /// <summary>Notas libres del usuario.</summary>
    public string? Notes { get; set; }

    /// <summary>Optimizar automáticamente cuando el juego arranca.</summary>
    public bool AutoStartOptimization { get; set; }

    /// <summary>Detener la optimización cuando el juego se cierra.</summary>
    public bool AutoStopOnExit { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(sin nombre)" : Name;
}

/// <summary>Servidor o endpoint objetivo de un juego (autorizado por el usuario).</summary>
public sealed class GameServerTarget
{
    /// <summary>Dominio público (p. ej. "server1.juego.com").</summary>
    public string? Domain { get; set; }

    /// <summary>IP fija alternativa al dominio.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Puertos relevantes (para TCP/UDP).</summary>
    public List<int> Ports { get; set; } = new();

    /// <summary>Región estimada (p. ej. "EU-Oeste", "US-Este", "Latam-Brasil").</summary>
    public string? Region { get; set; }

    /// <summary>Tipo de prueba por defecto para este destino.</summary>
    public ProbeKind DefaultProbe { get; set; } = ProbeKind.Icmp;

    /// <summary>URL de health HTTP/S si el servicio la expone (opcional).</summary>
    public string? HttpHealthUrl { get; set; }

    [JsonIgnore]
    public string DisplayHost =>
        !string.IsNullOrWhiteSpace(IpAddress) ? IpAddress! :
        !string.IsNullOrWhiteSpace(Domain) ? Domain! : "(sin host)";
}
