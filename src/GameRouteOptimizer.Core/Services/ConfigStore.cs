using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Persistencia de AppSettings en JSON con escritura atómica (tmp + replace).
/// Ruta por defecto: {dataDir}/settings.json.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public ConfigStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "settings.json");
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        if (!File.Exists(_filePath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            Migrate(settings);
            return settings;
        }
        catch (Exception ex)
        {
            Log.Warning($"settings.json ilegible ({ex.Message}); se usan valores por defecto.");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Version = AppSettings.CurrentVersion;
        WriteAtomic(_filePath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    private static void Migrate(AppSettings settings)
    {
        // Migraciones de versión: por ahora solo se asegura la versión actual.
        if (settings.Version < AppSettings.CurrentVersion) settings.Version = AppSettings.CurrentVersion;
    }

    internal static void WriteAtomic(string path, string content)
        => WriteAtomicPublic(path, content);

    /// <summary>Escritura atómica compartida (tmp + replace), también usada por el Servicio.</summary>
    public static void WriteAtomicPublic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}

/// <summary>
/// Almacén central de perfiles de juego y relays (JSON legible), con la misma
/// política de escritura atómica. Los secretos nunca se guardan aquí.
/// </summary>
public sealed class DataStore
{
    public string ProfilesFilePath { get; }
    public string RelaysFilePath { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public DataStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        ProfilesFilePath = Path.Combine(dataDirectory, "profiles.json");
        RelaysFilePath = Path.Combine(dataDirectory, "relays.json");
    }

    public List<GameProfile> LoadProfiles()
        => LoadList<GameProfile>(ProfilesFilePath);

    public void SaveProfiles(IEnumerable<GameProfile> profiles)
        => SaveList(ProfilesFilePath, profiles);

    public List<RelayNode> LoadRelays()
        => LoadList<RelayNode>(RelaysFilePath);

    public void SaveRelays(IEnumerable<RelayNode> relays)
        => SaveList(RelaysFilePath, relays);

    private static List<T> LoadList<T>(string path)
    {
        if (!File.Exists(path)) return new List<T>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new List<T>();
        }
        catch (Exception ex)
        {
            Log.Warning($"No se pudo leer {path} ({ex.Message}); se inicia lista vacía.");
            return new List<T>();
        }
    }

    private static void SaveList<T>(string path, IEnumerable<T> items)
        => ConfigStore.WriteAtomic(path, JsonSerializer.Serialize(items, JsonOpts));
}
