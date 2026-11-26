using System.Diagnostics;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Profiles;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Game;

/// <summary>Estado de detección de un juego.</summary>
public sealed record GameDetection(string ProfileId, string ProcessName, bool IsRunning, string? ProcessPath);

/// <summary>
/// Gestor de juegos: detección de ejecución por nombre de proceso, detección de juegos
/// instalados en carpetas autorizadas por el usuario y vigilancia de arranque/cierre
/// para auto-start opcional. NO inyecta, NO lee memoria, NO interactúa con el proceso
/// del juego más allá de listar procesos del sistema (API estándar).
/// </summary>
public sealed class GameManager
{
    private readonly ProfileManager _profiles;
    private readonly Dictionary<string, bool> _runningCache = new();
    private readonly Func<IEnumerable<(string Name, string? Path)>> _processProvider;
    private CancellationTokenSource? _watchCts;

    public event Action<GameDetection>? GameStarted;
    public event Action<GameDetection>? GameExited;

    public GameManager(ProfileManager profiles) : this(profiles, DefaultProcessProvider) { }

    /// <summary>Constructor de pruebas: permite inyectar un proveedor de procesos.</summary>
    public GameManager(ProfileManager profiles, Func<IEnumerable<(string Name, string? Path)>> processProvider)
    {
        _profiles = profiles;
        _processProvider = processProvider;
    }

    private static IEnumerable<(string Name, string? Path)> DefaultProcessProvider()
        => Process.GetProcesses().Select(p =>
        {
            try { return (p.ProcessName, (string?)null); }
            catch { return (p.ProcessName, (string?)null); }
        });

    /// <summary>¿Está corriendo un ejecutable del perfil?</summary>
    public bool IsProfileRunning(GameProfile profile, out string matchedExecutable)
    {
        var names = profile.Executables
            .Concat(string.IsNullOrWhiteSpace(profile.ExecutablePath)
                ? Array.Empty<string>()
                : new[] { Path.GetFileNameWithoutExtension(profile.ExecutablePath) })
            .Select(n => n.Trim().ToLowerInvariant())
            .Where(n => n.Length > 0)
            .ToHashSet();

        matchedExecutable = string.Empty;
        foreach (var proc in _processProvider())
        {
            if (names.Contains(proc.Name.ToLowerInvariant()))
            {
                matchedExecutable = proc.Name;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Detecta juegos instalados escaneando carpetas autorizadas por el usuario buscando
    /// ejecutables que coincidan con los nombres de los perfiles existentes.
    /// Solo lectura de nombres de archivo; no se ejecuta nada del juego.
    /// </summary>
    public List<(string ProfileId, string ExecutablePath)> DetectInstalledGames(
        IEnumerable<string> roots, IEnumerable<GameProfile>? candidates = null, int maxDepth = 4)
    {
        var results = new List<(string, string)>();
        var profiles = (candidates ?? _profiles.Profiles).Where(p => p.IsEnabled).ToList();
        if (profiles.Count == 0)
        {
            return results;
        }

        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                var files = Directory.EnumerateFiles(root, "*.exe", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = maxDepth,
                    IgnoreInaccessible = true
                });
                foreach (var file in files)
                {
                    var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var match = profiles.FirstOrDefault(p =>
                        p.Executables.Any(e => e.Trim().ToLowerInvariant() == name));
                    if (match is not null && !results.Any(r => r.Item1 == match.Id))
                    {
                        results.Add((match.Id, file));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"No se pudo escanear '{root}': {ex.Message}");
            }
        }
        return results;
    }

    /// <summary>Inicia la vigilancia de arranque/cierre de juegos (para auto-start opcional).</summary>
    public void StartWatching(int intervalMs = 2000)
    {
        if (_watchCts is not null)
        {
            return;
        }

        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    foreach (var profile in _profiles.Profiles.Where(p => p.IsEnabled && p.Executables.Count > 0))
                    {
                        var running = IsProfileRunning(profile, out var matched);
                        var wasRunning = _runningCache.TryGetValue(profile.Id, out var prev) && prev;
                        if (running && !wasRunning)
                        {
                            _runningCache[profile.Id] = true;
                            GameStarted?.Invoke(new GameDetection(profile.Id, matched, true, profile.ExecutablePath));
                        }
                        else if (!running && wasRunning)
                        {
                            _runningCache[profile.Id] = false;
                            GameExited?.Invoke(new GameDetection(profile.Id, matched, false, profile.ExecutablePath));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Vigilancia de juegos: {ex.Message}");
                }
                try { await Task.Delay(intervalMs, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
        Log.Information("Vigilancia de juegos iniciada.");
    }

    public void StopWatching()
    {
        try { _watchCts?.Cancel(); } catch { }
        try { _watchCts?.Dispose(); } catch { }
        _watchCts = null;
    }
}
