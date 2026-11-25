using GameRouteOptimizer.Core.AutoSwitch;
using GameRouteOptimizer.Core.Events;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.StateMachine;
using GameRouteOptimizer.Core.Storage;
using GameRouteOptimizer.Core.Tunneling;
using Xunit;

namespace GameRouteOptimizer.Tests;

/// <summary>SessionRecorder: eventos, antes/después e informe exportable.</summary>
public class SessionRecorderTests
{
    [Fact]
    public void SessionRecorder_GuardaEventos()
    {
        var dir = TestPaths.TempDir();
        using var db = new SessionDatabase(dir);
        using var recorder = new SessionRecorder(db, new EventBus());

        var profile = new GameProfile { Name = "Juego" };
        var session = recorder.StartSession(profile, new GameServer { Name = "s", Ip = "203.0.113.1" }, "Manual");
        recorder.Record(EventSeverity.Warning, EventCategory.Tunnel, "Túnel con latencia alta.");
        recorder.EndSession("Completed");

        var events = recorder.GetSessionEvents(session.Id);
        Assert.Contains(events, e => e.Message.Contains("Túnel con latencia alta."));
        Assert.Contains(events, e => e.Severity == EventSeverity.Warning);
    }

    [Fact]
    public void SessionRecorder_GuardaHistorialConAntesYDespues()
    {
        var dir = TestPaths.TempDir();
        using var db = new SessionDatabase(dir);
        using var recorder = new SessionRecorder(db, new EventBus());

        var session = recorder.StartSession(new GameProfile { Name = "Juego" }, new GameServer { Name = "s", Ip = "1.2.3.4" }, "Automatic");
        recorder.SetDirectBaseline(new ProbeSummary { Target = "1.2.3.4", MeanMs = 80, LossRatio = 0.02, JitterMs = 6, StabilityScore = 70, SuccessCount = 10, TotalProbes = 10 });
        recorder.SetOptimized(new ProbeSummary { Target = "1.2.3.4", MeanMs = 45, LossRatio = 0, JitterMs = 2, StabilityScore = 95, SuccessCount = 10, TotalProbes = 10 });
        recorder.SetSelectedCandidate("relay-1", "Relay Teste");
        recorder.EndSession("Completed");

        var history = recorder.GetRecentSessions();
        var loaded = history.First(s => s.Id == session.Id);
        Assert.Equal(80, (double)loaded.DirectBefore!.MeanMs, 1);
        Assert.Equal(45, (double)loaded.Optimized!.MeanMs, 1);
        Assert.Equal(35, (double)loaded.ImprovementMs!, 1);
        Assert.Contains("más rápido", loaded.ImprovementText);
        Assert.Equal("Relay Teste", loaded.SelectedRelayName);
    }

    [Fact]
    public void SessionRecorder_ExportaInforme()
    {
        var dir = TestPaths.TempDir();
        using var db = new SessionDatabase(dir);
        using var recorder = new SessionRecorder(db, new EventBus());

        var session = recorder.StartSession(new GameProfile { Name = "Informe" }, new GameServer { Name = "s", Ip = "1.2.3.4" }, "Manual");
        recorder.Record(EventSeverity.Info, EventCategory.Route, "Cambio de ruta probado.");
        session = recorder.EndSession("Completed");

        var outDir = TestPaths.TempDir();
        var mdPath = recorder.ExportReportMarkdown(session, outDir);
        var jsonPath = recorder.ExportReportJson(session, outDir);

        Assert.True(File.Exists(mdPath));
        Assert.True(File.Exists(jsonPath));
        var md = File.ReadAllText(mdPath);
        Assert.Contains("Informe de sesión", md);
        Assert.Contains("Cambio de ruta probado.", md);
        Assert.Contains("Mejora estimada", md);
    }
}

