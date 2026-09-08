using System.Security.Cryptography;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Services;

/// <summary>Abstracción de protección de secretos (claves privadas WireGuard).</summary>
public interface ISecretProtector
{
    /// <summary>true cuando la protección real está disponible en esta plataforma.</summary>
    bool IsAvailable { get; }

    /// <summary>Cifra texto plano y devuelve base64. Lanza si no está disponible.</summary>
    string ProtectToBase64(string plainText);

    /// <summary>Descifra base64 a texto plano. Lanza si no está disponible o falla.</summary>
    string UnprotectFromBase64(string protectedBase64);
}

/// <summary>
/// Protector con DPAPI de Windows (CurrentUser). En otras plataformas no está disponible:
/// en ese caso las claves privadas se mantienen solo en memoria y nunca se persisten.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public string ProtectToBase64(string plainText)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException(
                "DPAPI solo está disponible en Windows. La clave no se persistirá.");
        }

        var bytes = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plainText),
            optionalEntropy: System.Text.Encoding.UTF8.GetBytes("GameRouteOptimizer v1"),
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public string UnprotectFromBase64(string protectedBase64)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("DPAPI solo está disponible en Windows.");
        }

        var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
            Convert.FromBase64String(protectedBase64),
            optionalEntropy: System.Text.Encoding.UTF8.GetBytes("GameRouteOptimizer v1"),
            DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}

/// <summary>Servicio de notificaciones en la UI (en memoria).</summary>
public sealed class NotificationService
{
    private readonly object _gate = new();
    private readonly List<AppNotification> _notifications = new();
    public const int MaxRetained = 200;

    public event EventHandler<AppNotification>? NotificationAdded;

    public void Notify(EventLevel level, string title, string message)
    {
        var n = new AppNotification { Level = level, Title = title, Message = message };
        lock (_gate)
        {
            _notifications.Add(n);
            if (_notifications.Count > MaxRetained)
            {
                _notifications.RemoveRange(0, _notifications.Count - MaxRetained);
            }
        }

        NotificationAdded?.Invoke(this, n);
    }

    public void Info(string title, string message) => Notify(EventLevel.Information, title, message);
    public void Warn(string title, string message) => Notify(EventLevel.Warning, title, message);
    public void Error(string title, string message) => Notify(EventLevel.Error, title, message);

    public IReadOnlyList<AppNotification> GetRecent(int max = 100)
    {
        lock (_gate)
        {
            var start = Math.Max(0, _notifications.Count - max);
            return _notifications.Skip(start).ToList();
        }
    }
}
