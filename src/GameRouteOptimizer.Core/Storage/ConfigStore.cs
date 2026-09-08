using System.Text.Json;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Storage;

/// <summary>
/// Almacén local SQLite (Microsoft.Data.Sqlite): ajustes, perfiles, relays, sesiones y aprendizajes.
/// Todo es local; sin telemetría. Los secretos se guardan cifrados en otra tabla (ver ISecretProtector).
/// </summary>
public sealed class ConfigStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _connectionString;

    public string DbPath { get; }

    public ConfigStore(string? dbPath = null)
    {
        DbPath = dbPath ?? DefaultDbPath();
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            DefaultTimeout = 20,
            // WAL: permite lecturas concurrentes y reduce bloqueos entre procesos/instancias.
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        Initialize();
    }

    public static string DefaultDbPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(Path.GetTempPath(), "GameRouteOptimizer");
        }

        return Path.Combine(baseDir, "GameRouteOptimizer", "gro.db");
    }

    // ---------------- schema ----------------

    private void Initialize()
    {
        ExecuteNonQuery(
            "PRAGMA journal_mode=WAL;" +
            "CREATE TABLE IF NOT EXISTS Settings(key TEXT PRIMARY KEY, json TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS GameProfiles(id TEXT PRIMARY KEY, name TEXT NOT NULL, json TEXT NOT NULL, updated_utc TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS Relays(id TEXT PRIMARY KEY, name TEXT NOT NULL, json TEXT NOT NULL, updated_utc TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS RelaySecrets(relay_id TEXT PRIMARY KEY, protected_key TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS Learnings(relay_id TEXT NOT NULL, region TEXT NOT NULL, tail_avg_ms REAL NOT NULL, samples INTEGER NOT NULL, updated_utc TEXT NOT NULL, PRIMARY KEY(relay_id, region));" +
            "CREATE TABLE IF NOT EXISTS Sessions(id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, json TEXT NOT NULL);" +
            "PRAGMA user_version=1;");
    }

    private void ExecuteNonQuery(string sql)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ---------------- settings ----------------

    public AppSettings LoadSettings()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM Settings WHERE key='app';";
            var row = cmd.ExecuteScalar() as string;
            if (row is null)
            {
                var def = new AppSettings();
                SaveSettings(def);
                return def;
            }

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(row, JsonOptions) ?? new AppSettings();
                Sanitize(settings);
                return settings;
            }
            catch (JsonException)
            {
                return new AppSettings();
            }
        }
    }

    /// <summary>
    /// Rehidrata subobjetos que pudieran venir como null en JSON corrupto/manual:
    /// evita NullReferenceException en el resto de la aplicación.
    /// </summary>
    private static void Sanitize(AppSettings settings)
    {
        settings.Probing ??= new Models.ProbingSettings();
        settings.AutoSwitch ??= new Models.AutoSwitchSettings();
        settings.Tunnel ??= new Models.TunnelSettings();
        settings.Logging ??= new Models.LogSettings();
        settings.Tunnel.WireGuardSearchDirs ??= new List<string>();
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Settings(key, json) VALUES('app', $json) " +
                              "ON CONFLICT(key) DO UPDATE SET json=$json;";
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings, JsonOptions));
            cmd.ExecuteNonQuery();
        }
    }

    // ---------------- game profiles ----------------

    public List<GameProfile> LoadGameProfiles()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM GameProfiles ORDER BY name COLLATE NOCASE;";
            using var reader = cmd.ExecuteReader();
            var list = new List<GameProfile>();
            while (reader.Read())
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<GameProfile>(reader.GetString(0), JsonOptions);
                    if (profile is not null)
                    {
                        list.Add(profile);
                    }
                }
                catch (JsonException)
                {
                    // Fila corrupta: se ignora y se reporta en logs del llamador si lo desea.
                }
            }

            return list;
        }
    }

    public void SaveGameProfile(GameProfile profile)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO GameProfiles(id, name, json, updated_utc) VALUES($id, $name, $json, $utc) " +
                              "ON CONFLICT(id) DO UPDATE SET name=$name, json=$json, updated_utc=$utc;";
            cmd.Parameters.AddWithValue("$id", profile.Id);
            cmd.Parameters.AddWithValue("$name", profile.Name);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(profile, JsonOptions));
            cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteGameProfile(string id)
    {
        ExecuteWithLock(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM GameProfiles WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });
    }

    // ---------------- relays ----------------

    public List<RelayNode> LoadRelays()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM Relays ORDER BY name COLLATE NOCASE;";
            using var reader = cmd.ExecuteReader();
            var list = new List<RelayNode>();
            while (reader.Read())
            {
                try
                {
                    var relay = JsonSerializer.Deserialize<RelayNode>(reader.GetString(0), JsonOptions);
                    if (relay is not null)
                    {
                        list.Add(relay);
                    }
                }
                catch (JsonException)
                {
                    // Fila corrupta: se ignora.
                }
            }

            return list;
        }
    }

    public void SaveRelay(RelayNode relay)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Relays(id, name, json, updated_utc) VALUES($id, $name, $json, $utc) " +
                              "ON CONFLICT(id) DO UPDATE SET name=$name, json=$json, updated_utc=$utc;";
            cmd.Parameters.AddWithValue("$id", relay.Id);
            cmd.Parameters.AddWithValue("$name", relay.Name);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(relay, JsonOptions));
            cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteRelay(string id)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Relays WHERE id=$id; DELETE FROM RelaySecrets WHERE relay_id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // ---------------- secrets (cifrados) ----------------

    public void SaveProtectedSecret(string relayId, string protectedKeyBase64)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO RelaySecrets(relay_id, protected_key) VALUES($id, $key) " +
                              "ON CONFLICT(relay_id) DO UPDATE SET protected_key=$key;";
            cmd.Parameters.AddWithValue("$id", relayId);
            cmd.Parameters.AddWithValue("$key", protectedKeyBase64);
            cmd.ExecuteNonQuery();
        }
    }

    public string? LoadProtectedSecret(string relayId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT protected_key FROM RelaySecrets WHERE relay_id=$id;";
            cmd.Parameters.AddWithValue("$id", relayId);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void DeleteProtectedSecret(string relayId)
    {
        ExecuteWithLock(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM RelaySecrets WHERE relay_id=$id;";
            cmd.Parameters.AddWithValue("$id", relayId);
            cmd.ExecuteNonQuery();
        });
    }

    // ---------------- learnings relay→región ----------------

    public List<RouteLearning> LoadLearnings()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT relay_id, region, tail_avg_ms, samples, updated_utc FROM Learnings;";
            using var reader = cmd.ExecuteReader();
            var list = new List<RouteLearning>();
            while (reader.Read())
            {
                list.Add(new RouteLearning
                {
                    RelayId = reader.GetString(0),
                    Region = reader.GetString(1),
                    TailAvgMs = reader.GetDouble(2),
                    Samples = reader.GetInt32(3),
                    UpdatedUtc = DateTimeOffset.Parse(reader.GetString(4)),
                });
            }

            return list;
        }
    }

    public void SaveLearning(RouteLearning learning)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO Learnings(relay_id, region, tail_avg_ms, samples, updated_utc) VALUES($rid, $region, $avg, $samples, $utc) " +
                "ON CONFLICT(relay_id, region) DO UPDATE SET tail_avg_ms=$avg, samples=$samples, updated_utc=$utc;";
            cmd.Parameters.AddWithValue("$rid", learning.RelayId);
            cmd.Parameters.AddWithValue("$region", learning.Region);
            cmd.Parameters.AddWithValue("$avg", learning.TailAvgMs);
            cmd.Parameters.AddWithValue("$samples", learning.Samples);
            cmd.Parameters.AddWithValue("$utc", learning.UpdatedUtc.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    // ---------------- sesiones ----------------

    public void SaveSession(SessionRecord session)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Sessions(id, started_utc, json) VALUES($id, $utc, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json=$json;";
            cmd.Parameters.AddWithValue("$id", session.Id);
            cmd.Parameters.AddWithValue("$utc", session.StartedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(session, JsonOptions));
            cmd.ExecuteNonQuery();
        }
    }

    public List<SessionRecord> LoadSessions(int max = 100)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM Sessions ORDER BY started_utc DESC LIMIT $max;";
            cmd.Parameters.AddWithValue("$max", max);
            using var reader = cmd.ExecuteReader();
            var list = new List<SessionRecord>();
            while (reader.Read())
            {
                try
                {
                    var s = JsonSerializer.Deserialize<SessionRecord>(reader.GetString(0), JsonOptions);
                    if (s is not null)
                    {
                        list.Add(s);
                    }
                }
                catch (JsonException)
                {
                    // Fila corrupta: se ignora.
                }
            }

            return list;
        }
    }

    public void DeleteSessionsOlderThan(TimeSpan age)
    {
        ExecuteWithLock(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Sessions WHERE started_utc < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", (DateTimeOffset.UtcNow - age).ToString("O"));
            cmd.ExecuteNonQuery();
        });
    }

    // ---------------- helpers ----------------

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        conn.Open();
        using var busy = conn.CreateCommand();
        busy.CommandText = "PRAGMA busy_timeout=10000;";
        busy.ExecuteNonQuery();
        return conn;
    }

    private void ExecuteWithLock(Action<Microsoft.Data.Sqlite.SqliteConnection> action)
    {
        lock (_gate)
        {
            using var conn = Open();
            action(conn);
        }
    }

    public void Dispose()
    {
    }
}
