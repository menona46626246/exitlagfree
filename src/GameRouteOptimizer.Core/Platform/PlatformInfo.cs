using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace GameRouteOptimizer.Core.Platform;

/// <summary>Información y capacidades de la plataforma actual.</summary>
public static class PlatformInfo
{
    [SupportedOSPlatformGuard("windows")]
    public static bool IsWindows => OperatingSystem.IsWindows();

    [SupportedOSPlatformGuard("linux")]
    public static bool IsLinux => OperatingSystem.IsLinux();

    [SupportedOSPlatformGuard("macos")]
    public static bool IsMacOS => OperatingSystem.IsMacOS();

    /// <summary>¿Se está ejecutando con privilegios elevados (admin/root)?</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                if (IsWindows)
                {
                    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                if (IsLinux || IsMacOS)
                {
                    return GetEuid() == 0;
                }
            }
            catch
            {
                return false;
            }
            return false;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint getuid();

    private static uint GetEuid() => IsWindows ? uint.MaxValue : getuid();

    /// <summary>¿Existe el binario en PATH?</summary>
    public static bool CommandExists(string command)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        var exts = IsWindows ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        foreach (var dir in new[] { AppContext.BaseDirectory }.Concat(paths))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim('"'), command + ext);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Carpeta de datos por defecto (%APPDATA% en Windows, XDG_DATA_HOME o ~/.local/share en Linux).</summary>
    public static string DefaultDataDirectory()
    {
        if (IsWindows)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "GameRouteOptimizer");
        }
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "GameRouteOptimizer");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "GameRouteOptimizer");
    }
}

/// <summary>Resultado de ejecutar un comando externo.</summary>
public sealed record CommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Ejecuta procesos externos de forma segura: sin shell, argumentos separados, timeout
/// y salida capturada. Se usa para herramientas oficiales (wireguard.exe, wg, wg-quick,
/// netsh, route, ip). Nunca interpola cadenas del usuario en un shell.
/// </summary>
public static class CommandRunner
{
    public static CommandResult Run(string fileName, IEnumerable<string> args, int timeoutMs = 15000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi };
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) { sbOut.AppendLine(e.Data); } };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) { sbErr.AppendLine(e.Data); } };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (proc.WaitForExit(timeoutMs))
            {
                proc.WaitForExit(2000); // drenar async output
                return new CommandResult(proc.ExitCode, sbOut.ToString(), sbErr.ToString());
            }
            try { proc.Kill(true); } catch { /* el proceso ya pudo terminar */ }
            return new CommandResult(-1, sbOut.ToString(), "Timeout al ejecutar el proceso.");
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, sbOut.ToString(), ex.Message);
        }
    }

    public static async Task<CommandResult> RunAsync(string fileName, IEnumerable<string> args, int timeoutMs = 15000, CancellationToken ct = default)
        => await Task.Run(() => Run(fileName, args, timeoutMs), ct);
}
