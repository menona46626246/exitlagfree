using System.Text;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Storage;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Registro de sesiones: persiste cada sesión con sus eventos y métricas, calcula mejoras
/// antes/después y actualiza el aprendizaje del tramo relay→servidor con mediciones reales
/// tomadas con el túnel activo.
/// </summary>
public sealed class SessionRecorder
{
    private readonly ConfigStore _store;
    private readonly object _gate = new();
    private readonly List<SessionRecord> _recent;

    public event EventHandler<SessionRecord>? SessionUpdated;

    public SessionRecorder(ConfigStore store)
    {
        _store = store;
        _recent = store.LoadSessions(100);
    }

    public SessionRecord StartSession(string gameProfileName, string? gameProfileId, string targetDisplay, RouteMode mode)
    {
        lock (_gate)
        {
            var session = new SessionRecord
            {
                GameProfileName = gameProfileName,
                GameProfileId = gameProfileId,
                TargetDisplay = targetDisplay,
                Mode = mode,
            };
            session.AddEvent(SessionEventCategory.Info, "Sesión iniciada.");
            _recent.Insert(0, session);
            if (_recent.Count > 100)
            {
                _recent.RemoveRange(100, _recent.Count - 100);
            }

            _store.SaveSession(session);
            SessionUpdated?.Invoke(this, session);
            return session;
        }
    }

    public void AppendEvent(SessionRecord session, SessionEventCategory category, string message, string? state = null)
    {
        lock (_gate)
        {
            session.AddEvent(category, message, state);
            _store.SaveSession(session);
            SessionUpdated?.Invoke(this, session);
        }
    }

    public void SetRelay(SessionRecord session, RelayNode relay, RouteMode mode)
    {
        lock (_gate)
        {
            session.RelayId = relay.Id;
            session.RelayName = relay.Name;
            session.Mode = mode;
            _store.SaveSession(session);
            SessionUpdated?.Invoke(this, session);
        }
    }

    public void SetDirectMetrics(SessionRecord session, ProbeSummary metrics)
    {
        lock (_gate)
        {
            session.DirectMetrics = metrics;
            RecomputeImprovement(session);
            _store.SaveSession(session);
            SessionUpdated?.Invoke(this, session);
        }
    }

    public void SetOptimizedMetrics(SessionRecord session, ProbeSummary metrics)
    {
        lock (_gate)
        {
            session.OptimizedMetrics = metrics;
            RecomputeImprovement(session);
            _store.SaveSession(session);
            SessionUpdated?.Invoke(this, session);
        }
    }

    public void EndSession(SessionRecord session, string reason)
    {
        lock (_gate)
        {
            if (session.EndedUtc.HasValue)
            {
                return;
            }

            session.EndedUtc = DateTimeOffset.UtcNow;
            session.EndedReason = reason;
            session.AddEvent(SessionEventCategory.Info, $"Sesión finalizada: {reason}");
            _store.SaveSession(session);

            // Aprendizaje del tramo relay→servidor con datos reales del túnel.
            LearnTail(session);

            SessionUpdated?.Invoke(this, session);
        }
    }

    public IReadOnlyList<SessionRecord> RecentSessions() => _recent;

    private void RecomputeImprovement(SessionRecord session)
    {
        if (session.DirectMetrics?.AvgMs is not { } directAvg ||
            session.OptimizedMetrics?.AvgMs is not { } optimizedAvg)
        {
            return;
        }

        session.EstimatedImprovementMs = directAvg - optimizedAvg;
    }

    private void LearnTail(SessionRecord session)
    {
        if (session.OptimizedMetrics?.AvgMs is not { } viaTunnelAvg ||
            session.DirectMetrics is null ||
            string.IsNullOrWhiteSpace(session.RelayId) ||
            session.EstimatedImprovementMs is null)
        {
            return;
        }

        // Estimación del tramo relay→servidor = latencia total con túnel − latencia al relay.
        // Sin una medición del relay en la sesión se usa una cota optimista: total − mejora.
        var directAvg = session.DirectMetrics.AvgMs ?? viaTunnelAvg;
        var tailEstimate = Math.Max(0, viaTunnelAvg - Math.Min(viaTunnelAvg, directAvg - viaTunnelAvg));
        var region = GuessRegion(session);

        var learnings = _store.LoadLearnings();
        var existing = learnings.FirstOrDefault(l =>
            l.RelayId == session.RelayId && l.Region == region);
        if (existing is null)
        {
            existing = new RouteLearning
            {
                RelayId = session.RelayId!,
                Region = region,
                TailAvgMs = tailEstimate,
                Samples = 1,
            };
        }
        else
        {
            var total = existing.Samples + 1;
            existing.TailAvgMs = ((existing.TailAvgMs * existing.Samples) + tailEstimate) / total;
            existing.Samples = total;
        }

        existing.UpdatedUtc = DateTimeOffset.UtcNow;
        _store.SaveLearning(existing);
    }