/// <summary>Máquina de estados: transiciones válidas, rechazo y restauración ante fallo.</summary>
public class StateMachineTests
{
    [Fact]
    public async Task StateMachine_TransicionValida()
    {
        var sm = new OptimizerStateMachine(new EventBus());
        var ok = await sm.TransitionToAsync(OptimizerState.ProbingDirect);
        Assert.True(ok);
        Assert.Equal(OptimizerState.ProbingDirect, sm.Current);
    }

    [Fact]
    public async Task StateMachine_RechazaTransicionInvalida()
    {
        var sm = new OptimizerStateMachine(new EventBus());
        // Idle → Active no está permitida (debe pasar por Connecting).
        var ok = await sm.TransitionToAsync(OptimizerState.Active);
        Assert.False(ok);
        Assert.Equal(OptimizerState.Idle, sm.Current);
    }

    [Fact]
    public async Task StateMachine_RegistraTransicionesEnEventBus()
    {
        var bus = new EventBus();
        var received = new List<AppEvent>();
        bus.Subscribe<AppEvent>(e => received.Add(e));
        var sm = new OptimizerStateMachine(bus);

        await sm.TransitionToAsync(OptimizerState.ProbingDirect);

        Assert.Contains(received, e => e.Message.Contains("Inactivo") && e.Message.Contains("Midiendo ruta directa"));
    }

    [Fact]
    public async Task StateMachine_FalloEjecutaRutaDeRestauracion()
    {
        var bus = new EventBus();
        var sm = new OptimizerStateMachine(bus);
        var restoreRequested = false;
        sm.RestoreRequested += (_, _) => restoreRequested = true;

        await sm.TransitionToAsync(OptimizerState.ProbingDirect);
        var ok = await sm.TransitionToAsync(OptimizerState.SelectingRoute, _ =>
            throw new InvalidOperationException("boom"));

        Assert.False(ok);
        Assert.True(restoreRequested);
        Assert.Equal(OptimizerState.Idle, sm.Current); // restaurado
    }

    [Fact]
    public async Task StateMachine_CancelacionRestaura()
    {
        var sm = new OptimizerStateMachine(new EventBus());
        await sm.TransitionToAsync(OptimizerState.ProbingDirect);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ok = await sm.TransitionToAsync(OptimizerState.SelectingRoute, async ct =>
        {
            await Task.Delay(50, ct);
            return;
        }, cts.Token);

        Assert.False(ok);
        Assert.Equal(OptimizerState.Idle, sm.Current);
    }
}

/// <summary>Secretos: cifrado y descifrado (DPAPI/AES) + nunca texto plano.</summary>
public class SecretStorageTests
{
    [Fact]
    public void Secretos_CifranYDescifran()
    {
        var dir = TestPaths.TempDir();
        var protector = new SecretProtector(dir);
        var secret = "EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=";

        var cipher = protector.Protect(secret);

        Assert.NotEqual(secret, cipher);
        Assert.DoesNotContain(secret, cipher);
        Assert.Equal(secret, protector.Unprotect(cipher));
        Assert.Equal(protector.Mechanism, new SecretProtector(dir).Mechanism);
    }

    [Fact]
    public void Secretos_RoundTripEntreInstancias()
    {
        var dir = TestPaths.TempDir();
        var p1 = new SecretProtector(dir);
        var cipher = p1.Protect("clave-privada-super-secreta");

        var p2 = new SecretProtector(dir); // misma máquina: debe poder descifrar
        Assert.Equal("clave-privada-super-secreta", p2.Unprotect(cipher));
    }
}

