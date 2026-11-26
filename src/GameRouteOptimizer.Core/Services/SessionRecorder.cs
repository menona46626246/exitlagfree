using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRouteOptimizer.Core.Events;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.Storage;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Grabador de sesiones: eventos en vivo, métricas antes/después, historial SQLite
/// y exportación de informes (JSON + Markdown). Todo local, sin red.
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SessionDatabase _db;
    private readonly EventBus _events;
    private readonly IDisposable _eventSubscription;

    private SessionRecord? _current;

    public SessionRecorder(SessionDatabase db, EventBus events)
    {
        _db = db;
        _events = events;
        // Todos los AppEvent de la sesión activa se graban automáticamente.
        _eventSubscription = new AnonymousDisposable(() => { });
        _events.Subscribe<AppEvent>(OnAppEvent);
    }

    public SessionRecord? Current => _current;

    public event Action<SessionEvent>? EventRecorded;

    private void OnAppEvent(AppEvent evt)
    {
        if (_current is null)
        {
            return;
        }

        Record(evt.Severity, evt.Category, evt.Message, stateData: null);
    }

    /// <summary>Inicia una sesión nueva para un perfil/servidor.</summary>
    public SessionRecord StartSession(GameProfile profile, GameServer server, string mode)
    {
        var session = new SessionRecord
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            GameServerTarget = !string.IsNullOrWhiteSpace(server.Ip) ? server.Ip : server.Domain,
            Mode = mode
        };
        _current = session;
        _db.UpsertSession(session);
        Record(EventSeverity.Info, EventCategory.Session,
            $"Sesión iniciada: '{profile.Name}' → {session.GameServerTarget} (modo {mode}).");
        return session;
    }

    public void SetDirectBaseline(ProbeSummary direct) => Apply(s => s.DirectBefore = direct);

    public void SetOptimized(ProbeSummary optimized)
    {
        Apply(s =>
        {
            s.Optimized = optimized;
            s.ImprovementMs = s.DirectBefore is { IsReachable: true } d && optimized.IsReachable
                ? d.MeanMs - optimized.MeanMs
                : null;
        });
    }

    public void SetSelectedCandidate(string candidateId, string relayName) =>
        Apply(s => { s.SelectedCandidateId = candidateId; s.SelectedRelayName = relayName; });

    public void IncrementRouteSwitch() => Apply(s => s.RouteSwitchCount++);

    public void IncrementTunnelDrop() => Apply(s => s.TunnelDropCount++);

    /// <summary>Graba un evento explícito en la sesión activa.</summary>
    public void Record(EventSeverity severity, EventCategory category, string message, string? stateData = null)
    {
        var session = _current;
        if (session is null)
        {
            return;
        }

        var evt = new SessionEvent
        {
            SessionId = session.Id,
            Severity = severity,
            Category = category,
            Message = message,
            Data = stateData
        };
        session.Events.Add(evt);
        _db.AddEvent(evt);
        EventRecorded?.Invoke(evt);
    }

    /// <summary>Cierra la sesión y persiste el resumen final.</summary>
    public SessionRecord EndSession(string finalStatus = "Completed")
    {
        var session = _current ?? throw new InvalidOperationException("No hay sesión activa.");
        session.EndedUtc = DateTimeOffset.UtcNow;
        session.FinalStatus = finalStatus;
        _db.UpsertSession(session);
        Record(EventSeverity.Success, EventCategory.Session, $"Sesión finalizada ({finalStatus}).");
        _current = null;
        return session;
    }

    private void Apply(Action<SessionRecord> mutator)
    {
        if (_current is null)
        {
            return;
        }

        mutator(_current);
        _db.UpsertSession(_current);
    }

    // ---------- Historial ----------

    public List<SessionRecord> GetRecentSessions(int limit = 50) => _db.GetRecentSessions(limit);

    public List<SessionEvent> GetSessionEvents(string sessionId) => _db.GetEvents(sessionId);

    public double? GetHistoricalOptimizedMean(string profileId, string candidateId)
        => _db.GetHistoricalOptimizedMean(profileId, candidateId);

    // ---------- Exportación de informe ----------

    /// <summary>Exporta el informe de una sesión a Markdown; devuelve la ruta escrita.</summary>
    public string ExportReportMarkdown(SessionRecord session, string directory)
    {
        Directory.CreateDirectory(directory);
        var sb = new StringBuilder();
        sb.AppendLine($"# Informe de sesión — {session.ProfileName}");
        sb.AppendLine();
        sb.AppendLine($"- **Sesión:** `{session.Id}`");
        sb.AppendLine($"- **Inicio:** {session.StartedUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Fin:** {(session.EndedUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "en curso")} UTC");
        sb.AppendLine($"- **Objetivo:** {session.GameServerTarget}");
        sb.AppendLine($"- **Modo:** {session.Mode}");
        sb.AppendLine($"- **Ruta elegida:** {session.SelectedRelayName}{(string.IsNullOrEmpty(session.SelectedRelayName) ? "Directa" : "")} (`{session.SelectedCandidateId}`)");
        sb.AppendLine($"- **Cambios de ruta:** {session.RouteSwitchCount} · **Caídas de túnel:** {session.TunnelDropCount}");
        sb.AppendLine($"- **Mejora estimada:** {session.ImprovementText}");
        sb.AppendLine();

        if (session.DirectBefore is { } d)
        {
            sb.AppendLine($"**Directa antes:** {d.MeanMs:0.0} ms · p95 {d.P95Ms:0.0} · pérdida {d.LossRatio:P1} · jitter {d.JitterMs:0.0} ms · estabilidad {d.StabilityScore:0}/100");
        }

        if (session.Optimized is { } o)
        {
            sb.AppendLine($"**Optimizada:** {o.MeanMs:0.0} ms · p95 {o.P95Ms:0.0} · pérdida {o.LossRatio:P1} · jitter {o.JitterMs:0.0} ms · estabilidad {o.StabilityScore:0}/100");
        }

        sb.AppendLine();

        sb.AppendLine("## Eventos");
        sb.AppendLine();
        sb.AppendLine("| Hora | Severidad | Categoría | Mensaje |");
        sb.AppendLine("|------|-----------|-----------|---------|");
        foreach (var e in session.Events)
        {
            sb.AppendLine($"| {e.Timestamp:HH:mm:ss} | {e.Severity} | {e.Category} | {Escape(e.Message)} |");
        }

        var path = Path.Combine(directory, $"informe-sesion-{session.Id[..8]}.md");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    /// <summary>Exporta el informe completo a JSON; devuelve la ruta escrita.</summary>
    public string ExportReportJson(SessionRecord session, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"informe-sesion-{session.Id[..8]}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(session, JsonOpts), Encoding.UTF8);
        return path;
    }

    private static string Escape(string s) => s.Replace("|", "\\|");

    public void Dispose()
    {
        _events.Unsubscribe<AppEvent>(OnAppEvent);
        _db.Dispose();
    }
}

/// <summary>IDisposable mínimo para suscripciones.</summary>
public sealed class AnonymousDisposable(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
