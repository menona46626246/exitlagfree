namespace GameRouteOptimizer.Core.Models;

/// <summary>Un salto de traceroute con sus muestras.</summary>
public sealed class TracerouteHop
{
    public int Hop { get; set; }

    public string Address { get; set; } = "*";

    public string HostName { get; set; } = string.Empty;

    public List<double> RttSamplesMs { get; set; } = new();

    public bool Responded => Address != "*" && RttSamplesMs.Count > 0;

    public double BestMs => Responded ? RttSamplesMs.Min() : double.NaN;

    public double MeanMs => Responded ? RttSamplesMs.Average() : double.NaN;

    /// <summary>Nº de probes sin respuesta en este salto.</summary>
    public int LostProbes { get; set; }

    /// <summary>Posible cuello de botella: salto con incremento fuerte de latencia respecto al anterior.</summary>
    public bool SuspectedBottleneck { get; set; }
}

/// <summary>Resultado completo de un traceroute.</summary>
public sealed class TracerouteResult
{
    public string Target { get; set; } = string.Empty;

    public string ResolvedIp { get; set; } = string.Empty;

    public List<TracerouteHop> Hops { get; set; } = new();

    public bool Completed { get; set; }

    public string Method { get; set; } = "icmp-ttl";

    public TimeSpan Duration { get; set; }

    /// <summary>Resumen en español con el cuello de botella más probable.</summary>
    public string Explain()
    {
        if (Hops.Count == 0)
        {
            return "No se obtuvieron saltos. Verifica conectividad y permisos ICMP.";
        }

        var bottleneck = Hops.FirstOrDefault(h => h.SuspectedBottleneck);
        var last = Hops.Last();
        if (!last.Responded)
        {
            return $"La ruta llega hasta el salto {last.Hop} sin alcanzar el destino; probable filtrado o caída más allá de ese punto.";
        }

        if (bottleneck is { Responded: true })
        {
            return $"Ruta completa en {Hops.Count} saltos. Mayor incremento de latencia en el salto {bottleneck.Hop} ({bottleneck.Address}, ~{bottleneck.MeanMs:F0} ms): posible cuello de botella.";
        }

        return $"Ruta completa en {Hops.Count} saltos sin incrementos anómalos de latencia.";
    }
}
