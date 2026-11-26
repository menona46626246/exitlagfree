using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Profiles;

/// <summary>Modelo de importación/exportación de un perfil de juego (JSON).</summary>
public sealed class ProfileExportModel
{
    public const string FormatMarker = "gameroute-profile";

    public string Format { get; set; } = FormatMarker;

    public int Version { get; set; } = 1;

    public string ExportedUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public GameProfile Profile { get; set; } = new();
}

/// <summary>Gestor de perfiles de juego: CRUD, validación, import/export JSON.</summary>
public sealed class ProfileManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly DataStore _store;
    private List<GameProfile> _profiles;

    public event Action<GameProfile>? ProfileAdded;
    public event Action<GameProfile>? ProfileUpdated;
    public event Action<string>? ProfileRemoved;

    public ProfileManager(DataStore store)
    {
        _store = store;
        _profiles = store.LoadProfiles();
    }

    public IReadOnlyList<GameProfile> Profiles => _profiles.AsReadOnly();

    public GameProfile? GetById(string id) => _profiles.FirstOrDefault(p => p.Id == id);

    public void Add(GameProfile profile)
    {
        var errors = profile.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        if (_profiles.Any(p => p.Id == profile.Id))
        {
            throw new ArgumentException("Ya existe un perfil con ese id.");
        }

        _profiles.Add(profile);
        Persist();
        ProfileAdded?.Invoke(profile);
    }

    public void Update(GameProfile profile)
    {
        var existing = GetById(profile.Id) ?? throw new KeyNotFoundException("Perfil no encontrado.");
        var errors = profile.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        var index = _profiles.IndexOf(existing);
        profile.UpdatedUtc = DateTime.UtcNow;
        _profiles[index] = profile;
        Persist();
        ProfileUpdated?.Invoke(profile);
    }

    public bool Remove(string id)
    {
        var removed = _profiles.RemoveAll(p => p.Id == id) > 0;
        if (removed) { Persist(); ProfileRemoved?.Invoke(id); }
        return removed;
    }

    private void Persist() => _store.SaveProfiles(_profiles);

    // ---------- Import / Export JSON ----------

    /// <summary>Exporta un perfil (sin secretos: los perfiles nunca contienen claves).</summary>
    public string ExportProfileJson(GameProfile profile)
    {
        var model = new ProfileExportModel { Profile = profile };
        return JsonSerializer.Serialize(model, JsonOpts);
    }

    /// <summary>Importa un perfil desde JSON; devuelve el perfil importado (id nuevo si colisiona).</summary>
    public GameProfile ImportProfileJson(string json, bool replaceId = true)
    {
        GameProfile? profile;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            profile = root.TryGetProperty("profile", out var inner)
                ? JsonSerializer.Deserialize<GameProfile>(inner.GetRawText(), JsonOpts)
                : JsonSerializer.Deserialize<GameProfile>(root.GetRawText(), JsonOpts);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("El archivo no es un JSON válido de perfil de juego.");
        }
        if (profile is null)
        {
            throw new InvalidDataException("El JSON no contiene un perfil de juego.");
        }

        if (replaceId || string.IsNullOrEmpty(profile.Id) || _profiles.Any(p => p.Id == profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }

        Add(profile);
        return profile;
    }
}
