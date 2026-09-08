using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            var relay = manager.ImportFromConfigText(conf, null, out var warnings);
            Assert.NotNull(relay);
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
            var relay = new RelayNode { Name = "R1", EndpointHost = "10.0.0.2", EndpointPort = 51820 };
            manager.Save(relay, out _);
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
        Assert.Contains(game.Warnings, w => w.Contains("no-es-ip", StringComparison.Ordinal));
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

    private sealed class NoOpProtector : ISecretProtector
    {
        public bool IsAvailable => false;

        public string ProtectToBase64(string plainText) =>
            throw new PlatformNotSupportedException("no disponible en tests");

        public string UnprotectFromBase64(string protectedBase64) =>
            throw new PlatformNotSupportedException("no disponible en tests");
    }
}