    private static string GuessRegion(SessionRecord session)
    {
        // Región aproximada: si el juego no indica región, se agrupa por host del objetivo.
        return session.TargetDisplay ?? "desconocida";
    }

    // ---------- exportación de informes ----------

    /// <summary>Exporta la sesión como informe Markdown en español.</summary>
    public string ExportSessionMarkdown(SessionRecord session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Informe de sesión — GameRoute Optimizer");
        sb.AppendLine();
        sb.AppendLine($"- **Inicio:** {session.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(session.EndedUtc.HasValue
            ? $"- **Fin:** {session.EndedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "- **Fin:** (activa)");
        sb.AppendLine($"- **Juego:** {session.GameProfileName}");
        sb.AppendLine($"- **Servidor objetivo:** {session.TargetDisplay}");
        sb.AppendLine($"- **Modo:** {ModeLabel(session.Mode)}");
        sb.AppendLine(session.RelayName is null ? "- **Relay:** (ruta directa)" : $"- **Relay:** {session.RelayName}");

        if (session.DirectMetrics is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## Ruta directa");
            sb.AppendLine();
            sb.AppendLine(MetricsTable(session.DirectMetrics));
        }

        if (session.OptimizedMetrics is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## Con túnel activo");
            sb.AppendLine();
            sb.AppendLine(MetricsTable(session.OptimizedMetrics));
        }

        if (session.EstimatedImprovementMs.HasValue)
        {
            var delta = session.EstimatedImprovementMs.Value;
            var verdict = delta > 1
                ? $"El túnel mejoró la latencia media en {delta:F1} ms."
                : delta < -1
                    ? $"El túnel fue {Math.Abs(delta):F1} ms PEOR que la ruta directa."
                    : "No hubo diferencia significativa entre ambas rutas.";
            sb.AppendLine();
            sb.AppendLine($"## Resultado");
            sb.AppendLine();
            sb.AppendLine(verdict);
        }

        sb.AppendLine();
        sb.AppendLine("## Eventos");
        sb.AppendLine();
        sb.AppendLine("| Hora (local) | Categoría | Mensaje |");
        sb.AppendLine("|---|---|---|");
        foreach (var ev in session.Events)
        {
            var safe = ev.Message.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
            sb.AppendLine($"| {ev.Utc.ToLocalTime():HH:mm:ss} | {ev.Category} | {safe} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Generado por GameRoute Optimizer. Herramienta de diagnóstico honesta: las métricas");
        sb.AppendLine("dependen de la ruta, el ISP y el estado de la red en el momento de la medición.*");
        return sb.ToString();
    }

    private static string MetricsTable(ProbeSummary m)
    {
        static string Fmt(double? v, string suffix = " ms") => v.HasValue ? $"{v:F1}{suffix}" : "—";
        return string.Join(Environment.NewLine,
            $"| Métrica | Valor |",
            $"|---|---|",
            $"| Latencia media | {Fmt(m.AvgMs)} |",
            $"| p95 | {Fmt(m.P95Ms)} |",
            $"| p99 | {Fmt(m.P99Ms)} |",
            $"| Mín / Máx | {Fmt(m.MinMs)} / {Fmt(m.MaxMs)} |",
            $"| Jitter | {Fmt(m.JitterMs)} |",
            $"| Desviación típica | {Fmt(m.StdDevMs)} |",
            $"| Pérdida | {(m.LossPercent.HasValue ? $"{m.LossPercent:F1} %" : "—")} |",
            $"| Pruebas | {m.Successes}/{m.Attempts} exitosas ({m.Kind}) |");
    }

    private static string ModeLabel(RouteMode mode) => mode switch
    {
        RouteMode.Direct => "Ruta directa",
        RouteMode.TunnelGlobal => "Túnel global",
        RouteMode.TunnelGameDestinations => "Túnel solo destinos del juego",
        _ => mode.ToString(),
    };
}
