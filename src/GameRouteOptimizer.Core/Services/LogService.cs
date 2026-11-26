using System.Collections.ObjectModel;
using System.Text;

namespace GameRouteOptimizer.Core.Services;

/// <summary>Nivel de log.</summary>
public enum LogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3
}

/// <summary>Entrada de log (para ring buffer de UI).</summary>
public sealed record LogEntry(LogLevel Level, DateTimeOffset Timestamp, string Message)
{
    public override string ToString() =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level.ToString().ToUpperInvariant()}] {Message}";
}

/// <summary>
/// Servicio de logging estilo Serilog (D2): sink de archivo con rollover diario
/// + ring buffer en memoria para la UI. Sin dependencias externas.
/// API: Log.Information("..."), etc. a través de la instancia global <see cref="Log"/>.
/// </summary>
public sealed class LogService
{
    private static readonly object Gate = new();
    private static LogService? _default;

    private readonly string? _logDirectory;
    private readonly LogLevel _minLevel;
    private readonly int _ringCapacity;
    private readonly ObservableCollection<LogEntry> _ring = new();
    private DateTime _currentFileDate;
    private string? _currentFilePath;

    public event Action<LogEntry>? EntryAdded;

    public LogService(string? logDirectory, LogLevel minLevel = LogLevel.Information, int ringCapacity = 1000)
    {
        _logDirectory = logDirectory;
        _minLevel = minLevel;
        _ringCapacity = ringCapacity;
        if (_logDirectory is not null)
        {
            try { Directory.CreateDirectory(_logDirectory); } catch { /* sin logs de archivo */ }
        }
    }

    public static LogService Default => _default ??= new LogService(null);

    public static void SetDefault(LogService service) => _default = service;

    public ObservableCollection<LogEntry> RingBuffer { get { lock (Gate) { return _ring; } } }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (Gate)
        {
            return _ring.ToArray();
        }
    }

    public void Write(LogLevel level, string message)
    {
        if (level < _minLevel)
        {
            return;
        }

        var entry = new LogEntry(level, DateTimeOffset.Now, message);

        lock (Gate)
        {
            _ring.Add(entry);
            while (_ring.Count > _ringCapacity)
            {
                _ring.RemoveAt(0);
            }
        }

        WriteToFile(entry);
        try { EntryAdded?.Invoke(entry); } catch { /* handler aislado */ }
    }

    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Information(string message) => Write(LogLevel.Information, message);
    public void Warning(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);
    public void Error(string message, Exception ex) => Write(LogLevel.Error, $"{message}: {ex.GetType().Name}: {ex.Message}");

    private void WriteToFile(LogEntry entry)
    {
        if (_logDirectory is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                var today = entry.Timestamp.Date;
                if (_currentFilePath is null || _currentFileDate != today)
                {
                    _currentFileDate = today;
                    _currentFilePath = Path.Combine(_logDirectory, $"gameroute-{today:yyyyMMdd}.log");
                }
                File.AppendAllText(_currentFilePath, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // La escritura de logs nunca debe lanzar excepciones a la aplicación.
        }
    }

    /// <summary>Exporta el ring buffer a un archivo de texto; devuelve la ruta o null.</summary>
    public string? ExportTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"gameroute-export-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllLines(path, Snapshot().Select(e => e.ToString()), Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        lock (Gate)
        {
            _ring.Clear();
        }
    }
}

/// <summary>Fachada estática estilo Serilog.</summary>
public static class Log
{
    public static LogService Instance { get; set; } = LogService.Default;

    public static void Debug(string m) => Instance.Debug(m);
    public static void Information(string m) => Instance.Information(m);
    public static void Warning(string m) => Instance.Warning(m);
    public static void Error(string m) => Instance.Error(m);
    public static void Error(string m, Exception ex) => Instance.Error(m, ex);
}
