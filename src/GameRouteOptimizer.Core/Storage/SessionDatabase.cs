using System.Text.Json;
using GameRouteOptimizer.Core.Models;
using Microsoft.Data.Sqlite;

namespace GameRouteOptimizer.Core.Storage;

/// <summary>
/// Persistencia SQLite de sesiones y eventos (historial local). Esquema:
///   sessions(id TEXT PK, profile_id, profile_name, target, mode, started, ended,
///            status, direct_before, optimized, selected_candidate, selected_relay,
///            route_switches, tunnel_drops, improvement_ms)
///   session_events(id INTEGER PK AUTOINCREMENT, session_id, timestamp, severity,
///                  category, message, data)
/// Los ProbeSummary se guardan como JSON.
/// </summary>
public sealed class SessionDatabase : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public string DatabasePath { get; }

    public SessionDatabase(string dataDirectory, string fileName = "sessions.db")
    {
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, fileName);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT PRIMARY KEY,
                    profile_id TEXT NOT NULL,
                    profile_name TEXT NOT NULL,
                    target TEXT NOT NULL,
                    mode TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    ended_utc TEXT,
                    status TEXT NOT NULL,
                    direct_before TEXT,
                    optimized TEXT,
                    selected_candidate TEXT,
                    selected_relay TEXT,
                    route_switches INTEGER DEFAULT 0,
                    tunnel_drops INTEGER DEFAULT 0,
                    improvement_ms REAL
                );
                CREATE TABLE IF NOT EXISTS session_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    severity TEXT NOT NULL,
                    category TEXT NOT NULL,
                    message TEXT NOT NULL,
                    data TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_sessions_started ON sessions(started_utc DESC);
                CREATE INDEX IF NOT EXISTS idx_events_session ON session_events(session_id);
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public void UpsertSession(SessionRecord session)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sessions (id, profile_id, profile_name, target, mode, started_utc, ended_utc,
                    status, direct_before, optimized, selected_candidate, selected_relay,
                    route_switches, tunnel_drops, improvement_ms)
                VALUES ($id, $pid, $pname, $target, $mode, $started, $ended, $status, $db, $opt,
                    $cand, $relay, $sw, $drops, $imp)
                ON CONFLICT(id) DO UPDATE SET
                    ended_utc=$ended, status=$status, direct_before=$db, optimized=$opt,
                    selected_candidate=$cand, selected_relay=$relay, route_switches=$sw,
                    tunnel_drops=$drops, improvement_ms=$imp;
                """;
            cmd.Parameters.AddWithValue("$id", session.Id);
            cmd.Parameters.AddWithValue("$pid", session.ProfileId);
            cmd.Parameters.AddWithValue("$pname", session.ProfileName);
            cmd.Parameters.AddWithValue("$target", session.GameServerTarget);
            cmd.Parameters.AddWithValue("$mode", session.Mode);
            cmd.Parameters.AddWithValue("$started", session.StartedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$ended", (object?)session.EndedUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", session.FinalStatus);
            cmd.Parameters.AddWithValue("$db", session.DirectBefore is null ? DBNull.Value : JsonSerializer.Serialize(session.DirectBefore, JsonOpts));
            cmd.Parameters.AddWithValue("$opt", session.Optimized is null ? DBNull.Value : JsonSerializer.Serialize(session.Optimized, JsonOpts));
            cmd.Parameters.AddWithValue("$cand", session.SelectedCandidateId);
            cmd.Parameters.AddWithValue("$relay", session.SelectedRelayName);
            cmd.Parameters.AddWithValue("$sw", session.RouteSwitchCount);
            cmd.Parameters.AddWithValue("$drops", session.TunnelDropCount);
            cmd.Parameters.AddWithValue("$imp", (object?)session.ImprovementMs ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void AddEvent(SessionEvent evt)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO session_events (session_id, timestamp, severity, category, message, data)
                VALUES ($sid, $ts, $sev, $cat, $msg, $data);
                """;
            cmd.Parameters.AddWithValue("$sid", evt.SessionId);
            cmd.Parameters.AddWithValue("$ts", evt.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("$sev", evt.Severity.ToString());
            cmd.Parameters.AddWithValue("$cat", evt.Category.ToString());
            cmd.Parameters.AddWithValue("$msg", evt.Message);
            cmd.Parameters.AddWithValue("$data", (object?)evt.Data ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public List<SessionRecord> GetRecentSessions(int limit = 50)
    {
        var result = new List<SessionRecord>();
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM sessions ORDER BY started_utc DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var s = new SessionRecord
                {
                    Id = reader.GetString(reader.GetOrdinal("id")),
                    ProfileId = reader.GetString(reader.GetOrdinal("profile_id")),
                    ProfileName = reader.GetString(reader.GetOrdinal("profile_name")),
                    GameServerTarget = reader.GetString(reader.GetOrdinal("target")),
                    Mode = reader.GetString(reader.GetOrdinal("mode")),
                    StartedUtc = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_utc"))),
                    EndedUtc = reader.IsDBNull(reader.GetOrdinal("ended_utc")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ended_utc"))),
                    FinalStatus = reader.GetString(reader.GetOrdinal("status")),
                    SelectedCandidateId = reader.IsDBNull(reader.GetOrdinal("selected_candidate")) ? "" : reader.GetString(reader.GetOrdinal("selected_candidate")),
                    SelectedRelayName = reader.IsDBNull(reader.GetOrdinal("selected_relay")) ? "" : reader.GetString(reader.GetOrdinal("selected_relay")),
                    RouteSwitchCount = reader.GetInt32(reader.GetOrdinal("route_switches")),
                    TunnelDropCount = reader.GetInt32(reader.GetOrdinal("tunnel_drops")),
                    ImprovementMs = reader.IsDBNull(reader.GetOrdinal("improvement_ms")) ? null : reader.GetDouble(reader.GetOrdinal("improvement_ms")),
                    DirectBefore = ReadSummary(reader, "direct_before"),
                    Optimized = ReadSummary(reader, "optimized")
                };
                result.Add(s);
            }
        }
        return result;
    }

    public List<SessionEvent> GetEvents(string sessionId)
    {
        var result = new List<SessionEvent>();
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM session_events WHERE session_id=$sid ORDER BY id;";
            cmd.Parameters.AddWithValue("$sid", sessionId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new SessionEvent
                {
                    Id = reader.GetInt64(reader.GetOrdinal("id")),
                    SessionId = reader.GetString(reader.GetOrdinal("session_id")),
                    Timestamp = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("timestamp"))),
                    Severity = Enum.Parse<EventSeverity>(reader.GetString(reader.GetOrdinal("severity"))),
                    Category = Enum.Parse<EventCategory>(reader.GetString(reader.GetOrdinal("category"))),
                    Message = reader.GetString(reader.GetOrdinal("message")),
                    Data = reader.IsDBNull(reader.GetOrdinal("data")) ? null : reader.GetString(reader.GetOrdinal("data"))
                });
            }
        }
        return result;
    }

    /// <summary>Mejor latencia relay→juego conocida por historial (para RelayToGameMs aprendido).</summary>
    public double? GetHistoricalOptimizedMean(string profileId, string candidateId)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT optimized FROM sessions
                WHERE profile_id=$pid AND selected_candidate=$cand AND optimized IS NOT NULL
                ORDER BY started_utc DESC LIMIT 10;
                """;
            cmd.Parameters.AddWithValue("$pid", profileId);
            cmd.Parameters.AddWithValue("$cand", candidateId);
            using var reader = cmd.ExecuteReader();
            var means = new List<double>();
            while (reader.Read())
            {
                try
                {
                    var summary = JsonSerializer.Deserialize<ProbeSummary>(reader.GetString(0), JsonOpts);
                    if (summary is { IsReachable: true })
                    {
                        means.Add(summary.MeanMs);
                    }
                }
                catch { /* fila corrupta: se ignora */ }
            }
            return means.Count == 0 ? null : means.Average();
        }
    }

    private static ProbeSummary? ReadSummary(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        try { return JsonSerializer.Deserialize<ProbeSummary>(reader.GetString(ordinal), JsonOpts); }
        catch { return null; }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
