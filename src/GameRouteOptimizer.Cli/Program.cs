using GameRouteOptimizer.Core.Ipc;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Profiles;
using GameRouteOptimizer.Core.Relay;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Scoring;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.Cli;

/// <summary>
/// CLI multiplataforma de GameRoute Optimizer.
/// Comandos: probe, trace, score, relay, profile, tunnel-check, service-status.
/// Útil en servidores/Windows sin UI y para verificar el entorno.
/// </summary>
public static class Program
{
    private static DataStore _store = null!;
    private static ProfileManager _profiles = null!;
    private static RelayManager _relays = null!;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var dataDir = Environment.GetEnvironmentVariable("GAMEROUTE_DATA") ?? PlatformInfo.DefaultDataDirectory();
        var store = new DataStore(dataDir);
        _store = store;
        _profiles = new ProfileManager(store);
        _relays = new RelayManager(store, new SecretProtector(dataDir));

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "probe" => await CmdProbeAsync(args[1..]),
                "trace" => await CmdTraceAsync(args[1..]),
                "score" => await CmdScoreAsync(args[1..]),
                "relay" => await CmdRelayAsync(args[1..]),
                "profile" => CmdProfile(args[1..]),
                "tunnel-check" => CmdTunnelCheck(args[1..]),
                "service-status" => await CmdServiceStatusAsync(),
                "--help" or "help" => HelpAndReturn(),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int HelpAndReturn() { PrintHelp(); return 0; }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Comando desconocido: {cmd}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            GameRoute Optimizer CLI — diagnóstico y verificación de rutas

            Uso: gameroute <comando> [opciones]

            Comandos:
              probe  --host <host> [--protocol auto|icmp|tcp|udp|http] [--port N] [--mode quick|deep]
              trace  --host <host> [--max-hops N]
              score  --host <host> [--port N] [--mode quick|deep]      (compara directa vs relays guardados)
              relay  list | add | test <id> | remove <id> | import-wg <archivo.conf>
              profile list | import <archivo.json> | export <id> <archivo.json> | validate <id>
              tunnel-check --conf <archivo.conf>                        (valida sin aplicar)
              service-status                                            (consulta el servicio por IPC)

