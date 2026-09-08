using System.Text;
using GameRouteOptimizer.Core;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Privileged;
using GameRouteOptimizer.Core.Probing;

// GameRoute Optimizer — CLI (diagnóstico sin UI + ejecutor elevado de operaciones).
// Uso:
//   diag <host> [--port N] [--probe icmp|tcp|udp|http] [--count N] [--deep] [--url URL]
//   trace <host> [--max-hops N]
//   run-op <base64(json PrivilegedOp)>     (proceso elevado con UAC; interno)
//   agent                                   (servidor IPC elevado por named pipes)
//   version

return await CliMain.RunAsync(args);

static class CliMain
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "diag" => await DiagAsync(args[1..]),
                "trace" => await TraceAsync(args[1..]),
                "run-op" => await RunOpAsync(args[1..]),
                "agent" => await RunAgentAsync(),
                "version" => PrintVersion(),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => Unknown(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operación cancelada.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"{ProductInfo.Name} {ProductInfo.Version}");
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine($"{ProductInfo.Name} {ProductInfo.Version} — línea de comandos");
        Console.WriteLine();
        Console.WriteLine("Uso:");
        Console.WriteLine("  GameRouteOptimizer.Cli diag <host> [--port N] [--probe icmp|tcp|udp|http] [--count N] [--deep] [--url URL]");
        Console.WriteLine("      Mide latencia/pérdida/jitter hacia un host autorizado.");
        Console.WriteLine("  GameRouteOptimizer.Cli trace <host> [--max-hops N]");
        Console.WriteLine("      Traceroute simplificado.");
        Console.WriteLine("  GameRouteOptimizer.Cli run-op <base64JSON>   (uso interno elevado)");
        Console.WriteLine("  GameRouteOptimizer.Cli agent                 (servidor IPC elevado)");
        Console.WriteLine("  GameRouteOptimizer.Cli version");
        return 0;
    }

    private static int Unknown(string arg)
    {
        Console.Error.WriteLine($"Comando desconocido: {arg}");
        return 2;
    }

    private static async Task<int> DiagAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("Uso: diag <host> [--port N] [--probe icmp|tcp|udp|http] [--count N] [--deep] [--url URL]");
            return 2;
        }

        var host = args[0];
        var port = 443;
        var probe = ProbeKind.Icmp;
        var count = 5;
        var deep = false;
        var url = (string?)null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    port = int.Parse(args[++i]);
                    break;
                case "--count" when i + 1 < args.Length:
                    count = int.Parse(args[++i]);
                    break;
                case "--probe" when i + 1 < args.Length:
                    probe = args[++i].ToLowerInvariant() switch
                    {
                        "tcp" or "tcpconnect" => ProbeKind.TcpConnect,
                        "udp" => ProbeKind.Udp,
                        "http" or "https" or "httpget" => ProbeKind.HttpGet,
                        _ => ProbeKind.Icmp,
                    };
                    break;
                case "--url" when i + 1 < args.Length:
                    url = args[++i];
                    break;
                case "--deep":
                    deep = true;
                    break;
            }
        }

        if (probe == ProbeKind.HttpGet && url is null)
        {
            url = $"https://{host}/";
        }

        var settings = new ProbingSettings
        {
            QuickProbeCount = count,
            DeepProbeCount = count * 4,
            TimeoutMs = 1500,
        };
        var transport = new SystemProbeTransport();
        var engine = new ProbeEngine(transport, settings);

        Console.WriteLine($"Probando {host} ({probe}, {count} intentos)…");
        var spec = new ProbeTargetSpec
        {
            Label = host,
            Host = host,
            Kind = probe,
            Port = port,
            HttpUrl = url,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await engine.ProbeAsync(spec, deep, cts.Token);

        var s = result.Summary;
        Console.WriteLine();
        Console.WriteLine("Resultado:");
        Console.WriteLine($"  Destino:     {s.TargetLabel}");
        Console.WriteLine($"  Tipo:        {s.Kind}");
        Console.WriteLine($"  Intentos:    {s.Attempts}  (éxitos {s.Successes}, fallos {s.Failures})");
        Console.WriteLine(s.AvgMs.HasValue ? $"  Latencia:    media {s.AvgMs:F1} ms | mín {s.MinMs:F1} | máx {s.MaxMs:F1}" : "  Latencia:    sin datos");
        Console.WriteLine(s.P95Ms.HasValue ? $"  Percentiles: p95 {s.P95Ms:F1} ms | p99 {s.P99Ms:F1} ms" : string.Empty);
        Console.WriteLine(s.JitterMs.HasValue ? $"  Jitter:      {s.JitterMs:F1} ms" : string.Empty);
        Console.WriteLine(s.StdDevMs.HasValue ? $"  Estabilidad: σ = {s.StdDevMs:F1} ms" : string.Empty);
        Console.WriteLine(s.LossPercent.HasValue ? $"  Pérdida:     {s.LossPercent:F1} %" : string.Empty);
        if (!s.IcmpReliable)
        {
            Console.WriteLine("  Aviso: ICMP no fiable (bloqueado); se usó TCP connect.");
        }

        if (!string.IsNullOrWhiteSpace(s.UnavailableReason))
        {
            Console.WriteLine($"  No utilizable: {s.UnavailableReason}");
        }

        return s.Usable ? 0 : 3;
    }

    private static async Task<int> TraceAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Uso: trace <host> [--max-hops N]");
            return 2;
        }

        var host = args[0];
        var maxHops = 30;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--max-hops" && i + 1 < args.Length)
            {
                maxHops = int.Parse(args[++i]);
            }
        }

        var transport = new SystemProbeTransport();
        var traceroute = new TracerouteEngine(transport);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        Console.WriteLine($"Traceroute hacia {host}…");
        var result = await traceroute.TraceAsync(host, cts.Token, maxHops: maxHops);

        Console.WriteLine();
        Console.WriteLine($"{"salto",5}  {"dirección",-16} {"rtt1",8} {"rtt2",8}  estado");
        foreach (var hop in result.Hops)
        {
            var addr = hop.Address ?? "*";
            var r1 = hop.RttMs1.HasValue ? $"{hop.RttMs1:F0} ms" : "-";
            var r2 = hop.RttMs2.HasValue ? $"{hop.RttMs2:F0} ms" : "-";
            var state = hop.Timeouts == 0 ? "ok" : $"{hop.Timeouts} sin respuesta";
            Console.WriteLine($"{hop.Ttl,5}  {addr,-16} {r1,8} {r2,8}  {state}");
        }

        Console.WriteLine();
        Console.WriteLine(result.Note);
        foreach (var obs in result.Observations)
        {
            Console.WriteLine("  ⚠ " + obs);
        }

        return result.Completed ? 0 : 4;
    }

    private static async Task<int> RunOpAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Uso: run-op <base64JSON>");
            return 2;
        }

        PrivilegedOp? op;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[0]));
            op = System.Text.Json.JsonSerializer.Deserialize<PrivilegedOp>(json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Payload inválido: " + ex.Message);
            return 2;
        }

        if (op is null)
        {
            Console.Error.WriteLine("Payload vacío.");
            return 2;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var executor = new OpExecutor();
        var result = await executor.ExecuteAsync(op, cts.Token);

        var resultJson = System.Text.Json.JsonSerializer.Serialize(result);
        var resultFile = Environment.GetEnvironmentVariable("GRO_RESULT_FILE");
        if (!string.IsNullOrWhiteSpace(resultFile))
        {
            // Modo UAC: la UI lee el resultado desde este archivo.
            await File.WriteAllTextAsync(resultFile, resultJson, Encoding.UTF8);
        }
        else
        {
            Console.WriteLine(resultJson);
        }

        return result.Ok ? 0 : 1;
    }

    private static async Task<int> RunAgentAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("El modo agente usa named pipes de Windows.");
            return 2;
        }

        Console.WriteLine("Agente GameRoute Optimizer escuchando en el pipe 'gro.privileged.v1'…");
        Console.WriteLine("Ctrl+C para salir.");
        await using var server = new OpPipeServer(new OpExecutor());
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
}
