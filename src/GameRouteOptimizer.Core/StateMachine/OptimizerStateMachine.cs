using GameRouteOptimizer.Core.Events;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.StateMachine;

/// <summary>
/// Máquina de estados del optimizador. Toda transición:
/// - se valida contra la tabla de transiciones permitidas;
/// - se registra en logs y se publica en el EventBus;
/// - ejecuta el action asociado con soporte de cancelación;
/// - ante fallo ejecuta la ruta de restauración (Stopping → Idle o Error).
/// </summary>
public sealed class OptimizerStateMachine
{
    private static readonly Dictionary<OptimizerState, OptimizerState[]> AllowedTransitions = new()
    {
        [OptimizerState.Idle] = [OptimizerState.DetectingGame, OptimizerState.ProbingDirect, OptimizerState.Connecting, OptimizerState.WaitingUser, OptimizerState.Error],
        [OptimizerState.DetectingGame] = [OptimizerState.ProbingDirect, OptimizerState.Idle, OptimizerState.Error],
        [OptimizerState.ProbingDirect] = [OptimizerState.ProbingRelays, OptimizerState.SelectingRoute, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.ProbingRelays] = [OptimizerState.SelectingRoute, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.SelectingRoute] = [OptimizerState.WaitingUser, OptimizerState.Connecting, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.WaitingUser] = [OptimizerState.Connecting, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.Connecting] = [OptimizerState.Active, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.Active] = [OptimizerState.Monitoring, OptimizerState.Switching, OptimizerState.Degraded, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.Monitoring] = [OptimizerState.Switching, OptimizerState.Degraded, OptimizerState.FailingBack, OptimizerState.Stopping, OptimizerState.Active, OptimizerState.Error],
        [OptimizerState.Degraded] = [OptimizerState.Monitoring, OptimizerState.FailingBack, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.Switching] = [OptimizerState.Monitoring, OptimizerState.Active, OptimizerState.FailingBack, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.FailingBack] = [OptimizerState.Monitoring, OptimizerState.Stopping, OptimizerState.Error],
        [OptimizerState.Stopping] = [OptimizerState.Idle, OptimizerState.Error],
        [OptimizerState.Error] = [OptimizerState.Idle, OptimizerState.Stopping]
    };

    private readonly EventBus _events;
    private readonly object _gate = new();
    private OptimizerState _state = OptimizerState.Idle;

    public OptimizerStateMachine(EventBus events)
    {
        _events = events;
    }

    public OptimizerState Current
    {
        get { lock (_gate)
            {
                return _state;
            }
        }
    }

    public event Action<OptimizerState, OptimizerState>? StateChanged;

    public bool CanTransitionTo(OptimizerState next)
        => AllowedTransitions[Current].Contains(next);

    /// <summary>
    /// Transiciona al estado siguiente ejecutando `action` (si se provee).
    /// Si action lanza, se restaura: Stopping→Idle o Error según gravedad.
    /// Devuelve true si la transición se completó.
    /// </summary>
    public async Task<bool> TransitionToAsync(OptimizerState next, Func<CancellationToken, Task>? action = null, CancellationToken ct = default)
    {
        var previous = Current;
        if (!CanTransitionTo(next))
        {
            Log.Warning($"Transición de estado rechazada: {previous} → {next} no permitida.");
            return false;
        }

        lock (_gate)
        {
            _state = next;
        }

        Log.Information($"Estado: {previous} → {next}");
        _events.Publish(new AppEvent
        {
            Severity = EventSeverity.Info,
            Category = EventCategory.System,
            Message = $"Estado: {Translate(previous)} → {Translate(next)}",
            State = next
        });
        StateChanged?.Invoke(previous, next);

        if (action is null)
        {
            return true;
        }

        try
        {
            await action(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Cancelación segura: restaurar.
            Log.Information($"Operación cancelada en estado {next}; restaurando.");
            if (CanTransitionTo(OptimizerState.Stopping))
            {
                await TransitionToAsync(OptimizerState.Stopping, async token =>
                {
                    RestoreRequested?.Invoke(this, new OperationCanceledException("Cancelado por el usuario."));
                    await Task.CompletedTask;
                });
                if (Current == OptimizerState.Stopping)
                {
                    await TransitionToAsync(OptimizerState.Idle);
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Fallo en estado {next}: {ex.Message}", ex);
            // Prioridad: restaurar la red (Stopping → Idle); Error solo si no hay ruta de restauración.
            RestoreRequested?.Invoke(this, ex);
            if (CanTransitionTo(OptimizerState.Stopping))
            {
                await TransitionToAsync(OptimizerState.Stopping);
                if (Current == OptimizerState.Stopping)
                {
                    await TransitionToAsync(OptimizerState.Idle);
                }
            }
            else if (CanTransitionTo(OptimizerState.Error))
            {
                await TransitionToAsync(OptimizerState.Error);
            }
            return false;
        }
    }

    /// <summary>Transición síncrona simple (sin acción).</summary>
    public bool TransitionTo(OptimizerState next) => TransitionToAsync(next).GetAwaiter().GetResult();

    /// <summary>Solicitado cuando una transición falla y hay que restaurar la red.</summary>
    public event EventHandler<Exception>? RestoreRequested;

    public static string Translate(OptimizerState state) => state switch
    {
        OptimizerState.Idle => "Inactivo",
        OptimizerState.DetectingGame => "Detectando juego",
        OptimizerState.ProbingDirect => "Midiendo ruta directa",
        OptimizerState.ProbingRelays => "Midiendo relays",
        OptimizerState.SelectingRoute => "Seleccionando ruta",
        OptimizerState.WaitingUser => "Esperando decisión del usuario",
        OptimizerState.Connecting => "Conectando túnel",
        OptimizerState.Active => "Optimizando",
        OptimizerState.Monitoring => "Monitoreando",
        OptimizerState.Degraded => "Calidad degradada",
        OptimizerState.Switching => "Cambiando de ruta",
        OptimizerState.FailingBack => "Regresando a ruta directa",
        OptimizerState.Stopping => "Deteniendo y restaurando",
        OptimizerState.Error => "Error",
        _ => state.ToString()
    };
}
