using System.Diagnostics;
using System.Text;

namespace GameRouteOptimizer.Core.Privileged;

/// <summary>
/// Ejecuta operaciones privilegiadas lanzando el helper elevado (GameRouteOptimizer.Cli run-op)
/// con UAC (Verb=runas). El helper ejecuta la operación y devuelve el resultado JSON por stdout.
/// La UI nunca corre elevada.
/// </summary>
public sealed class CliElevatedRunner : IPrivilegedOps
{
    private readonly string _helperExePath;

    public CliElevatedRunner(string? helperExePath = null)
    {
        // Por defecto, el helper es el CLI junto al ejecutable actual.
        _helperExePath = helperExePath ?? GuessHelperPath();
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    private static string GuessHelperPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "GameRouteOptimizer.Cli.exe");
        return File.Exists(candidate) ? candidate : baseDir;
    }

    public async Task<PrivilegedOpResult> RunAsync(PrivilegedOp op, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return PrivilegedOpResult.Failure("La elevación solo está disponible en Windows.");
        }

        var opJson = System.Text.Json.JsonSerializer.Serialize(op);
        var psi = new ProcessStartInfo
        {
            FileName = _helperExePath,
            Arguments = "run-op \"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(opJson)) + "\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        // runas no permite redirigir stdout; el helper escribe el resultado a un archivo temporal.
        var resultFile = Path.Combine(Path.GetTempPath(), $"gro-op-{Guid.NewGuid():N}.json");
        try
        {
            psi.Environment["GRO_RESULT_FILE"] = resultFile;
            using var process = new Process { StartInfo = psi };
            process.Start();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (!File.Exists(resultFile))
            {
                return process.ExitCode == 0
                    ? PrivilegedOpResult.Failure("El usuario canceló la elevación (UAC).")
                    : PrivilegedOpResult.Failure($"El helper elevado falló (código {process.ExitCode}).");
            }

            var json = await File.ReadAllTextAsync(resultFile, ct).ConfigureAwait(false);
            var result = System.Text.Json.JsonSerializer.Deserialize<PrivilegedOpResult>(json);
            return result ?? PrivilegedOpResult.Failure("Respuesta del helper vacía.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC cancelado o helper no encontrado.
            return PrivilegedOpResult.Failure("Elevación cancelada o helper no disponible (UAC).");
        }
        finally
        {
            try
            {
                if (File.Exists(resultFile))
                {
                    File.Delete(resultFile);
                }
            }
            catch (Exception)
            {
                // Limpieza best-effort.
            }
        }
    }
}
