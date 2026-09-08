using System.Net;

namespace GameRouteOptimizer.Core.Probing;

/// <summary>Resultado de una única operación de red a bajo nivel.</summary>
public sealed class ProbeReply
{
    public bool Success { get; init; }

    public double? RttMs { get; init; }

    public ProbeFailureReason Failure { get; init; }

    /// <summary>true para respuestas ICMP "TTL expired" de traceroute.</summary>
    public bool TtlExpired { get; init; }

    /// <summary>Dirección que respondió (útil en traceroute).</summary>
    public IPAddress? Sender { get; init; }

    public string? Detail { get; init; }

    public static ProbeReply Ok(double rttMs, IPAddress? sender = null, string? detail = null) =>
        new() { Success = true, RttMs = rttMs, Sender = sender, Detail = detail };

    public static ProbeReply Fail(ProbeFailureReason reason, string? detail = null) =>
        new() { Success = false, Failure = reason, Detail = detail };
}

/// <summary>Motivos de fallo normalizados (nunca se lanzan excepciones al llamador).</summary>
public enum ProbeFailureReason
{
    None,
    Timeout,
    IcmpBlocked,
    DnsFailure,
    Unreachable,
    Refused,
    ProtocolNotSupported,
    Cancelled,
    Error,
}

/// <summary>Especificación de un destino a sondear (siempre autorizado por el usuario).</summary>
public sealed class ProbeTargetSpec
{
    public required string Label { get; init; }

    public required string Host { get; init; }

    public Models.ProbeKind Kind { get; init; } = Models.ProbeKind.Icmp;

    /// <summary>Puerto para TCP/UDP (obligatorio para esos tipos).</summary>
    public int Port { get; init; }

    /// <summary>URL para HTTP/S health check.</summary>
    public string? HttpUrl { get; init; }
}

/// <summary>Resultado de una serie de probes con resumen estadístico.</summary>
public sealed class ProbeSeriesResult
{
    public required Models.ProbeSummary Summary { get; init; }

    /// <summary>RTTs individuales (ms) en orden, para gráficos.</summary>
    public List<double?> RawRttsMs { get; init; } = new();
}

/// <summary>Salto de un traceroute.</summary>
public sealed class TracerouteHop
{
    public int Ttl { get; init; }
    public string? Address { get; init; }
    public double? RttMs1 { get; init; }
    public double? RttMs2 { get; init; }
    public int Timeouts { get; init; }

    public bool Responded => !string.IsNullOrEmpty(Address) || RttMs1.HasValue || RttMs2.HasValue;
}

/// <summary>Resultado de un traceroute simplificado.</summary>
public sealed class TracerouteResult
{
    public required string Target { get; init; }
    public List<TracerouteHop> Hops { get; init; } = new();
    public bool Completed { get; init; }
    public string? Note { get; init; }

    /// <summary>Observaciones en español (saltos problemáticos).</summary>
    public List<string> Observations { get; init; } = new();
}
