using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.State;

/// <summary>
/// Máquina de estados del programa con transiciones explícitas, registro de cambios
/// y validación de cancelaciones. La lógica de red la ejecutan los suscriptores.
/// </summary>
public sealed class ProgramStateMachine
{
    private readonly object _gate = new();
    private ProgramState _state = ProgramState.Idle;

    /// <summary>Se dispara tras una transición válida (previo, nuevo, razón).</summary>
    public event EventHandler<StateChangedEventArgs>? StateChanged;

    /// <summary>Se dispara al intentar una transición no permitida.</summary>
    public event EventHandler<StateChangedEventArgs>? InvalidTransitionAttempted;

    public ProgramState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsActive =>
        State is ProgramState.Active or ProgramState.Monitoring or ProgramState.Degraded;

    public bool IsBusy =>
        State is ProgramState.DetectingGame or ProgramState.ProbingDirect or ProgramState.ProbingRelays
            or ProgramState.SelectingRoute or ProgramState.Connecting or ProgramState.Switching
            or ProgramState.FailingBack or ProgramState.Stopping;

    /// <summary>Solicita una transición. Devuelve false (sin cambios) si no está permitida.</summary>
    public bool TryTransition(ProgramState next, string reason)
    {
        lock (_gate)
        {
            var prev = _state;
            if (!IsTransitionAllowed(prev, next))
            {
                InvalidTransitionAttempted?.Invoke(this,
                    new StateChangedEventArgs(prev, next, reason, allowed: false));
                return false;
            }

            _state = next;
            var args = new StateChangedEventArgs(prev, next, reason, allowed: true);
            try
            {
                StateChanged?.Invoke(this, args);
            }
            catch
            {
                // Un suscriptor con error no debe romper la máquina.
            }

            return true;
        }
    }

    /// <summary>Transiciones cancelables (seguras para el usuario).</summary>
    public static bool IsCancelSafe(ProgramState state) =>
        state is ProgramState.Idle or ProgramState.DetectingGame or ProgramState.ProbingDirect
            or ProgramState.ProbingRelays or ProgramState.SelectingRoute or ProgramState.WaitingUser;

