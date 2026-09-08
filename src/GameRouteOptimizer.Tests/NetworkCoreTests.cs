using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Privileged;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.State;
using GameRouteOptimizer.Core.Storage;
using GameRouteOptimizer.Core.Tunneling;
using Xunit;

namespace GameRouteOptimizer.Tests;

/// <summary>
/// Pruebas del núcleo de red y túnel: plan de rutas (incluido DNS), políticas de failback,
/// kill switch con operaciones simuladas, gestor de túnel (secuencia única, rollback,
/// recuperación de huérfanos), claves privadas en reposo y modo global de extremo a extremo.
/// Sin red externa ni dependencias del sistema: todas las operaciones privilegiadas son falsas.
/// </summary>
public class NetworkCoreTests
{
    private const string ValidKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>Segunda clave WireGuard válida (44 caracteres) para el relay con secreto.</summary>
    private const string SecretKey = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBE=";

    // ================= RouteCalculator (rutas + DNS) =================

    [Fact]
    public void RouteCalculator_PlanSoloJuegoIncluyeDnsIpv4()
    {
        var plan = RouteCalculator.BuildPlan(
            RouteMode.TunnelGameDestinations,
            new[] { "server.ejemplo.com" },
            new[] { "203.0.113.7" },
            "gro0",
            dnsServerIps: new[] { "8.8.8.8", "8.8.8.8", "2001:db8::1", "no-es-ip" });

        Assert.Contains("203.0.113.7/32", plan.AllowedIps);
        Assert.Contains("8.8.8.8/32", plan.AllowedIps);
        Assert.Single(plan.AllowedIps, ip => ip == "8.8.8.8/32");
        Assert.DoesNotContain(plan.AllowedIps, ip => ip.Contains("db8", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, w => w.Contains("no-es-ip", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, w => w.Contains("2001:db8::1", StringComparison.Ordinal));
        // El plan sigue siendo reversible: todo lo añadido tiene su inversa.
        Assert.Equal(plan.RoutesToAdd.Count, plan.RoutesToDeleteOnStop.Count);
    }

    [Fact]
    public void RouteCalculator_PlanGlobalNoSeVeAfectadoPorDns()
    {
        var plan = RouteCalculator.BuildPlan(
            RouteMode.TunnelGlobal,
            Array.Empty<string>(),
            Array.Empty<string>(),
            "gro0",
            dnsServerIps: new[] { "8.8.8.8" });

        Assert.Contains("0.0.0.0/0", plan.AllowedIps);
        Assert.Contains("::/0", plan.AllowedIps);
        Assert.DoesNotContain(plan.AllowedIps, ip => ip.EndsWith("/32", StringComparison.Ordinal));
    }

    // ================= FailbackPolicy (fallos y empeoramiento) =================

    private static AutoSwitchSettings AutoSettings(bool autoFailback = true, int failures = 3) => new()
    {
        AutoFailback = autoFailback,
        FailbackAfterConsecutiveFailures = failures,
        MinImprovementMs = 10,
    };

    private static ProbeSummary Summary(double avgMs, bool usable = true) => new()
    {
        TargetLabel = "srv",
        Kind = ProbeKind.TcpConnect,
        Attempts = 10,
        Successes = usable ? 10 : 0,
        Failures = usable ? 0 : 10,
        AvgMs = avgMs,
        MinMs = avgMs - 2,
        MaxMs = avgMs + 2,
        LossPercent = usable ? 0 : 100,
    };

    [Fact]
    public void FailbackPolicy_CaidaSoloTrasNComprobacionesConsecutivas()
    {
        var settings = AutoSettings();
        Assert.Equal(FailbackKind.None,
            FailbackPolicy.ShouldFailback(false, true, 1, 0, Summary(50), Summary(60), settings, DateTimeOffset.UtcNow).Kind);
        Assert.Equal(FailbackKind.None,
            FailbackPolicy.ShouldFailback(false, true, 2, 0, Summary(50), Summary(60), settings, DateTimeOffset.UtcNow).Kind);
        var decision = FailbackPolicy.ShouldFailback(false, true, 3, 0, Summary(50), Summary(60), settings, DateTimeOffset.UtcNow);
        Assert.Equal(FailbackKind.TunnelDown, decision.Kind);
        Assert.Contains("dejó de responder", decision.ReasonEs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailbackPolicy_ConAutoFailbackDesactivadoUnTunelCaidoSigueExigiendoVueltaADirecto()
    {
        var settings = AutoSettings(autoFailback: false);
        var decision = FailbackPolicy.ShouldFailback(false, true, 1, 0, Summary(50), Summary(60), settings, DateTimeOffset.UtcNow);
        Assert.Equal(FailbackKind.TunnelDown, decision.Kind);
    }

    [Fact]
    public void FailbackPolicy_EmpeoramientoMenorNoDisparaFailback()
    {
        var settings = AutoSettings();
        var decision = FailbackPolicy.ShouldFailback(true, true, 0, 0, Summary(100), Summary(108), settings, DateTimeOffset.UtcNow);
        Assert.Equal(FailbackKind.None, decision.Kind);
    }

    [Fact]
    public void FailbackPolicy_EmpeoramientoClaroYConstanteDisparaFailback()
    {
        var settings = AutoSettings();
        var decision = FailbackPolicy.ShouldFailback(true, true, 0, 0, Summary(60), Summary(100), settings, DateTimeOffset.UtcNow);
        Assert.Equal(FailbackKind.TunnelWorse, decision.Kind);
        Assert.Contains("empeora", decision.ReasonEs, StringComparison.OrdinalIgnoreCase);
    }

    // ================= Kill switch con operaciones simuladas =================

    [Fact]
    public async Task KillSwitchManager_ActivaYDesactivaConOpsSimuladas()
    {
        var ops = new FakeOps();
        var manager = new KillSwitchManager(ops);
        var events = 0;
        manager.Changed += (_, _) => events++;

        var (ok, error) = await manager.EnableAsync("GRO-abc", new[] { "198.51.100.1" }, 51820, CancellationToken.None);
        Assert.True(ok, error);
        Assert.True(manager.IsEnabled);

        var enableOp = ops.Ops.Single(o => o.Kind == PrivilegedOpKind.KillSwitchEnable);
        var payload = JsonSerializer.Deserialize<KillSwitchPayload>(enableOp.PayloadJson!)!;
        Assert.Equal("GRO-abc", payload.TunnelInterfaceName);
        Assert.Contains("198.51.100.1", payload.EndpointIps);
        Assert.Equal(51820, payload.EndpointPort);
        Assert.Contains("kill switch", enableOp.Reason, StringComparison.OrdinalIgnoreCase);

        // Activar dos veces no duplica operaciones ni estado.
        var countBefore = ops.Ops.Count;
        var (ok2, _) = await manager.EnableAsync("GRO-abc", new[] { "198.51.100.1" }, 51820, CancellationToken.None);
        Assert.True(ok2);
        Assert.Equal(countBefore, ops.Ops.Count);

        var (okD, errorD) = await manager.DisableAsync(CancellationToken.None);
        Assert.True(okD, errorD);
        Assert.False(manager.IsEnabled);
        Assert.Contains(ops.Ops, o => o.Kind == PrivilegedOpKind.KillSwitchDisable);

        // Desactivar sin estar activo no ejecuta operaciones.
        var countAfter = ops.Ops.Count;
        var (ok3, _) = await manager.DisableAsync(CancellationToken.None);
        Assert.True(ok3);
        Assert.Equal(countAfter, ops.Ops.Count);
        Assert.Equal(2, events); // activado y desactivado
    }

    [Fact]
    public async Task KillSwitchManager_EnableFallidoNoMarcaActivado()
    {
        var ops = new FakeOps { Result = PrivilegedOpResult.Failure("elevación rechazada") };
        var manager = new KillSwitchManager(ops);

        var (ok, error) = await manager.EnableAsync("GRO-abc", new[] { "198.51.100.1" }, 51820, CancellationToken.None);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.False(manager.IsEnabled);

        // ForceDisable no lanza aunque no estuviera activo.
        var (okD, _) = await manager.ForceDisableWithResultAsync();
        Assert.True(okD);
    }

    [Fact]
    public async Task KillSwitchManager_OperacionesNoDisponiblesDevuelvenErrorClaro()
    {
        var ops = new FakeOps { IsAvailable = false };
        var manager = new KillSwitchManager(ops);

        var (ok, error) = await manager.EnableAsync("GRO-abc", new[] { "198.51.100.1" }, 51820, CancellationToken.None);
        Assert.False(ok);
        Assert.Contains("elevado", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.SupportsRealKillSwitch);
    }

    // ================= TunnelManager (secuencia única, rollback, limpieza) =================

    private static WireGuardConfig SampleConfig(string dns = "1.1.1.1") => new()
    {
        PrivateKey = ValidKey,
        InterfaceAddresses = { "10.66.0.2/32" },
        Dns = dns,
        Peers =
        {
            new WireGuardPeer
            {
                PublicKey = ValidKey,
                AllowedIps = { "0.0.0.0/0", "::/0" },
                EndpointHost = "198.51.100.10",
                EndpointPort = 51820,
            },
        },
    };

    [Fact]
    public async Task TunnelManager_ConectarEsUnaSolaSecuenciaConDnsYDesconectarRestaura()
    {
        var ops = new FakeOps { Result = PrivilegedOpResult.Success("ok") };
        var manager = new TunnelManager(ops);
        var lastEvent = TunnelProviderState.NotInstalled;
        manager.StatusChanged += (_, e) => lastEvent = e.State;

        var (ok, error) = await manager.ConnectAsync(
            SampleConfig(), "GRO-t1", RouteMode.TunnelGlobal, "relay-1", "Relay Uno", CancellationToken.None);
        Assert.True(ok, error);
        Assert.NotNull(manager.Active);
        Assert.Equal("GRO-t1", manager.Active!.InterfaceName);
        Assert.Equal(RouteMode.TunnelGlobal, manager.Active.Mode);
        Assert.Equal(TunnelProviderState.Active, lastEvent);

        // Una única operación privilegiada (una sola elevación/UAC para todo el arranque).
        var seqOp = Assert.Single(ops.Ops, o => o.Kind == PrivilegedOpKind.Sequence);
        var steps = JsonSerializer.Deserialize<SequencePayload>(seqOp.PayloadJson!)!.Steps;
        Assert.Equal(3, steps.Count);
        Assert.Equal(PrivilegedOpKind.InstallWireGuardTunnel, steps[0].Kind);
        Assert.Equal(PrivilegedOpKind.WaitWireGuardTunnel, steps[1].Kind);
        Assert.Equal(PrivilegedOpKind.SetInterfaceDns, steps[2].Kind);

        var install = JsonSerializer.Deserialize<WireGuardTunnelPayload>(steps[0].PayloadJson!)!;
        Assert.Equal("GRO-t1", install.TunnelName);
        Assert.Contains("AllowedIPs = 0.0.0.0/0, ::/0", install.ConfigText, StringComparison.Ordinal);
        Assert.Contains("DNS = 1.1.1.1", install.ConfigText, StringComparison.Ordinal);

        var dns = JsonSerializer.Deserialize<DnsPayload>(steps[2].PayloadJson!)!;
        Assert.Equal("GRO-t1", dns.InterfaceName);
        Assert.Contains("1.1.1.1", dns.Servers);

        // Desconectar: la desinstalación elimina interfaz, rutas y DNS de una sola operación.
        var (okD, errorD) = await manager.DisconnectAsync("prueba", CancellationToken.None);
        Assert.True(okD, errorD);
        Assert.Null(manager.Active);
        Assert.Equal(TunnelProviderState.NotInstalled, manager.LastKnownState);

        var disconnectOp = ops.Ops.Last(o => o.Kind == PrivilegedOpKind.UninstallWireGuardTunnel);
        var disconnectPayload = JsonSerializer.Deserialize<WireGuardTunnelPayload>(disconnectOp.PayloadJson!)!;
        Assert.Equal("GRO-t1", disconnectPayload.TunnelName);
        Assert.DoesNotContain(ops.Ops, o => o.Kind == PrivilegedOpKind.RestoreInterfaceDns);
    }

    [Fact]
    public async Task TunnelManager_SinDnsNoIncluyePasoDeDns()
    {
        var ops = new FakeOps { Result = PrivilegedOpResult.Success("ok") };
        var manager = new TunnelManager(ops);

        var (ok, _) = await manager.ConnectAsync(
            SampleConfig(dns: "dns.interno.example"), "GRO-t2", RouteMode.TunnelGlobal, "r", "R", CancellationToken.None);
        Assert.True(ok);

        // DNS no IPv4: no se intenta aplicar como DNS de interfaz (solo queda en el .conf).
        var seq = ops.Ops.Single(o => o.Kind == PrivilegedOpKind.Sequence);
        var steps = JsonSerializer.Deserialize<SequencePayload>(seq.PayloadJson!)!.Steps;
        Assert.Equal(2, steps.Count);
        Assert.DoesNotContain(steps, s => s.Kind == PrivilegedOpKind.SetInterfaceDns);
    }

    [Fact]
    public async Task TunnelManager_ConectarSinDobleTunelActivo()
    {
        var ops = new FakeOps { Result = PrivilegedOpResult.Success("ok") };
        var manager = new TunnelManager(ops);
        await manager.ConnectAsync(SampleConfig(), "GRO-a", RouteMode.TunnelGlobal, "r1", "R1", CancellationToken.None);

        var (ok, error) = await manager.ConnectAsync(SampleConfig(), "GRO-b", RouteMode.TunnelGlobal, "r2", "R2", CancellationToken.None);
        Assert.False(ok);
        Assert.Contains("ya hay un túnel activo", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TunnelManager_ConectarFallidoRevierteLaInstalacionParcial()
    {
        var ops = new FakeOps();
        ops.Handler = op => op.Kind switch
        {
            PrivilegedOpKind.Sequence => PrivilegedOpResult.Failure("wireguard.exe falló al instalar"),
            PrivilegedOpKind.UninstallWireGuardTunnel => PrivilegedOpResult.Success("ok", "rollback"),
            _ => PrivilegedOpResult.Success("ok"),
        };
        var manager = new TunnelManager(ops);

        var (ok, error) = await manager.ConnectAsync(
            SampleConfig(), "GRO-t3", RouteMode.TunnelGlobal, "r", "R", CancellationToken.None);
        Assert.False(ok);
        Assert.Contains("revirtió", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(manager.Active);

        var rollback = ops.Ops.Last(o => o.Kind == PrivilegedOpKind.UninstallWireGuardTunnel);
        var payload = JsonSerializer.Deserialize<WireGuardTunnelPayload>(rollback.PayloadJson!)!;
        Assert.Equal("GRO-t3", payload.TunnelName);
    }

    [Fact]
    public async Task TunnelManager_DesconectarSinTunelEsInocuo()
    {
        var ops = new FakeOps();
        var manager = new TunnelManager(ops);
        var (ok, error) = await manager.DisconnectAsync("nada que hacer", CancellationToken.None);
        Assert.True(ok, error);
        Assert.Empty(ops.Ops);
        Assert.Equal(TunnelProviderState.NotInstalled, manager.LastKnownState);
    }

    [Fact]
    public async Task TunnelManager_RemoveLeftoverEnviaDesinstalacionDelTunel()
    {
        var ops = new FakeOps { Result = PrivilegedOpResult.Success("ok") };
        var manager = new TunnelManager(ops);

        var (ok, error) = await manager.RemoveLeftoverAsync("GRO-hu1", CancellationToken.None);
        Assert.True(ok, error);
        var op = Assert.Single(ops.Ops);
        Assert.Equal(PrivilegedOpKind.UninstallWireGuardTunnel, op.Kind);
        var payload = JsonSerializer.Deserialize<WireGuardTunnelPayload>(op.PayloadJson!)!;
        Assert.Equal("GRO-hu1", payload.TunnelName);
        Assert.Null(manager.Active);
    }

    // ================= recuperación tras cierre inesperado =================

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), "gro-net-tests-" + Guid.NewGuid().ToString("N") + ".db");

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

    [Fact]
    public async Task RecuperacionTrasCierreEliminaTunelYDesactivaKillSwitch()
    {
        var store = NewStore(out var dbPath);
        try
        {
            store.SaveNetworkRuntimeState(new NetworkRuntimeState
            {
                TunnelInterfaceName = "GRO-huerto",
                KillSwitchEnabled = true,
            });

            var ops = new FakeOps { Result = PrivilegedOpResult.Success("ok") };
            var orchestrator = BuildOrchestrator(store, ops, out _, out _);

            await orchestrator.RecoverAfterCrashAsync();

            Assert.Contains(ops.Ops, o => o.Kind == PrivilegedOpKind.KillSwitchDisable);
            var uninstall = ops.Ops.Single(o => o.Kind == PrivilegedOpKind.UninstallWireGuardTunnel);
            var payload = JsonSerializer.Deserialize<WireGuardTunnelPayload>(uninstall.PayloadJson!)!;
            Assert.Equal("GRO-huerto", payload.TunnelName);

            var cleared = store.LoadNetworkRuntimeState();
            Assert.Null(cleared.TunnelInterfaceName);
            Assert.False(cleared.KillSwitchEnabled);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task RecuperacionSinRestosNoEjecutaNingunaOperacion()
    {
        var store = NewStore(out var dbPath);
        try
        {
            store.SaveNetworkRuntimeState(new NetworkRuntimeState());
            var ops = new FakeOps { Result = PrivilegedOpResult.Success("ok") };
            var orchestrator = BuildOrchestrator(store, ops, out _, out _);

            await orchestrator.RecoverAfterCrashAsync();

            Assert.Empty(ops.Ops);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    // ================= claves privadas: nunca en texto plano =================

    [Fact]
    public async Task RelayManager_ClavePrivadaSoloCifradaEnSqliteYAusenteDelJson()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var protector = new FakeProtector();
            var manager = new RelayManager(store, protector);
            var relay = new RelayNode
            {
                Name = "Relay secreto",
                EndpointHost = "198.51.100.10",
                EndpointPort = 51820,
                PublicKey = ValidKey,
            };
            Assert.True(manager.Save(relay, out var saveError), saveError);
            var storedRelay = manager.GetById(relay.Id)!;
            Assert.NotNull(storedRelay);
            Assert.True(manager.SetPrivateKey(storedRelay, SecretKey));

            // La fila del relay (JSON) no contiene la clave…
            var relayJson = ReadSettingsLikeColumn(dbPath, "Relays", "json", relay.Id);
            Assert.DoesNotContain(SecretKey, relayJson, StringComparison.Ordinal);
            Assert.DoesNotContain("PrivateKeyPlain", relayJson, StringComparison.Ordinal);

            // …y la tabla de secretos solo guarda el valor cifrado.
            var storedSecret = ReadRelaySecret(dbPath, relay.Id);
            Assert.NotNull(storedSecret);
            Assert.Equal("ENC:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(SecretKey)),
                storedSecret);
            Assert.DoesNotContain(SecretKey, storedSecret, StringComparison.Ordinal);

            // Un gestor nuevo (mismo almacén) recupera la clave descifrada solo en memoria.
            var reloaded = new RelayManager(store, protector);
            var reloadedRelay = reloaded.GetById(relay.Id)!;
            Assert.True(reloaded.HasUsablePrivateKey(reloadedRelay));
            Assert.Equal(SecretKey, reloadedRelay.PrivateKeyPlain);

            // La exportación JSON nunca incluye secretos, y la importación los descarta.
            var exported = RelayManager.ExportToJson(new[] { reloadedRelay });
            Assert.DoesNotContain(SecretKey, exported, StringComparison.Ordinal);
            var imported = RelayManager.ImportFromJson(exported, out _);
            Assert.Null(Assert.Single(imported).PrivateKeyPlain);

            // Sin protector disponible la clave NO se persiste (solo memoria).
            var store2 = NewStore(out var dbPath2);
            try
            {
                var managerNoOp = new RelayManager(store2, new NoOpProtector());
                var relay2 = new RelayNode { Name = "R", EndpointHost = "198.51.100.11", EndpointPort = 51820 };
                Assert.True(managerNoOp.Save(relay2, out _));
                var storedRelay2 = managerNoOp.GetById(relay2.Id)!;
                Assert.False(managerNoOp.SetPrivateKey(storedRelay2, SecretKey));
                var json2 = ReadSettingsLikeColumn(dbPath2, "Relays", "json", relay2.Id);
                Assert.DoesNotContain(SecretKey, json2, StringComparison.Ordinal);
                Assert.Null(ReadRelaySecret(dbPath2, relay2.Id));
            }
            finally
            {
                store2.Dispose();
                DeleteDb(dbPath2);
            }

            await Task.CompletedTask;
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    // ================= orquestador: modo global de extremo a extremo =================

    [Fact]
    public async Task Orchestrator_ModoGlobalActivaTunelGlobalYAlDetenerRestaura()
    {
        var store = NewStore(out var dbPath);
        try
        {
            var ops = new FakeOps { Result = PrivilegedOpResult.Success("ok") };
            var orchestrator = BuildOrchestrator(store, ops, out var games, out var relays);

            var profile = new GameProfile
            {
                Name = "Juego global",
                RouteMode = RouteMode.TunnelGlobal,
                AutoStartOptimization = false,
            };
            profile.Targets.Add(new GameServerTarget { IpAddress = "203.0.113.7", Ports = { 27015 }, Region = "EU" });
            Assert.True(games.Save(profile, out var gameError), gameError);

            var relay = new RelayNode
            {
                Name = "Relay Global",
                EndpointHost = "198.51.100.10",
                EndpointPort = 51820,
                PublicKey = ValidKey,
                AllowedIps = "0.0.0.0/0, ::/0",
                PrivateKeyPlain = ValidKey,
            };
            // Guardar con la clave ya en memoria: RelayManager la persiste cifrada y la restaura
            // en el objeto recargado; el orquestador solo ve la instancia del gestor.
            Assert.True(relays.Save(relay, out var relayError), relayError);
            Assert.NotNull(relays.GetById(relay.Id));

            orchestrator.RequestOptimization(profile, autoApproved: true);

            // Esperar a que el túnel quede activo (el worker mide directo + relay y conecta).
            var connected = await WaitUntilAsync(
                () => orchestrator.IsTunnelActive && orchestrator.ActiveTunnel is not null, TimeSpan.FromSeconds(15));
            Assert.True(connected, "El orquestador no llegó a activar el túnel (estado: " + orchestrator.State + ")");

            // El modo respetado es GLOBAL (regresión: antes se forzaba «solo juego»).
            Assert.Equal(RouteMode.TunnelGlobal, orchestrator.ActiveTunnel!.Mode);

            // La configuración enviada encamina TODO (0.0.0.0/0), no solo destinos.
            var seq = ops.Ops.First(o => o.Kind == PrivilegedOpKind.Sequence);
            var steps = JsonSerializer.Deserialize<SequencePayload>(seq.PayloadJson!)!.Steps;
            Assert.Equal(PrivilegedOpKind.InstallWireGuardTunnel, steps[0].Kind);
            var install = JsonSerializer.Deserialize<WireGuardTunnelPayload>(steps[0].PayloadJson!)!;
            Assert.Contains("AllowedIPs = 0.0.0.0/0, ::/0", install.ConfigText, StringComparison.Ordinal);
            Assert.StartsWith("GRO-", install.TunnelName, StringComparison.Ordinal);

            // Detener: el túnel se desinstala y el estado persistido queda limpio.
            await orchestrator.StopOptimizationAsync("fin de la prueba");
            var stopped = await WaitUntilAsync(() => !orchestrator.IsTunnelActive && orchestrator.State == ProgramState.Idle,
                TimeSpan.FromSeconds(15));
            Assert.True(stopped, "El orquestador no volvió a Idle tras detener");

            var leftoverUninstall = UninstallNames(ops).Count > 0;
            Assert.True(leftoverUninstall, "Al detener debe desinstalarse el túnel");
            var state = store.LoadNetworkRuntimeState();
            Assert.Null(state.TunnelInterfaceName);
            Assert.False(state.KillSwitchEnabled);
        }
        finally
        {
            store.Dispose();
            DeleteDb(dbPath);
        }
    }

    // ================= helpers de construcción =================

    private static ConfigStore NewStore(out string dbPath)
    {
        dbPath = NewDbPath();
        return new ConfigStore(dbPath);
    }

    private static OptimizationOrchestrator BuildOrchestrator(
        ConfigStore store,
        FakeOps ops,
        out GameManager games,
        out RelayManager relays)
    {
        var logDir = Path.Combine(Path.GetTempPath(), "gro-net-logs-" + Guid.NewGuid().ToString("N"));
        var logs = new LogService(logDir);
        var protector = new FakeProtector();
        games = new GameManager(store);
        relays = new RelayManager(store, protector);
        var sessions = new SessionRecorder(store);
        var probing = new ProbingSettings
        {
            QuickProbeCount = 3,
            DeepProbeCount = 3,
            IntervalMs = 1,
            TimeoutMs = 400,
        };
        var settings = new AppSettings { Probing = probing };
        settings.AutoSwitch.Enabled = false;
        settings.Tunnel.HealthCheckIntervalSeconds = 2;
        // En entornos donde wg.exe existe pero la interfaz de prueba no, la consulta de salud
        // devolvería Degraded; con el umbral alto el túnel de prueba nunca dispara failback.
        settings.AutoSwitch.FailbackAfterConsecutiveFailures = 100;

        var probes = new ProbeEngine(new FakeProbeTransport(), probing);
        var traceroute = new TracerouteEngine(new FakeProbeTransport());
        var tunnel = new TunnelManager(ops);
        var killSwitch = new KillSwitchManager(ops);
        var state = new ProgramStateMachine();
        var orchestrator = new OptimizationOrchestrator(
            () => settings, logs, new NotificationService(), sessions,
            games, relays, probes, traceroute, tunnel, killSwitch, state, store);
        return orchestrator;
    }

    /// <summary>
    /// Nombres de túnel que se pidió desinstalar, tanto en operaciones directas como dentro
    /// de secuencias compuestas (el gestor agrupa la desconexión en una sola elevación).
    /// </summary>
    private static List<string> UninstallNames(FakeOps ops)
    {
        var names = new List<string>();
        foreach (var op in ops.Ops)
        {
            CollectUninstallNames(op, names);
        }

        return names;
    }

    private static void CollectUninstallNames(PrivilegedOp op, List<string> names)
    {
        if (string.IsNullOrWhiteSpace(op.PayloadJson))
        {
            return;
        }

        if (op.Kind == PrivilegedOpKind.UninstallWireGuardTunnel)
        {
            var payload = JsonSerializer.Deserialize<WireGuardTunnelPayload>(op.PayloadJson);
            if (!string.IsNullOrWhiteSpace(payload?.TunnelName))
            {
                names.Add(payload.TunnelName);
            }
        }
        else if (op.Kind == PrivilegedOpKind.Sequence)
        {
            var steps = JsonSerializer.Deserialize<SequencePayload>(op.PayloadJson)?.Steps;
            if (steps is not null)
            {
                foreach (var step in steps)
                {
                    CollectUninstallNames(step, names);
                }
            }
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(120);
        }

        return condition();
    }

    private static string ReadSettingsLikeColumn(string dbPath, string table, string column, string id)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM {table} WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        return (cmd.ExecuteScalar() as string) ?? string.Empty;
    }

    private static string? ReadRelaySecret(string dbPath, string relayId)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT protected_key FROM RelaySecrets WHERE relay_id=$id;";
        cmd.Parameters.AddWithValue("$id", relayId);
        return cmd.ExecuteScalar() as string;
    }

    // ================= fakes =================

    private sealed class FakeOps : IPrivilegedOps
    {
        public bool IsAvailable { get; set; } = true;
        public List<PrivilegedOp> Ops { get; } = new();
        public PrivilegedOpResult Result { get; set; } = PrivilegedOpResult.Success("ok");
        public Func<PrivilegedOp, PrivilegedOpResult>? Handler { get; set; }

        public Task<PrivilegedOpResult> RunAsync(PrivilegedOp op, CancellationToken ct)
        {
            Ops.Add(op);
            return Task.FromResult(Handler is not null ? Handler(op) : Result);
        }
    }

    private sealed class FakeProbeTransport : IProbeTransport
    {
        public string Description => "transporte simulado";

        public Task<ProbeReply> IcmpProbeAsync(IPAddress target, int ttl, int timeoutMs, CancellationToken ct)
            => Task.FromResult(ProbeReply.Ok(15));

        public Task<ProbeReply> TcpConnectAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct)
            => Task.FromResult(ProbeReply.Ok(15));

        public Task<ProbeReply> UdpProbeAsync(IPAddress target, int port, int timeoutMs, CancellationToken ct)
            => Task.FromResult(ProbeReply.Ok(15));

        public Task<ProbeReply> HttpProbeAsync(Uri url, int timeoutMs, CancellationToken ct)
            => Task.FromResult(ProbeReply.Ok(15));
    }

    /// <summary>Protector simulado reversible (sustituye a DPAPI en tests).</summary>
    private sealed class FakeProtector : ISecretProtector
    {
        public bool IsAvailable => true;

        public string ProtectToBase64(string plainText) =>
            "ENC:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));

        public string UnprotectFromBase64(string protectedBase64) =>
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedBase64["ENC:".Length..]));
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
