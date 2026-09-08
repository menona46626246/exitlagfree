using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using GameRouteOptimizer.Core.Models;

namespace GameRouteOptimizer.Core.Services;

/// <summary>
/// Servicio de logs local (Serilog a archivo) con un buffer en memoria para la UI.
/// Sin telemetría ni salidas remotas.
/// </summary>
public sealed class LogService : IDisposable
{
    public const int RingBufferCapacity = 2000;

    private readonly object _gate = new();
    private readonly List<LogEntry> _ringBuffer = new();
    private readonly Serilog.Core.Logger? _fileLogger;
    private bool _disposed;

    public string? LogDirectory { get; }

    public event EventHandler<LogEntry>? EntryAdded;

    public LogService(string? logDirectory = null)
    {
        LogDirectory = logDirectory ?? DefaultLogDirectory();

        try
        {
            Directory.CreateDirectory(LogDirectory);
            var level = ToSerilogLevel(EventLevel.Information);
            _fileLogger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(new LoggingLevelSwitch(level))
                .Enrich.WithProperty("App", ProductInfo.Name)
                .WriteTo.File(
                    new Serilog.Formatting.Json.JsonFormatter(),
                    Path.Combine(LogDirectory, "gro-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 10,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(2))
                .CreateLogger();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LogService no pudo inicializar el archivo: {ex.Message}");
            _fileLogger = null;
        }
    }

    public static string DefaultLogDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(Path.GetTempPath(), "GameRouteOptimizer");
        }

        return Path.Combine(baseDir, "GameRouteOptimizer", "logs");
    }

    public void SetMinimumLevel(EventLevel level)
    {
        // En v1 el nivel se fija en la creación; este método queda para configuración en caliente futura.
    }

    public void Verbose(string message) => Write(EventLevel.Verbose, message);
    public void Debug(string message) => Write(EventLevel.Debug, message);
    public void Info(string message) => Write(EventLevel.Information, message);
    public void Warn(string message) => Write(EventLevel.Warning, message);
    public void Error(string message) => Write(EventLevel.Error, message);

    public void Write(EventLevel level, string message)
    {
        var entry = new LogEntry { Utc = DateTimeOffset.UtcNow, Level = level, Message = message };
        lock (_gate)
        {
            _ringBuffer.Add(entry);
            if (_ringBuffer.Count > RingBufferCapacity)
            {
                _ringBuffer.RemoveRange(0, _ringBuffer.Count - RingBufferCapacity);
            }
        }

        _fileLogger?.Write(ToSerilogLevel(level), message);
        try
        {
            EntryAdded?.Invoke(this, entry);
        }
        catch
        {
            // Los suscriptores de UI no deben romper el logging.
        }
    }

    public IReadOnlyList<LogEntry> GetRecent(int max = RingBufferCapacity)
    {
        lock (_gate)
        {
            var start = Math.Max(0, _ringBuffer.Count - max);
            return _ringBuffer.Skip(start).ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _ringBuffer.Clear();
        }
    }

    public string ExportAllText()
    {
        lock (_gate)
        {
            return string.Join(Environment.NewLine,
                _ringBuffer.Select(e =>
                    $"[{e.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}] {e.Level.ToString().ToUpperInvariant(),-11} {e.Message}"));
        }
    }

    public static LogEventLevel ToSerilogLevel(EventLevel level) => level switch
    {
        EventLevel.Verbose => LogEventLevel.Verbose,
        EventLevel.Debug => LogEventLevel.Debug,
        EventLevel.Warning => LogEventLevel.Warning,
        EventLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Information,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fileLogger?.Dispose();
    }
}