    /// <summary>Transiciones permitidas entre estados.</summary>
    public static bool IsTransitionAllowed(ProgramState from, ProgramState to) => (from, to) switch
    {
        (ProgramState.Idle, ProgramState.DetectingGame) => true,
        (ProgramState.Idle, ProgramState.ProbingDirect) => true,
        (ProgramState.Idle, ProgramState.Error) => true,

        // Detección de juego → probing directo o vuelta a Idle.
        (ProgramState.DetectingGame, ProgramState.ProbingDirect) => true,
        (ProgramState.DetectingGame, ProgramState.Idle) => true,
        (ProgramState.DetectingGame, ProgramState.Error) => true,

        // Probing.
        (ProgramState.ProbingDirect, ProgramState.ProbingRelays) => true,
        (ProgramState.ProbingDirect, ProgramState.SelectingRoute) => true,
        (ProgramState.ProbingDirect, ProgramState.Idle) => true,
        (ProgramState.ProbingDirect, ProgramState.Error) => true,
        (ProgramState.ProbingRelays, ProgramState.SelectingRoute) => true,
        (ProgramState.ProbingRelays, ProgramState.Idle) => true,
        (ProgramState.ProbingRelays, ProgramState.Error) => true,

        // Selección.
        (ProgramState.SelectingRoute, ProgramState.WaitingUser) => true,
        (ProgramState.SelectingRoute, ProgramState.Connecting) => true,
        (ProgramState.SelectingRoute, ProgramState.Idle) => true,
        (ProgramState.SelectingRoute, ProgramState.Error) => true,
        (ProgramState.WaitingUser, ProgramState.Connecting) => true,
        (ProgramState.WaitingUser, ProgramState.Idle) => true,
        (ProgramState.WaitingUser, ProgramState.Error) => true,

        // Conexión → activo/monitoreo, o fallo → error/failback.
        (ProgramState.Connecting, ProgramState.Active) => true,
        (ProgramState.Connecting, ProgramState.Monitoring) => true,
        (ProgramState.Connecting, ProgramState.Error) => true,
        (ProgramState.Connecting, ProgramState.FailingBack) => true,
        (ProgramState.Connecting, ProgramState.Idle) => true,

        (ProgramState.Active, ProgramState.Monitoring) => true,
        (ProgramState.Active, ProgramState.Switching) => true,
        (ProgramState.Active, ProgramState.Stopping) => true,
        (ProgramState.Active, ProgramState.Degraded) => true,
        (ProgramState.Active, ProgramState.Error) => true,
        (ProgramState.Active, ProgramState.Idle) => true,

        (ProgramState.Monitoring, ProgramState.Active) => true,
        (ProgramState.Monitoring, ProgramState.Connecting) => true,
        (ProgramState.Monitoring, ProgramState.Degraded) => true,
        (ProgramState.Monitoring, ProgramState.Switching) => true,
        (ProgramState.Monitoring, ProgramState.Stopping) => true,
        (ProgramState.Monitoring, ProgramState.Error) => true,
        (ProgramState.Monitoring, ProgramState.Idle) => true,

        (ProgramState.Degraded, ProgramState.Switching) => true,
        (ProgramState.Degraded, ProgramState.FailingBack) => true,
        (ProgramState.Degraded, ProgramState.Stopping) => true,
        (ProgramState.Degraded, ProgramState.Error) => true,
        (ProgramState.Degraded, ProgramState.Idle) => true,

        (ProgramState.Switching, ProgramState.Connecting) => true,
        (ProgramState.Switching, ProgramState.SelectingRoute) => true,
        (ProgramState.Switching, ProgramState.FailingBack) => true,
        (ProgramState.Switching, ProgramState.Error) => true,
        (ProgramState.Switching, ProgramState.Idle) => true,

        (ProgramState.FailingBack, ProgramState.Idle) => true,
        (ProgramState.FailingBack, ProgramState.ProbingDirect) => true,
        (ProgramState.FailingBack, ProgramState.Error) => true,
        (ProgramState.FailingBack, ProgramState.Stopping) => true,

        (ProgramState.Stopping, ProgramState.Idle) => true,
        (ProgramState.Stopping, ProgramState.Error) => true,

        (ProgramState.Error, ProgramState.Idle) => true,

        // Reintentos desde error son deliberados y explícitos.
        _ => false,
    };

    /// <summary>Etiqueta en español para la UI.</summary>
    public static string ToSpanish(ProgramState state) => state switch
    {
        ProgramState.Idle => "Inactivo",
        ProgramState.DetectingGame => "Detectando juego",
        ProgramState.ProbingDirect => "Midiendo ruta directa",
        ProgramState.ProbingRelays => "Midiendo relays",
        ProgramState.SelectingRoute => "Seleccionando mejor ruta",
        ProgramState.WaitingUser => "Esperando tu confirmación",
        ProgramState.Connecting => "Conectando túnel",
        ProgramState.Active => "Optimizado",
        ProgramState.Monitoring => "Monitoreando",
        ProgramState.Degraded => "Calidad degradada",
        ProgramState.Switching => "Cambiando de ruta",
        ProgramState.FailingBack => "Volviendo a ruta directa",
        ProgramState.Stopping => "Deteniendo",
        ProgramState.Error => "Error",
        _ => state.ToString(),
    };
}

public sealed class StateChangedEventArgs : EventArgs
{
    public ProgramState From { get; }
    public ProgramState To { get; }
    public string Reason { get; }
    public bool Allowed { get; }

    public StateChangedEventArgs(ProgramState from, ProgramState to, string reason, bool allowed)
    {
        From = from;
        To = to;
        Reason = reason;
        Allowed = allowed;
    }
}