/// <summary>IPC: framing y servidor/cliente sobre pipes (loopback local).</summary>
public class IpcTests
{
    [Fact]
    public async Task Ipc_PingPongEndToEnd()
    {
        var pipeName = "gr-test-" + Guid.NewGuid().ToString("N")[..8];
        using var server = new GameRouteOptimizer.Core.Ipc.NamedPipeIpcServer(pipeName);
        server.RegisterHandler(GameRouteOptimizer.Core.Ipc.IpcMessage.Ping,
            msg => new GameRouteOptimizer.Core.Ipc.IpcMessage(GameRouteOptimizer.Core.Ipc.IpcMessage.Pong, new { echo = msg.Id }));
        server.Start();

        // Dar tiempo al servidor a levantar el pipe.
        await Task.Delay(300);

        using var client = new GameRouteOptimizer.Core.Ipc.NamedPipeIpcClient(pipeName);
        var response = await client.SendAsync(new GameRouteOptimizer.Core.Ipc.IpcMessage(GameRouteOptimizer.Core.Ipc.IpcMessage.Ping, new { }), 5000);

        Assert.NotNull(response);
        Assert.Equal(GameRouteOptimizer.Core.Ipc.IpcMessage.Pong, response!.Type);
    }

    [Fact]
    public async Task Ipc_TipoDesconocido_DevuelveError()
    {
        var pipeName = "gr-test-" + Guid.NewGuid().ToString("N")[..8];
        using var server = new GameRouteOptimizer.Core.Ipc.NamedPipeIpcServer(pipeName);
        server.Start();
        await Task.Delay(300);

        using var client = new GameRouteOptimizer.Core.Ipc.NamedPipeIpcClient(pipeName);
        var response = await client.SendAsync(new GameRouteOptimizer.Core.Ipc.IpcMessage("tipo-inexistente", new { }), 5000);

        Assert.NotNull(response);
        Assert.Equal(GameRouteOptimizer.Core.Ipc.IpcMessage.Error, response!.Type);
    }

    [Fact]
    public async Task Ipc_ServicioNoDisponible_DevuelveNull()
    {
        using var client = new GameRouteOptimizer.Core.Ipc.NamedPipeIpcClient("gr-no-existe-" + Guid.NewGuid().ToString("N")[..8]);
        var response = await client.SendAsync(new GameRouteOptimizer.Core.Ipc.IpcMessage("ping", new { }), 3000);
        Assert.Null(response);
    }
}

/// <summary>Orquestador básico con túnel simulado: diagnóstico y decisión de no-tunelar.</summary>
public class OrchestratorTests
{
    private sealed class FakeTunnelManager : ITunnelManager
    {
        public TunnelState NextState { get; set; } = TunnelState.Connected;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public event EventHandler<TunnelStatus>? StatusChanged;

        public Task<TunnelStatus> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult(new TunnelStatus { State = TunnelState.Connected, TunnelName = "fake" });

        public Task<TunnelStatus> StartAsync(TunnelStartRequest request, CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(new TunnelStatus { State = NextState, TunnelName = request.TunnelName, Detail = "fake ok" });
        }