            Notas:
              - Solo se realizan pruebas a endpoints definidos por ti.
              - El túnel se gestiona con el servicio elevado (installer/install-service.ps1).
            """);
    }

    // ---------- probe ----------

    private static async Task<int> CmdProbeAsync(string[] args)
    {
        var host = GetOption(args, "--host") ?? throw new ArgumentException("Falta --host.");
        var protocol = ParseEnum<ProbeProtocol>(GetOption(args, "--protocol") ?? "auto");
        var mode = ParseEnum<ProbeMode>(GetOption(args, "--mode") ?? "quick");
        var port = int.TryParse(GetOption(args, "--port"), out var p) ? p : 80;

        var target = new GameServer { Name = host, Domain = host, Ip = host, PreferredProtocol = protocol };
        if (protocol == ProbeProtocol.Tcp || protocol == ProbeProtocol.Udp)
        {
            target.Ports.Add(port);
        }

        Console.WriteLine($"Probe → {host} (protocolo {protocol}, modo {mode})…");
        var engine = new ProbeEngine();
        var summary = await engine.ProbeAsync(target, mode);
        PrintSummary(summary);
        return summary.IsReachable ? 0 : 2;
    }

    private static void PrintSummary(ProbeSummary s)
    {
        Console.WriteLine($"  Objetivo:      {s.Target}");
        Console.WriteLine($"  Protocolo:     {s.ProtocolUsed}");
        Console.WriteLine($"  Probes:        {s.TotalProbes} (fallidas: {s.FailedProbes})");
        Console.WriteLine($"  Media:         {s.MeanMs:0.0} ms");
        Console.WriteLine($"  p50/p95/p99:   {s.P50Ms:0.0} / {s.P95Ms:0.0} / {s.P99Ms:0.0} ms");
        Console.WriteLine($"  Min/Max:       {s.MinMs:0.0} / {s.MaxMs:0.0} ms");
        Console.WriteLine($"  Pérdida:       {s.LossRatio:P1}");
        Console.WriteLine($"  Jitter:        {s.JitterMs:0.0} ms");
        Console.WriteLine($"  Estabilidad:   {s.StabilityScore:0}/100");
        if (!string.IsNullOrWhiteSpace(s.Notes))
        {
            Console.WriteLine($"  Nota:          {s.Notes}");
        }
    }

    // ---------- trace ----------

    private static async Task<int> CmdTraceAsync(string[] args)
    {
        var host = GetOption(args, "--host") ?? throw new ArgumentException("Falta --host.");
        var maxHops = int.TryParse(GetOption(args, "--max-hops"), out var h) ? h : 20;
        Console.WriteLine($"Traceroute → {host}…");
        var tracer = new TracerouteEngine(maxHops: maxHops);
        var result = await tracer.TraceAsync(host);
        Console.WriteLine($"  Método: {result.Method}");
        foreach (var hop in result.Hops)
        {
            var times = hop.Responded ? string.Join("  ", hop.RttSamplesMs.Select(t => $"{t:0.0} ms")) : "*";
            var flag = hop.SuspectedBottleneck ? "  ← posible cuello de botella" : "";
            Console.WriteLine($"  {hop.Hop,3}  {hop.Address,-16}  {times}{flag}");
        }
        Console.WriteLine($"  → {result.Explain()}");
        return 0;
    }

    // ---------- score ----------

    private static async Task<int> CmdScoreAsync(string[] args)
    {
        var host = GetOption(args, "--host") ?? throw new ArgumentException("Falta --host.");
        var mode = ParseEnum<ProbeMode>(GetOption(args, "--mode") ?? "quick");
        var relays = _relays.Relays;
        var profile = _profiles.Profiles.FirstOrDefault() ?? new GameProfile { Name = "CLI" };
        profile.Servers = [new GameServer { Name = host, Domain = host, Ip = host }];

        var analyzer = new RouteAnalyzer(new ProbeEngine());
        Console.WriteLine("Comparando ruta directa vs relays (puede tardar)…");
        var rec = await analyzer.CompareAsync(profile, profile.Servers[0], relays, mode);
        foreach (var (candidate, score) in rec.Candidates.Zip(rec.AllScores))
        {
            var state = score.Excluded ? $"EXCLUIDO ({score.ExclusionReason})" : $"score {score.Value:0.0}/100 (confianza {score.Confidence:P0})";
            Console.WriteLine($"  {candidate.DisplayName,-24} {candidate.LatencyMs,8:0.0} ms  pérdida {candidate.LossRatio,6:P1}  {state}");
        }
        Console.WriteLine();
        Console.WriteLine($"Recomendación: {(rec.Best?.DisplayName ?? "ninguna")} — {rec.SimpleExplanation}");
        return 0;
    }

    // ---------- relay ----------

    private static async Task<int> CmdRelayAsync(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("Uso: relay list|add|test <id>|remove <id>|import-wg <archivo.conf>"); return 1; }
        switch (args[0].ToLowerInvariant())
        {
            case "list":
                foreach (var r in _relays.Relays)
                {
                    Console.WriteLine($"  {r.Id[..8]}  {r.Name,-20} {r.EndpointHost}:{r.EndpointPort,-22} {r.Health,-8} {(r.IsEnabled ? "activo" : "inactivo")}");
                }

                if (_relays.Relays.Count == 0)
                {
                    Console.WriteLine("  (sin relays)");
                }

                return 0;

            case "add":
                {
                    var name = Prompt("Nombre: ");
                    var host = Prompt("Endpoint host/IP: ");
                    var port = int.TryParse(Prompt("Puerto [51820]: "), out var pp) ? pp : 51820;
                    var pub = Prompt("Clave pública del peer: ");
                    var relay = new RelayNode { Name = name, EndpointHost = host, EndpointPort = port, PublicKey = pub };
                    var errors = relay.Validate();
                    if (errors.Count > 0) { Console.WriteLine("  Errores: " + string.Join(" ", errors)); return 1; }
                    _relays.Add(relay);
                    Console.WriteLine($"  Relay añadido: {relay.Id[..8]}");
                    return 0;
                }

            case "test":
                {
                    var relay = FindRelay(args.ElementAtOrDefault(1));
                    Console.WriteLine($"  Midiendo '{relay.Name}'…");
                    var prober = new RelayProber(new ProbeEngine());
                    var result = await prober.TestRelayAsync(relay);
                    Console.WriteLine($"  Alcanzable: {result.Reachable} · media {result.MeanMs:0.0} ms · pérdida {result.LossRatio:P1}");
                    Console.WriteLine($"  {result.Note}");
                    _relays.Update(relay);
                    return 0;
                }

            case "remove":
                _relays.Remove(FindRelay(args.ElementAtOrDefault(1)).Id);
                Console.WriteLine("  Relay eliminado.");
                return 0;

            case "import-wg":
                {
                    var path = args.ElementAtOrDefault(1) ?? throw new ArgumentException("Falta la ruta del .conf");
                    var content = File.ReadAllText(path);
                    var relay = _relays.ImportWireGuardConfig(content, Path.GetFileNameWithoutExtension(path));
                    Console.WriteLine($"  Relay importado: {relay.Id[..8]} {relay.Name} (clave privada cifrada con {_ = ""}{new SecretProtector(Environment.GetEnvironmentVariable("GAMEROUTE_DATA") ?? PlatformInfo.DefaultDataDirectory()).Mechanism}).");
                    return 0;
                }
        }
        return 1;
    }

    private static RelayNode FindRelay(string? idPrefix)
    {
        if (string.IsNullOrWhiteSpace(idPrefix))
        {
            throw new ArgumentException("Falta el id del relay.");
        }

        var relay = _relays.Relays.FirstOrDefault(r => r.Id.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase))
                    ?? throw new KeyNotFoundException($"No existe relay con id que empiece por '{idPrefix}'.");
        return relay;
    }

    // ---------- profile ----------

    private static int CmdProfile(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("Uso: profile list|import <archivo>|export <id> <archivo>|validate <id>"); return 1; }
        switch (args[0].ToLowerInvariant())
        {
            case "list":
                foreach (var p in _profiles.Profiles)
                {
                    Console.WriteLine($"  {p.Id[..8]}  {p.Name,-24} servidores: {p.Servers.Count}");
                }

                if (_profiles.Profiles.Count == 0)
                {
                    Console.WriteLine("  (sin perfiles)");
                }

                return 0;

            case "import":
                {
                    var json = File.ReadAllText(args.ElementAtOrDefault(1) ?? throw new ArgumentException("Falta el archivo"));
                    var profile = _profiles.ImportProfileJson(json);
                    Console.WriteLine($"  Perfil importado: {profile.Id[..8]} {profile.Name}");
                    return 0;
                }

            case "export":
                {
                    var id = args.ElementAtOrDefault(1) ?? throw new ArgumentException("Falta el id");
                    var file = args.ElementAtOrDefault(2) ?? $"{id}.json";
                    var profile = _profiles.Profiles.FirstOrDefault(p => p.Id.StartsWith(id))
                                  ?? throw new KeyNotFoundException("Perfil no encontrado.");
                    File.WriteAllText(file, _profiles.ExportProfileJson(profile));
                    Console.WriteLine($"  Exportado a {file}");
                    return 0;
                }

            case "validate":
                {
                    var profile = _profiles.Profiles.FirstOrDefault(p => p.Id.StartsWith(args.ElementAtOrDefault(1) ?? ""))
                                  ?? throw new KeyNotFoundException("Perfil no encontrado.");
                    var errors = profile.Validate();
                    Console.WriteLine(errors.Count == 0 ? "  Perfil válido." : "  Errores: " + string.Join(" ", errors));
                    return errors.Count == 0 ? 0 : 1;
                }
        }
        return 1;
    }

    // ---------- tunnel-check ----------

    private static int CmdTunnelCheck(string[] args)
    {
        var path = GetOption(args, "--conf") ?? throw new ArgumentException("Falta --conf.");
        var content = File.ReadAllText(path);
        try
        {
            var tunnel = WireGuardConfigParser.Parse(content);
            var errors = WireGuardConfigParser.Validate(tunnel);
            if (errors.Count > 0)
            {
                Console.WriteLine("Configuración INVÁLIDA:");
                foreach (var e in errors)
                {
                    Console.WriteLine($"  - {e}");
                }

                return 1;
            }
            Console.WriteLine("Configuración VÁLIDA.");
            Console.WriteLine(tunnel.ToConfString(includePrivateKey: false));
            Console.WriteLine("(clave privada enmascarada: nunca se muestra en la salida)");
            return 0;
        }
        catch (WireGuardConfigException ex)
        {
            Console.WriteLine($"Configuración INVÁLIDA: {ex.Message}");
            return 1;
        }
    }

    // ---------- service-status ----------

    private static async Task<int> CmdServiceStatusAsync()
    {
        var client = new NamedPipeIpcClient("gameroute-optimizer-svc");
        var response = await client.SendAsync(new IpcMessage(IpcMessage.Ping, new { }));
        if (response is null)
        {
            Console.WriteLine("Servicio no disponible. Instálalo con: installer/install-service.ps1 (Windows, como admin).");
            return 2;
        }
        Console.WriteLine($"Servicio: {response.Payload}");
        return 0;
    }

    // ---------- helpers ----------

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: true, out var v)
            ? v
            : throw new ArgumentException($"Valor inválido para {typeof(T).Name}: {value}");

    private static string Prompt(string label)
    {
        Console.Write(label);
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }
}
