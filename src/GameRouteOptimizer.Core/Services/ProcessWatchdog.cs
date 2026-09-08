using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Vigila el lanzamiento/cierre de los juegos configurados (polling ligero cada 2 s).
/// Sirve para auto-start/auto-stop de la optimización.
/// </summary>
public sealed class ProcessWatchdog : IDisposable
{
    private readonly GameManager _gameManager;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _runningProfileIds = new(StringComparer.Ordinal);
    private Task? _loop;
    private bool _started;

    public event EventHandler<GameProcessEventArgs>? GameLaunched;
    public event EventHandler<GameProcessEventArgs>? GameExited;

    public bool Enabled { get; set; } = true;

    public ProcessWatchdog(GameManager gameManager)
    {
        _gameManager = gameManager;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _loop = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                if (!Enabled)
                {
                    continue;
                }

                var profiles = _gameManager.GetAll()
                    .Where(p => p.ExecutableNames.Count > 0 || !string.IsNullOrWhiteSpace(p.ExecutablePath))
                    .ToList();

                foreach (var profile in profiles)
                {
                    var running = GameManager.IsGameRunning(profile);
                    var knownRunning = _runningProfileIds.Contains(profile.Id);
                    if (running && !knownRunning)
                    {
                        _runningProfileIds.Add(profile.Id);
                        GameLaunched?.Invoke(this,
                            new GameProcessEventArgs(profile.Id, profile.Name, launched: true));
                    }
                    else if (!running && knownRunning)
                    {
                        _runningProfileIds.Remove(profile.Id);
                        GameExited?.Invoke(this,
                            new GameProcessEventArgs(profile.Id, profile.Name, launched: false));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // El watchdog nunca debe caerse: se ignora la pasada y se sigue.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // Ignorado.
        }

        _cts.Dispose();
    }
}

public sealed class GameProcessEventArgs : EventArgs
{
    public string ProfileId { get; }
    public string ProfileName { get; }
    public bool Launched { get; }

    public GameProcessEventArgs(string profileId, string profileName, bool launched)
    {
        ProfileId = profileId;
        ProfileName = profileName;
        Launched = launched;
    }
}
