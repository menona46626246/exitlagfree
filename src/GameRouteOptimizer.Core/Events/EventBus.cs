using System.Collections.Concurrent;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Events;

/// <summary>Evento de aplicación publicado por el EventBus (para UI, logs y sesión).</summary>
public sealed class AppEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public EventSeverity Severity { get; init; } = EventSeverity.Info;

    public EventCategory Category { get; init; } = EventCategory.System;

    public string Message { get; init; } = string.Empty;

    /// <summary>Id de sesión asociada (si aplica).</summary>
    public string? SessionId { get; init; }

    /// <summary>Estado del optimizador en el momento del evento.</summary>
    public OptimizerState State { get; init; } = OptimizerState.Idle;

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] {Severity} {Category}: {Message}";
}

/// <summary>
/// Bus de eventos en memoria, thread-safe. Único canal de comunicación entre módulos
/// (core → UI/logs/sesión) para mantener bajo acoplamiento.
/// </summary>
public sealed class EventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Subscribe<T>(Action<T> handler)
    {
        var list = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
        lock (list)
        {
            list.Add(handler);
        }
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list))
        {
            return;
        }

        lock (list)
        {
            list.Remove(handler);
        }
    }

    public void Publish<T>(T evt)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list))
        {
            return;
        }

        Delegate[] snapshot;
        lock (list)
        {
            snapshot = list.ToArray();
        }

        foreach (var d in snapshot)
        {
            try
            {
                ((Action<T>)d)(evt);
            }
            catch
            {
                // Un handler defectuoso no debe tumbar el bus. Se ignora de forma deliberada:
                // los errores de handlers se registran donde corresponda (LogService escribe por su cuenta).
            }
        }
    }
}
