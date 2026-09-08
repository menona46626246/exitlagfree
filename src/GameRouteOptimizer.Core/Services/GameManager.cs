using System.Diagnostics;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Storage;

namespace GameRouteOptimizer.Core.Services;

/// <summary>Gestión de perfiles de juego: CRUD, import/export JSON y utilidades de proceso.</summary>
public sealed class GameManager
{
    private readonly ConfigStore _store;
    private readonly object _gate = new();
    private List<GameProfile> _profiles;

    public event EventHandler? ProfilesChanged;

    public GameManager(ConfigStore store)
    {
        _store = store;
        _profiles = store.LoadGameProfiles();
    }

    public IReadOnlyList<GameProfile> GetAll() => _profiles;

    public GameProfile? GetById(string? id) => _profiles.FirstOrDefault(p => p.Id == id);

    public string Validate(GameProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return "El nombre del juego es obligatorio.";
        }

        // Se permite guardar un perfil sin servidores (p. ej. recién creado o solo para
        // detectar el proceso); la validación de uso ocurre al optimizar/diagnosticar.
        for (var i = 0; i < profile.Targets.Count; i++)
        {
            var target = profile.Targets[i];
            var host = !string.IsNullOrWhiteSpace(target.Domain)
                ? target.Domain!.Trim()
                : target.IpAddress?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                return $"El servidor objetivo {i + 1} no tiene dominio ni IP. Rellénalo o elimínalo.";
            }
        }

        return string.Empty;
    }

    public bool Save(GameProfile profile, out string error)
    {
        error = Validate(profile);
        if (error.Length > 0)
        {
            return false;
        }

        lock (_gate)
        {
            _store.SaveGameProfile(profile);
            _profiles = _store.LoadGameProfiles();
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            _store.DeleteGameProfile(id);
            _profiles.RemoveAll(p => p.Id == id);
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Exporta perfiles a JSON (no contiene secretos: los perfiles nunca los guardan).</summary>
    public static string ExportToJson(IEnumerable<GameProfile> profiles, bool indented = true)
    {
        return System.Text.Json.JsonSerializer.Serialize(profiles,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = indented });
    }

    public static List<GameProfile> ImportFromJson(string json, out List<string> warnings)
    {
        warnings = new List<string>();
        try
        {
            var profiles = System.Text.Json.JsonSerializer.Deserialize<List<GameProfile>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (profiles is null)
            {
                warnings.Add("JSON vacío o no es una lista de perfiles.");
                return new List<GameProfile>();
            }

            foreach (var profile in profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    profile.Id = Guid.NewGuid().ToString("N");
                }
            }

            return profiles;
        }
        catch (System.Text.Json.JsonException ex)
        {
            warnings.Add("JSON inválido: " + ex.Message);
            return new List<GameProfile>();
        }
    }

    // ---------- utilidades de proceso / detección ----------

    /// <summary>true si algún proceso en ejecución coincide con el perfil.</summary>
    public static bool IsGameRunning(GameProfile profile)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
            if (fileName.Length > 0)
            {
                names.Add(fileName);
            }
        }

        foreach (var n in profile.ExecutableNames)
        {
            var clean = n.Trim();
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean[..^4];
            }

            if (clean.Length > 0)
            {
                names.Add(clean);
            }
        }

        if (names.Count == 0)
        {
            return false;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.IsNullOrEmpty(process.ProcessName) && names.Contains(process.ProcessName))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Procesos sin acceso: se ignoran.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// Busca ejecutables instalados en rutas comunes (autorizado por el usuario).
    /// Devuelve candidatos (ruta, nombre) que existen realmente.
    /// </summary>
    public static List<InstallCandidate> FindInstalledExecutables(IEnumerable<string> exeNames, int maxResults = 25)
    {
        var results = new List<InstallCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = exeNames
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : n + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var dir in CommonInstallRoots())
        {
            if (results.Count >= maxResults)
            {
                break;
            }

            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                // Búsqueda en 2 niveles de profundidad para no escanear todo el disco.
                foreach (var file in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
                             .Where(f => IsShallowEnough(f, dir, depth: 3)))
                {
                    if (results.Count >= maxResults)
                    {
                        break;
                    }

                    var name = Path.GetFileName(file);
                    if (candidates.Contains(name, StringComparer.OrdinalIgnoreCase) && seen.Add(name))
                    {
                        results.Add(new InstallCandidate(file, name));
                    }
                }
            }
            catch (Exception)
            {
                // Carpetas sin permiso de lectura se omiten.
            }
        }

        return results;
    }

    /// <summary>Rutas típicas donde buscar juegos (no exhaustivo; nunca escanea el disco entero).</summary>
    public static IEnumerable<string> CommonInstallRoots()
    {
        var roots = new List<string>();
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf))
        {
            roots.Add(pf);
        }

        if (!string.IsNullOrEmpty(pf86) && !string.Equals(pf, pf86, StringComparison.OrdinalIgnoreCase))
        {
            roots.Add(pf86);
        }

        var steam = Path.Combine(pf86.Length > 0 ? pf86 : pf, "Steam", "steamapps", "common");
        if (Directory.Exists(steam))
        {
            roots.Add(steam);
        }

        var epic = Path.Combine(pf.Length > 0 ? pf : string.Empty, "Epic Games");
        if (Directory.Exists(epic))
        {
            roots.Add(epic);
        }

        return roots;
    }

    private static bool IsShallowEnough(string file, string root, int depth)
    {
        var rel = Path.GetRelativePath(root, file);
        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Count(s => s.Length > 0);
        return segments <= depth;
    }
}

/// <summary>Candidato de instalación encontrado por el escáner autorizado.</summary>
public sealed record InstallCandidate(string FullPath, string FileName);
