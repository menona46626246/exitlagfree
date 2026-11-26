using GameRouteOptimizer.Core.Events;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Services;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Servicio de notificaciones: recibe AppEvents y los expone a la UI (lista de eventos
/// recientes). No usa toasts del SO nianything externo; la UI muestra las notificaciones.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly EventBus _events;
    private readonly List<AppEvent> _recent = new();
    private readonly object _gate = new();
    private const int MaxRecent = 200;

    public event Action<AppEvent>? NotificationArrived;

    public NotificationService(EventBus events)
    {
        _events = events;
        _events.Subscribe<AppEvent>(OnEvent);
    }

    private void OnEvent(AppEvent evt)
    {
        lock (_gate)
        {
            _recent.Add(evt);
            if (_recent.Count > MaxRecent)
            {
                _recent.RemoveAt(0);
            }
        }
        try { NotificationArrived?.Invoke(evt); } catch { /* UI aislada */ }
    }

    public IReadOnlyList<AppEvent> Recent
    {
        get { lock (_gate)
            {
                return _recent.ToArray();
            }
        }
    }

    public void Info(string message) => _events.Publish(new AppEvent { Severity = EventSeverity.Info, Message = message });
    public void Success(string message) => _events.Publish(new AppEvent { Severity = EventSeverity.Success, Message = message });
    public void Warning(string message) => _events.Publish(new AppEvent { Severity = EventSeverity.Warning, Message = message });
    public void Error(string message) => _events.Publish(new AppEvent { Severity = EventSeverity.Error, Message = message });

    public void Dispose()
    {
        _events.Unsubscribe<AppEvent>(OnEvent);
    }
}