        public Task<TunnelStatus> StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            return Task.FromResult(new TunnelStatus { State = TunnelState.Disconnected });
        }
    }

    private static (EventBus Bus, ProbeEngine Engine, SessionRecorder Sessions) BuildCore()
    {
        var dir = TestPaths.TempDir();
        var bus = new EventBus();
        var engine = new ProbeEngine(new ProbeOptions { QuickSamples = 4, DeepSamples = 6, IntervalMs = 1, MaxProbesPerSecond = 100 },
            new FakeProbeTransport
            {
                IcmpLatency = { ["203.0.113.50"] = 80, ["203.0.113.51"] = 30 },
                TcpLatency = { ["203.0.113.51"] = 30 }
            });
        var sessions = new SessionRecorder(new SessionDatabase(dir), bus);
        return (bus, engine, sessions);
    }

    [Fact]
    public async Task Diagnostico_CompletoSinTunel()
    {
        var (bus, engine, sessions) = BuildCore();
        using var sessionsOwner = sessions;
        var sm = new OptimizerStateMachine(bus);
        var analyzer = new RouteAnalyzer(engine);
        var orchestrator = new OptimizerOrchestrator(bus, sm, engine, new TracerouteEngine(),
            analyzer, new FakeTunnelManager(), sessions, new AppSettings { MonitorIntervalMs = 50 });

        var profile = new GameProfile { Name = "Test", Servers = [new GameServer { Name = "s", Ip = "203.0.113.50" }] };
        var (probes, trace, recommendation) = await orchestrator.RunDiagnosticsAsync(
            profile, profile.Servers[0], [], ProbeMode.Quick);

        Assert.True(probes.IsReachable);
        Assert.Equal(80, probes.MeanMs, 1);
        Assert.Equal(OptimizerState.Idle, orchestrator.StateMachine.Current);
    }

    [Fact]
    public async Task OptimizacionManual_ConectaTunelSimulado()
    {
        var (bus, engine, sessions) = BuildCore();
        using var sessionsOwner = sessions;
        var sm = new OptimizerStateMachine(bus);
        var analyzer = new RouteAnalyzer(engine);
        var tunnel = new FakeTunnelManager();
        var orchestrator = new OptimizerOrchestrator(bus, sm, engine, new TracerouteEngine(),
            analyzer, tunnel, sessions, new AppSettings { MonitorIntervalMs = 50 });

        var profile = new GameProfile { Name = "Test", Servers = [new GameServer { Name = "s", Ip = "203.0.113.50" }] };
        var relay = new RelayNode { Id = "relayman1", Name = "Relay bueno", EndpointHost = "203.0.113.51", EndpointPort = 51820 };

        await orchestrator.StartOptimizationAsync(profile, profile.Servers[0], [relay],
            new OptimizationOptions { Automatic = false, ManualRelayId = relay.Id, ProbeMode = ProbeMode.Quick });

        Assert.Equal(1, tunnel.StartCalls);
        // Espera breve a que el monitoreo arranque.
        await Task.Delay(200);
        await orchestrator.StopAsync("test");
        Assert.Equal(1, tunnel.StopCalls);
        Assert.Equal(OptimizerState.Idle, orchestrator.StateMachine.Current);
    }

    [Fact]
    public async Task OptimizacionAutomatica_SiDirectaEsMejor_NoTunela()
    {
        var (bus, engine, sessions) = BuildCore();
        using var sessionsOwner = sessions;
        var sm = new OptimizerStateMachine(bus);
        var analyzer = new RouteAnalyzer(engine);
        var tunnel = new FakeTunnelManager();
        var orchestrator = new OptimizerOrchestrator(bus, sm, engine, new TracerouteEngine(),
            analyzer, tunnel, sessions, new AppSettings { MonitorIntervalMs = 50 });

        // El relay responde a 100 ms (más lento que la directa de 80 ms).
        var engineWithSlowRelay = new ProbeEngine(
            new ProbeOptions { QuickSamples = 4, IntervalMs = 1, MaxProbesPerSecond = 100 },
            new FakeProbeTransport
            {
                IcmpLatency = { ["203.0.113.50"] = 80, ["203.0.113.60"] = 100 }
            });
        var slowAnalyzer = new RouteAnalyzer(engineWithSlowRelay);
        var orchestrator2 = new OptimizerOrchestrator(bus, sm, engineWithSlowRelay, new TracerouteEngine(),
            slowAnalyzer, tunnel, sessions, new AppSettings { MonitorIntervalMs = 50 });

        var profile = new GameProfile { Name = "Test", Servers = [new GameServer { Name = "s", Ip = "203.0.113.50" }] };
        var relay = new RelayNode { Id = "relaylento", Name = "Relay lento", EndpointHost = "203.0.113.60", EndpointPort = 51820 };

        await orchestrator2.StartOptimizationAsync(profile, profile.Servers[0], [relay],
            new OptimizationOptions { Automatic = true, ProbeMode = ProbeMode.Quick });

        Assert.Equal(0, tunnel.StartCalls);
        Assert.Equal(OptimizerState.Idle, orchestrator2.StateMachine.Current);
    }
}
