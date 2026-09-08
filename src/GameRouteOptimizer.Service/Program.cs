using System.Text;
using GameRouteOptimizer.Core;
using GameRouteOptimizer.Core.Privileged;

// GameRoute Optimizer — Servicio local de operaciones privilegiadas.
// Modo recomendado: se ejecuta ELEVADO (UAC o como tarea/administrador) y queda escuchando
// el named pipe 'gro.privileged.v1' para que la UI le pida operaciones de red.
// Uso:
//   GameRouteOptimizer.Service            (modo agente, equivalente a: agent)
//   GameRouteOptimizer.Service agent
//   GameRouteOptimizer.Service run-op <base64JSON>   (ejecución de una sola operación)
//   GameRouteOptimizer.Service version

Console.OutputEncoding = Encoding.UTF8;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "agent";

switch (command)
{
    case "version":
        Console.WriteLine($"{ProductInfo.Name} Service {ProductInfo.Version}");
        return 0;

    case "run-op" when args.Length >= 2:
    {
        PrivilegedOp? op;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));
            op = System.Text.Json.JsonSerializer.Deserialize<PrivilegedOp>(json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Payload inválido: " + ex.Message);
            return 2;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await new OpExecutor().ExecuteAsync(op!, cts.Token);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        return result.Ok ? 0 : 1;
    }

    case "agent":
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("El servicio usa named pipes de Windows.");
            return 2;
        }

        Console.WriteLine($"{ProductInfo.Name} Service {ProductInfo.Version} — agente elevado");
        Console.WriteLine("Escuchando en 'gro.privileged.v1'. Ctrl+C para salir.");
        await using var server = new OpPipeServer(new OpExecutor());
        server.ClientConnected += (_, _) => Console.WriteLine("Cliente conectado.");
        server.Error += (_, ex) => Console.Error.WriteLine("Error en el agente: " + ex.Message);
        server.Start();

        using var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.Set();
        };
        done.Wait();
        await server.DisposeAsync();
        Console.WriteLine("Agente detenido.");
        return 0;
    }

    default:
        Console.Error.WriteLine("Uso: GameRouteOptimizer.Service [agent|run-op <base64JSON>|version]");
        return 2;
}
