using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using GameRouteOptimizer.Core;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Scoring;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.State;
using GameRouteOptimizer.Core.Storage;
using Xunit;

namespace GameRouteOptimizer.Tests;

/// <summary>
/// Pruebas del núcleo: almacenamiento, gestión de juegos/relays, métricas, scoring,
/// cálculo de rutas y máquina de estados. Sin red externa ni dependencias del sistema.
/// </summary>
public class CoreTests
{
    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), "gro-tests-" + Guid.NewGuid().ToString("N") + ".db");

    private static ConfigStore NewStore(out string dbPath)
    {
        dbPath = NewDbPath();
        return new ConfigStore(dbPath);
    }

    private static void DeleteDb(string dbPath)
    {
        try
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            foreach (var suffix in new[] { "-shm", "-wal" })
            {
                var side = dbPath + suffix;
                if (File.Exists(side))
                {
                    File.Delete(side);
                }
            }
        }
        catch (IOException)
        {
            // Windows puede tardar en soltar el archivo; se ignora en tests.
        }
    }

    private static GameProfile SampleGame() => new()
    {
        Name = "Juego de prueba",
        ExecutableNames = { "juego.exe" },
        Targets =
        {
            new GameServerTarget { Domain = "server.ejemplo.com", Ports = { 27015 }, Region = "EU-Oeste" },
        },
        RouteMode = RouteMode.TunnelGameDestinations,
        AutoStartOptimization = true,
    };

    private static ProbeSummary Summary(double avgMs, double lossPct, double jitterMs, bool usable = true)
    {
        var s = new ProbeSummary
        {
            TargetLabel = "x",
            Kind = ProbeKind.TcpConnect,
            StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-2),
            FinishedUtc = DateTimeOffset.UtcNow,
            Attempts = 10,
            Successes = usable && lossPct < 100 ? 10 - (int)Math.Round(lossPct / 10) : 0,
            Failures = usable ? (int)Math.Round(lossPct / 10) : 10,
            MinMs = avgMs - 5,
            AvgMs = avgMs,
            MaxMs = avgMs + 5,
            P95Ms = avgMs + 4,
            P99Ms = avgMs + 6,
            JitterMs = jitterMs,
            StdDevMs = jitterMs / 2,
            LossPercent = lossPct,
        };
        return s;
    }

    [Fact]
    public void ConfigStore_RoundTrip_JuegosRelaysSesionYConfiguracion()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var game = SampleGame();
            store.SaveGameProfile(game);
            var loaded = store.LoadGameProfiles();
            Assert.Contains(loaded, g => g.Id == game.Id && g.Name == game.Name);
            Assert.Equal("server.ejemplo.com", loaded.First(g => g.Id == game.Id).Targets[0].Domain);

            var relay = new RelayNode { Name = "Relay prueba", EndpointHost = "10.0.0.1", EndpointPort = 51820 };
            store.SaveRelay(relay);
            Assert.Contains(store.LoadRelays(), r => r.Id == relay.Id && r.EndpointPort == 51820);

            var session = new SessionRecord
            {
                GameProfileName = "Juego de prueba",
                TargetDisplay = "server.ejemplo.com",
            };
            session.AddEvent(SessionEventCategory.Info, "inicio");
            store.SaveSession(session);
            var sessions = store.LoadSessions(50);
            Assert.Contains(sessions, s => s.Id == session.Id && s.Events.Count == 1);

            var settings = new AppSettings();
            settings.Probing.QuickProbeCount = 7;
            settings.AutoSwitch.MinImprovementMs = 25;
            store.SaveSettings(settings);
            var reloaded = store.LoadSettings();
            Assert.Equal(7, reloaded.Probing.QuickProbeCount);
            Assert.Equal(25, reloaded.AutoSwitch.MinImprovementMs);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void GameManager_CRUD_Y_RoundTripJson()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var manager = new GameManager(store);
            var game = SampleGame();
            Assert.True(manager.Save(game, out _));
            Assert.NotNull(manager.GetById(game.Id));

            var json = GameManager.ExportToJson(manager.GetAll());
            Assert.Contains("server.ejemplo.com", json, StringComparison.Ordinal);

            var imported = GameManager.ImportFromJson(json, out var warnings);
            Assert.Empty(warnings);
            var copy = Assert.Single(imported);
            Assert.Equal(game.Name, copy.Name);
            Assert.Equal(game.Targets[0].Domain, copy.Targets[0].Domain);

            manager.Delete(game.Id);
            Assert.Null(manager.GetById(game.Id));
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void RelayManager_ImportConf_GeneraRelayCompleto()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var manager = new RelayManager(store, new NoOpProtector());
            const string conf = """
                [Interface]
                Address = 10.66.0.2/32
                DNS = 1.1.1.1
                PrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=

                [Peer]
                PublicKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                Endpoint = relay.ejemplo.com:51820
                AllowedIPs = 0.0.0.0/0, ::/0
                PersistentKeepalive = 25
                """;

            var parsed = GameRouteOptimizer.Core.Tunneling.WireGuardConfigParser.Parse(conf);
            Assert.True(parsed.Ok, "parse .conf: " + string.Join(" | ", parsed.Errors));

            var relay = manager.ImportFromConfigText(conf, null, out var warnings);
            Assert.True(relay is not null, "import .conf: " + string.Join(" | ", warnings));
            Assert.Equal("relay.ejemplo.com", relay!.EndpointHost);
            Assert.Equal(51820, relay.EndpointPort);
            Assert.Equal("1.1.1.1", relay.DnsInternal);
            Assert.True(relay.HasEndpoint);
            Assert.Contains(relay.TunnelAddresses, a => a.StartsWith("10.66.0.2", StringComparison.Ordinal));
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void RelayManager_ExportJson_NuncaIncluyeClavePrivada()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var manager = new RelayManager(store, new NoOpProtector());
            var relay = new RelayNode
            {
                Name = "R1",
                EndpointHost = "10.0.0.2",
                EndpointPort = 51820,
                PublicKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            };
            Assert.True(manager.Save(relay, out var saveError), "Save relay: " + saveError);
            var json = RelayManager.ExportToJson(manager.GetAll());
            Assert.DoesNotContain("PrivateKey", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Privada", json, StringComparison.OrdinalIgnoreCase);

            var imported = RelayManager.ImportFromJson(json, out var warnings);
            Assert.Empty(warnings);
            var copy = Assert.Single(imported);
            Assert.Equal(relay.Id, copy.Id);
            Assert.Null(copy.PrivateKeyPlain);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void ProbeSummaryCalculator_AgregaExitosYPerdida()
    {
        var attempts = new List<ProbeReply>
        {
            ProbeReply.Ok(20),
            ProbeReply.Ok(22),
            ProbeReply.Ok(25),
            ProbeReply.Ok(24),
            ProbeReply.Fail(GameRouteOptimizer.Core.Probing.ProbeFailureReason.Timeout),
        };
        var summary = ProbeSummaryCalculator.Compute(
            "server.ejemplo.com", ProbeKind.Icmp, attempts,
            DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);

        Assert.Equal(5, summary.Attempts);
        Assert.Equal(4, summary.Successes);
        Assert.Equal(1, summary.Failures);
        Assert.NotNull(summary.LossPercent);
        Assert.Equal(20.0, summary.LossPercent!.Value);
        Assert.NotNull(summary.AvgMs);
        Assert.InRange(summary.AvgMs!.Value, 20d, 25d);
        Assert.NotNull(summary.MinMs);
        Assert.InRange(summary.MinMs!.Value, 19d, 21d);
    }

    [Fact]
    public void ScoreEngine_RecomiendaRelaySoloSiMejoraClaramente()
    {
        var options = new ScoringOptions { MinImprovementMs = 10 };
        var candidates = new List<CandidateMeasurement>
        {
            new()
            {
                CandidateId = "direct",
                DisplayName = "Ruta directa",
                Kind = RouteCandidateKind.Direct,
                Measured = Summary(120, 0, 3),
            },
            new()
            {
                CandidateId = "relay-bueno",
                DisplayName = "Relay bueno",
                Kind = RouteCandidateKind.Relay,
                RelayId = "r1",
                Measured = Summary(55, 0, 5),
                Priority = 1,
            },
        };

        var output = ScoreEngine.Evaluate(candidates, options, currentId: "direct");
        Assert.Equal("relay-bueno", output.WinnerId);
        Assert.True(output.ChangeRecommended);
        Assert.InRange(output.Confidence, 0.0, 1.0);
        Assert.False(string.IsNullOrWhiteSpace(output.ExplanationEs));

        // Relay peor que la directa: nunca se recomienda cambiarse.
        candidates[1].Measured = Summary(200, 0, 5);
        var output2 = ScoreEngine.Evaluate(candidates, options, currentId: "direct");
        Assert.Equal("direct", output2.WinnerId);
        Assert.False(output2.ChangeRecommended);
    }

    [Fact]
    public void ScoreEngine_ExcluyeCandidatoConPerdidaExtrema()
    {
        var options = new ScoringOptions { MinImprovementMs = 10 };
        var candidates = new List<CandidateMeasurement>
        {
            new()
            {
                CandidateId = "direct",
                DisplayName = "Directa",
                Kind = RouteCandidateKind.Direct,
                Measured = Summary(150, 0, 2),
            },
            new()
            {
                CandidateId = "relay-malo",
                DisplayName = "Relay con 40% pérdida",
                Kind = RouteCandidateKind.Relay,
                RelayId = "r2",
                Measured = Summary(40, 40, 8),
            },
        };

        var output = ScoreEngine.Evaluate(candidates, options, currentId: "direct");
        Assert.Equal("direct", output.WinnerId);
        var scored = Assert.Single(output.Ranked, c => c.CandidateId == "relay-malo");
        Assert.True(scored.Excluded);
    }

    [Fact]
    public void RouteCalculator_PlanGlobal_Y_SoloDestinos()
    {
        // Global: todo el tráfico, con rutas espejo reversibles.
        var global = RouteCalculator.BuildPlan(
            RouteMode.TunnelGlobal,
            Array.Empty<string>(),
            Array.Empty<string>(),
            "gro0");
        Assert.Contains("0.0.0.0/0", global.AllowedIps);
        Assert.Contains("::/0", global.AllowedIps);
        Assert.Equal(global.RoutesToAdd.Count, global.RoutesToDeleteOnStop.Count);
        Assert.True(global.RoutesToAdd.Count >= 2);

        // Solo destinos: encamina las IPs resueltas (IPv4), nunca más de lo pedido.
        var game = RouteCalculator.BuildPlan(
            RouteMode.TunnelGameDestinations,
            new[] { "server.ejemplo.com", "server.ejemplo.com" },
            new[] { "203.0.113.7", "no-es-ip" },
            "gro0");
        Assert.Contains("203.0.113.7/32", game.AllowedIps);
        Assert.Contains(game.RoutesToAdd, r => r.Prefix == "203.0.113.7/32" && r.InterfaceName == "gro0");
        Assert.DoesNotContain(game.AllowedIps, a => a.Contains("no-es-ip", StringComparison.Ordinal));
        Assert.Contains(game.Warnings, w => w.Contains("server.ejemplo.com", StringComparison.Ordinal));
        Assert.True(game.NeedsTunnelRoutes);
    }

    [Fact]
    public void RouteCalculator_CoberturaIpv4()
    {
        Assert.True(RouteCalculator.Ipv4IsCovered("8.8.8.8", new[] { "0.0.0.0/0" }));
        Assert.True(RouteCalculator.Ipv4IsCovered("10.1.2.3", new[] { "10.0.0.0/8", "192.168.0.0/16" }));
        Assert.False(RouteCalculator.Ipv4IsCovered("11.1.2.3", new[] { "10.0.0.0/8" }));
    }

    [Fact]
    public void ProgramStateMachine_SoloPermiteTransicionesValidas()
    {
        var fsm = new ProgramStateMachine();
        Assert.Equal(ProgramState.Idle, fsm.State);

        Assert.True(fsm.TryTransition(ProgramState.ProbingDirect, "test"));
        Assert.Equal(ProgramState.ProbingDirect, fsm.State);

        Assert.True(fsm.TryTransition(ProgramState.ProbingRelays, "test"));
        Assert.True(fsm.TryTransition(ProgramState.SelectingRoute, "test"));
        Assert.True(fsm.TryTransition(ProgramState.WaitingUser, "test"));
        Assert.True(fsm.TryTransition(ProgramState.Connecting, "test"));
        Assert.True(fsm.TryTransition(ProgramState.Active, "test"));

        // De Idle no se puede saltar directo a Active; y desde Active no a ProbingDirect.
        Assert.False(new ProgramStateMachine().TryTransition(ProgramState.Active, "inválido"));
        Assert.False(fsm.TryTransition(ProgramState.ProbingDirect, "inválido desde activo"));
        Assert.False(string.IsNullOrWhiteSpace(ProgramStateMachine.ToSpanish(ProgramState.Idle)));
    }

    // ================= pruebas de probes y del motor =================

    [Fact]
    public void ProbeSummaryCalculator_JitterUsaOrdenDeLlegada()
    {
        // Serie alternada 1,100,1,100,1: el jitter real (diferencias consecutivas) es ~99 ms.
        var attempts = new List<ProbeReply>
        {
            ProbeReply.Ok(1), ProbeReply.Ok(100), ProbeReply.Ok(1), ProbeReply.Ok(100), ProbeReply.Ok(1),
        };
        var summary = ProbeSummaryCalculator.Compute(
            "srv", ProbeKind.Icmp, attempts, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);

        Assert.NotNull(summary.JitterMs);
        Assert.InRange(summary.JitterMs!.Value, 98.5, 99.5);
        Assert.Equal(0, summary.LossPercent);
        Assert.NotNull(summary.P95Ms);
        Assert.Equal(100, summary.P95Ms!.Value); // percentil sobre valores ordenados
    }

    [Fact]
    public void ProbeSummaryCalculator_P95InterpolaEntreMuestras()
    {
        var attempts = new List<ProbeReply>();
        foreach (var ms in new[] { 10, 20, 30, 40, 50 })
        {
            attempts.Add(ProbeReply.Ok(ms));
        }

        var summary = ProbeSummaryCalculator.Compute(
            "srv", ProbeKind.Icmp, attempts, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow);
        Assert.NotNull(summary.P95Ms);
        Assert.InRange(summary.P95Ms!.Value, 47.5, 48.5); // rank 3.8 → 48
        Assert.Equal(50, summary.MaxMs);
        Assert.Equal(10, summary.MinMs);
    }

    [Fact]
    public async Task ProbeEngine_FallbackAutomaticoACuandoIcmpBloqueado()
    {
        var fake = new FakeProbeTransport
        {
            IcmpReply = ProbeReply.Fail(GameRouteOptimizer.Core.Probing.ProbeFailureReason.IcmpBlocked, "bloqueado"),
            TcpReply = ProbeReply.Ok(12),
        };
        var settings = FastSettings();
        var engine = new ProbeEngine(fake, settings);

        var result = await engine.ProbeAsync(
            new ProbeTargetSpec { Label = "srv", Host = "127.0.0.1", Kind = ProbeKind.Icmp },
            deep: false, CancellationToken.None);

        Assert.Equal(ProbeKind.TcpConnect, result.Summary.Kind);
        Assert.False(result.Summary.IcmpReliable);
        Assert.True(result.Summary.Usable);
        Assert.Equal(settings.QuickProbeCount, fake.TcpCalls);
        Assert.Equal(settings.QuickProbeCount, fake.IcmpCalls);
        Assert.NotNull(result.Summary.Note);
        Assert.Contains("TCP connect", result.Summary.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeEngine_ConIcmpDeshabilitadoUsaTcpDirectamente()
    {
        var fake = new FakeProbeTransport { TcpReply = ProbeReply.Ok(9) };
        var settings = FastSettings();
        settings.UseIcmp = false;
        var engine = new ProbeEngine(fake, settings);

        var result = await engine.ProbeAsync(
            new ProbeTargetSpec { Label = "srv", Host = "127.0.0.1", Kind = ProbeKind.Icmp },
            deep: false, CancellationToken.None);

        Assert.Equal(ProbeKind.TcpConnect, result.Summary.Kind);
        Assert.Equal(0, fake.IcmpCalls);
        Assert.Equal(settings.QuickProbeCount, fake.TcpCalls);
        Assert.True(result.Summary.Usable);
    }

    [Fact]
    public async Task ProbeEngine_DnsFallidoDaResumenLimpioSinRed()
    {
        // Resolver simulado que siempre falla: cubre el camino de "dominio inválido / DNS caído"
        // de forma determinista y sin red externa.
        var fake = new FakeProbeTransport();
        var engine = new ProbeEngine(fake, FastSettings(), resolver: new EmptyResolver());

        var result = await engine.ProbeAsync(
            new ProbeTargetSpec { Label = "servidor-inexistente", Host = "no-resuelve.ejemplo", Kind = ProbeKind.Icmp },
            deep: false, CancellationToken.None);

        Assert.False(result.Summary.Usable);
        Assert.False(result.Summary.HasData);
        Assert.Equal(0, fake.IcmpCalls);
        Assert.NotNull(result.Summary.UnavailableReason);
        Assert.Contains("resolver", result.Summary.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeEngine_CanceladoAntesDeEmpezarDevuelveResumenLimpio()
    {
        var fake = new FakeProbeTransport();
        var engine = new ProbeEngine(fake, FastSettings());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await engine.ProbeAsync(
            new ProbeTargetSpec { Label = "srv", Host = "127.0.0.1", Kind = ProbeKind.Icmp },
            deep: false, cts.Token);

        Assert.False(result.Summary.HasData);
        Assert.Equal(0, fake.IcmpCalls);
    }

    /// <summary>Resolver determinista sin DNS para tests del motor de probes.</summary>
    private sealed class EmptyResolver : EndpointResolver
    {
        public override Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(Array.Empty<IPAddress>());
    }

    private static ProbingSettings FastSettings() => new()
    {
        QuickProbeCount = 3,
        DeepProbeCount = 3,
        IntervalMs = 1,
        TimeoutMs = 500,
    };

    // ================= perfiles: guardado flexible + preflight =================

    [Fact]
    public void GameManager_PerfilSinServidoresSeGuardaYElPreflightLoRechaza()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var manager = new GameManager(store);
            var draft = new GameProfile { Name = "Borrador sin servidores" };
            Assert.True(manager.Save(draft, out _));
            Assert.NotNull(manager.GetById(draft.Id));

            // Optimizar/diagnosticar sí exige un servidor objetivo utilizable.
            var problem = OptimizationOrchestrator.FindStartProblem(draft);
            Assert.False(string.IsNullOrWhiteSpace(problem));
            Assert.Contains("no tiene servidores objetivo", problem, StringComparison.OrdinalIgnoreCase);

            draft.Targets.Add(new GameServerTarget { Domain = "srv.ejemplo.com" });
            Assert.True(manager.Save(draft, out _));
            Assert.Equal(string.Empty, OptimizationOrchestrator.FindStartProblem(draft));

            // Un servidor sin dominio ni IP se rechaza al guardar con mensaje claro.
            var bad = new GameProfile { Name = "Juego roto" };
            bad.Targets.Add(new GameServerTarget());
            Assert.False(manager.Save(bad, out var error));
            Assert.Contains("servidor objetivo 1", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    // ================= relays: guardado en progreso y solo-medición =================

    [Fact]
    public void RelayManager_RelaySoloMedicionSeGuardaYConClavesValidasConecta()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var manager = new RelayManager(store, new NoOpProtector());

            // Solo-medición: sin clave pública ni privada es válido guardar.
            var relay = new RelayNode { Name = "Relay medible", EndpointHost = "relay.ejemplo.com", EndpointPort = 51820 };
            Assert.True(manager.Save(relay, out var saveError), saveError);
            Assert.False(manager.HasUsablePrivateKey(relay));

            // Clave pública con formato inválido → error claro.
            var relayMal = new RelayNode { Name = "Relay malo", EndpointHost = "r.ejemplo.com", EndpointPort = 51820, PublicKey = "no-es-una-clave" };
            Assert.False(manager.Save(relayMal, out var malError));
            Assert.Contains("clave pública", malError, StringComparison.OrdinalIgnoreCase);

            // Clave privada válida en memoria (sin DPAPI en tests: solo memoria).
            Assert.False(manager.SetPrivateKey(relay, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="));
            Assert.True(manager.HasUsablePrivateKey(relay));
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    // ================= SQLite: tolerancia a corrupción =================

    [Fact]
    public void ConfigStore_SettingsCorruptasDevuelvenDefaultsYSobrevivenAlGuardado()
    {
        var store = NewStore(out var dbPath);
        try
        {
            // Corromper la fila de configuración directamente en SQLite.
            using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText = "INSERT INTO Settings(key, json) VALUES('app', '{esto-no-es-json');";
                cmd.ExecuteNonQuery();
            }

            // LoadSettings no lanza y devuelve valores por defecto completos.
            var loaded = store.LoadSettings();
            Assert.Equal(5, loaded.Probing.QuickProbeCount);
            Assert.NotNull(loaded.AutoSwitch);
            Assert.NotNull(loaded.Tunnel);
            Assert.NotNull(loaded.Logging);

            // Tras guardar, la configuración queda íntegra de nuevo.
            loaded.Probing.QuickProbeCount = 11;
            store.SaveSettings(loaded);
            Assert.Equal(11, store.LoadSettings().Probing.QuickProbeCount);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void ConfigStore_FilaDePerfilCorruptaSeIgnoraSinRomperLaCarga()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var good = SampleGame();
            store.SaveGameProfile(good);

            using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText =
                    "INSERT INTO GameProfiles(id, name, json, updated_utc) VALUES('fila-rota', 'roto', '{json-roto', '2026-01-01T00:00:00Z');";
                cmd.ExecuteNonQuery();
            }

            var profiles = store.LoadGameProfiles();
            Assert.Contains(profiles, p => p.Id == good.Id);
            Assert.DoesNotContain(profiles, p => p.Id == "fila-rota");
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public void ConfigStore_SettingsConSubobjetosNullSeSanean()
    {
        var store = NewStore(out var dbPath);
        try
        {
            using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText = "INSERT INTO Settings(key, json) VALUES('app', '{\"Probing\":null,\"AutoSwitch\":null,\"Tunnel\":null,\"Logging\":null}');";
                cmd.ExecuteNonQuery();
            }

            var loaded = store.LoadSettings();
            Assert.NotNull(loaded.Probing);
            Assert.NotNull(loaded.AutoSwitch);
            Assert.NotNull(loaded.Tunnel);
            Assert.NotNull(loaded.Logging);
            Assert.Equal(5, loaded.Probing.QuickProbeCount);
            Assert.False(loaded.AutoSwitch.Enabled);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    // ================= scoring: histéresis y penalizaciones =================

    [Fact]
    public void ScoreEngine_NoCambiaPorMejorasMinimasNiConJitterAlto()
    {
        var options = new ScoringOptions { MinImprovementMs = 10 };
        var direct = new CandidateMeasurement
        {
            CandidateId = "direct",
            DisplayName = "Ruta directa",
            Kind = RouteCandidateKind.Direct,
            Measured = Summary(120, 0, 3),
        };

        // Mejora mínima (5 ms) < histéresis (10 ms) + tramo final desconocido → no cambiar.
        var relay = new CandidateMeasurement
        {
            CandidateId = "relay:a",
            DisplayName = "Relay marginal",
            Kind = RouteCandidateKind.Relay,
            RelayId = "a",
            Measured = Summary(115, 0, 3),
        };
        var output = ScoreEngine.Evaluate(new[] { direct, relay }, options, currentId: "direct");
        Assert.False(output.ChangeRecommended);

        // Relay con jitter extremo (100 ms) se puntúa peor que la directa.
        var relayJitter = new CandidateMeasurement
        {
            CandidateId = "relay:b",
            DisplayName = "Relay inestable",
            Kind = RouteCandidateKind.Relay,
            RelayId = "b",
            Measured = Summary(100, 0, 100),
        };
        var output2 = ScoreEngine.Evaluate(new[] { direct, relayJitter }, options, currentId: "direct");
        Assert.Equal("direct", output2.WinnerId);
        Assert.False(output2.ChangeRecommended);
    }

    [Fact]
    public void ScoreEngine_RelaySinMedicionesQuedaExcluido()
    {
        var options = new ScoringOptions();
        var direct = new CandidateMeasurement
        {
            CandidateId = "direct",
            DisplayName = "Ruta directa",
            Kind = RouteCandidateKind.Direct,
            Measured = Summary(80, 0, 2),
        };
        var relay = new CandidateMeasurement
        {
            CandidateId = "relay:x",
            DisplayName = "Relay sin medir",
            Kind = RouteCandidateKind.Relay,
            RelayId = "x",
            Measured = null,
        };

        var output = ScoreEngine.Evaluate(new[] { direct, relay }, options, currentId: "direct");
        Assert.Equal("direct", output.WinnerId);
        var scored = Assert.Single(output.Ranked, c => c.CandidateId == "relay:x");
        Assert.True(scored.Excluded);
        Assert.Contains("Sin mediciones", scored.ExcludeReason, StringComparison.Ordinal);
    }

    private sealed class FakeProbeTransport : IProbeTransport
    {
        public string Description => "transporte simulado";

        public ProbeReply IcmpReply { get; set; } = ProbeReply.Ok(20);
        public ProbeReply TcpReply { get; set; } = ProbeReply.Ok(15);
        public ProbeReply UdpReply { get; set; } = ProbeReply.Ok(25);
        public ProbeReply HttpReply { get; set; } = ProbeReply.Ok(30);

        public int IcmpCalls { get; private set; }
        public int TcpCalls { get; private set; }
        public int UdpCalls { get; private set; }
        public int HttpCalls { get; private set; }

        public Task<ProbeReply> IcmpProbeAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken ct)
        {
            IcmpCalls++;
            return Task.FromResult(IcmpReply);
        }

        public Task<ProbeReply> TcpConnectAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct)
        {
            TcpCalls++;
            return Task.FromResult(TcpReply);
        }

        public Task<ProbeReply> UdpProbeAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct)
        {
            UdpCalls++;
            return Task.FromResult(UdpReply);
        }

        public Task<ProbeReply> HttpProbeAsync(Uri url, int timeoutMs, CancellationToken ct)
        {
            HttpCalls++;
            return Task.FromResult(HttpReply);
        }
    }

    private sealed class NoOpProtector : ISecretProtector
    {
        public bool IsAvailable => false;

        public string ProtectToBase64(string plainText) =>
            throw new PlatformNotSupportedException("no disponible en tests");

        public string UnprotectFromBase64(string protectedBase64) =>
            throw new PlatformNotSupportedException("no disponible en tests");
    }
}
