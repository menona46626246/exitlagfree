using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Scoring;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.State;
using GameRouteOptimizer.Core.Storage;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.Core.Services;

/// <summary>Instantánea de métricas en vivo para la UI.</summary>
public sealed class MetricsSnapshot
{
    public DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
    public ProbeSummary? Direct { get; set; }
    public ProbeSummary? Optimized { get; set; }
    public ProbeSummary? RelayEndpoint { get; set; }
    public string? RelayName { get; set; }
    public bool TunnelActive { get; set; }
}

/// <summary>Reporte del modo diagnóstico (sin túnel).</summary>
public sealed class DiagnosticsReport
{
    public required string GameName { get; init; }
    public required string TargetDisplay { get; init; }
    public DateTimeOffset CompletedUtc { get; set; } = DateTimeOffset.UtcNow;
    public ProbeSummary? Direct { get; set; }
    public TracerouteResult? Traceroute { get; set; }
    public List<RelayMeasurement> RelayMeasurements { get; set; } = new();
    public ScoringOutput? Recommendation { get; set; }
    public List<string> Notes { get; set; } = new();
}

public sealed class RelayMeasurement
{
    public required string RelayId { get; init; }
    public required string RelayName { get; init; }
    public ProbeSummary? Endpoint { get; set; }
}

/// <summary>
/// Orquestador de diagnóstico y optimización. Conecta: máquina de estados, probes, scoring,
/// túnel, kill switch, sesiones y eventos. Diseñado para ejecutarse con dependencias reales
/// (app) o simuladas (tests).
/// </summary>
public sealed class OptimizationOrchestrator : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<AppSettings> _settingsProvider;
    private readonly LogService _log;
    private readonly NotificationService _notifications;
    private readonly SessionRecorder _sessions;
    private readonly GameManager _games;
    private readonly RelayManager _relays;
    private readonly ProbeEngine _probeEngine;
    private readonly TracerouteEngine _traceroute;
    private readonly TunnelManager _tunnel;
    private readonly KillSwitchManager _killSwitch;
    private readonly ProgramStateMachine _state;
    private readonly ConfigStore _store;

    private CancellationTokenSource? _operationCts;
    private Task? _operationTask;
    private GameProfile? _currentProfile;
    private SessionRecord? _currentSession;
    private string? _currentTargetDisplay;
    private DateTimeOffset? _lastSwitchUtc;
    private int _sustainedWinnerTicks;
    private string? _lastRecommendedWinner;
    private int _tunnelFailures;
    private int _targetViaTunnelFailures;
    private DateTimeOffset _lastDirectSampleUtc;
    private DateTimeOffset _lastOptimizedSampleUtc;
    private bool _userConfirmedRelay;
    private string? _pendingRelayId;

    // Últimos resúmenes para el bucle de monitoreo (se reemplazan con cada muestra).
    private ProbeSummary? _rollingDirect;
    private ProbeSummary? _rollingOptimized;

    public event EventHandler<MetricsSnapshot>? MetricsUpdated;
    public event EventHandler<DiagnosticsReport>? DiagnosticsCompleted;
    public event EventHandler<ScoringOutput>? RecommendationUpdated;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<SessionEvent>? SessionEventAdded;

    public OptimizationOrchestrator(
        Func<AppSettings> settingsProvider,
        LogService log,
        NotificationService notifications,
        SessionRecorder sessions,
        GameManager games,
        RelayManager relays,
        ProbeEngine probeEngine,
        TracerouteEngine traceroute,
        TunnelManager tunnel,
        KillSwitchManager killSwitch,
        ProgramStateMachine state,
        ConfigStore store)
    {
        _settingsProvider = settingsProvider;
        _log = log;
        _notifications = notifications;
        _sessions = sessions;
        _games = games;
        _relays = relays;
        _probeEngine = probeEngine;
        _traceroute = traceroute;
        _tunnel = tunnel;
        _killSwitch = killSwitch;
        _state = state;
        _store = store;
        _state.StateChanged += (_, args) => StateChanged?.Invoke(this, args);
    }

    public ProgramState State => _state.State;
    public GameProfile? CurrentProfile
    {
        get
        {
            lock (_gate)
            {
                return _currentProfile;
            }
        }
    }

    public SessionRecord? CurrentSession
    {
        get
        {
            lock (_gate)
            {
                return _currentSession;
            }
        }
    }

    public bool IsTunnelActive => _tunnel.Active is not null;
    public ActiveTunnel? ActiveTunnel => _tunnel.Active;
    public ScoringOutput? LastRecommendation { get; private set; }

    private AppSettings Settings => _settingsProvider();

    // ================= Diagnóstico (sin túnel) =================

    public bool IsDiagnosticsRunning =>
        _state.State is ProgramState.ProbingDirect or ProgramState.ProbingRelays;

    public async Task<DiagnosticsReport> RunDiagnosticsAsync(
        GameProfile profile,
        bool deep,
        CancellationToken externalCt)
    {
        var startProblem = FindStartProblem(profile);
        if (startProblem.Length > 0)
        {
            throw new InvalidOperationException(startProblem);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var report = new DiagnosticsReport
        {
            GameName = profile.Name,
            TargetDisplay = PrimaryTargetDisplay(profile),
        };

        if (!_state.TryTransition(ProgramState.ProbingDirect, "diagnóstico: ruta directa"))
        {
            throw new InvalidOperationException("No se puede iniciar el diagnóstico ahora.");
        }

        try
        {
            var target = FirstUsableTarget(profile)!;
            var settings = Settings;
            var spec = BuildTargetSpec(target, settings);

            var direct = await _probeEngine.ProbeAsync(spec, deep, cts.Token).ConfigureAwait(false);
            report.Direct = direct.Summary;
            LogInfo($"Diagnóstico directo: {direct.Summary}");

            // Traceroute de la ruta directa.
            try
            {
                var trace = await _traceroute.TraceAsync(spec.Host, cts.Token).ConfigureAwait(false);
                report.Traceroute = trace;
                foreach (var obs in trace.Observations)
                {
                    report.Notes.Add(obs);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Notes.Add("Traceroute no disponible: " + ex.Message);
            }

            // Relays habilitados y no bloqueados, con endpoint válido para medir.
            var candidates = _relays.GetEnabled()
                .Where(r => r.HasEndpoint && !profile.BlockedRelayIds.Contains(r.Id))
                .ToList();
            var relaysSinEndpoint = _relays.GetEnabled()
                .Count(r => !r.HasEndpoint && !profile.BlockedRelayIds.Contains(r.Id));
            if (relaysSinEndpoint > 0)
            {
                report.Notes.Add(
                    $"{relaysSinEndpoint} relay(s) habilitado(s) sin endpoint válido se omitieron del diagnóstico; revísalos en la sección Relays.");
            }

            if (candidates.Count > 0 &&
                _state.TryTransition(ProgramState.ProbingRelays, "diagnóstico: relays"))
            {
                foreach (var relay in candidates)
                {
                    if (cts.IsCancellationRequested)
                    {
                        break;
                    }

                    var relaySpec = new ProbeTargetSpec
                    {
                        Label = $"relay {relay.Name}",
                        Host = relay.EndpointHost,
                        Kind = ProbeKind.Icmp,
                    };
                    var relayResult = await _probeEngine.ProbeAsync(relaySpec, deep, cts.Token).ConfigureAwait(false);
                    report.RelayMeasurements.Add(new RelayMeasurement
                    {
                        RelayId = relay.Id,
                        RelayName = relay.Name,
                        Endpoint = relayResult.Summary,
                    });
                    LogInfo($"Relay {relay.Name}: {relayResult.Summary}");
                }
            }

            // Recomendación honesta.
            var measurements = BuildMeasurements(report.Direct, report.RelayMeasurements, profile);
            var options = ScoringOptions.FromSettings(Settings.AutoSwitch, Settings.Probing);
            var evaluation = ScoreEngine.Evaluate(measurements, options);
            report.Recommendation = evaluation;
            LastRecommendation = evaluation;
            RecommendationUpdated?.Invoke(this, evaluation);

            if (evaluation.WinnerId == "direct")
            {
                report.Notes.Add("La ruta directa es la mejor opción: un relay no mejoraría la conexión.");
            }
            else if (evaluation.ChangeRecommended)
            {
                var winner = evaluation.Ranked.FirstOrDefault(c => c.CandidateId == evaluation.WinnerId);
                report.Notes.Add(winner is not null
                    ? $"El relay «{winner.DisplayName}» podría mejorar la conexión: {evaluation.ExplanationEs}"
                    : evaluation.ExplanationEs);
            }
            else if (evaluation.WinnerId is not null)
            {
                report.Notes.Add("Hay relays medidos, pero la mejora no es clara: " + evaluation.ExplanationEs);
            }

            DiagnosticsCompleted?.Invoke(this, report);
            return report;
        }
        catch (OperationCanceledException)
        {
            _state.TryTransition(ProgramState.Idle, "diagnóstico cancelado");
            throw;
        }
        catch (Exception ex)
        {
            LogError("Diagnóstico falló: " + ex.Message);
            _state.TryTransition(ProgramState.Error, "diagnóstico falló");
            throw;
        }
        finally
        {
            if (State is ProgramState.ProbingDirect or ProgramState.ProbingRelays)
            {
                _state.TryTransition(ProgramState.Idle, "diagnóstico terminado");
            }
        }
    }

    // ================= Optimización =================

    public bool IsOptimizing =>
        _state.State is not (ProgramState.Idle or ProgramState.Error);

    public void RequestOptimization(GameProfile profile, bool autoApproved = false)
    {
        lock (_gate)
        {
            if (_operationTask is { IsCompleted: false })
            {
                return;
            }

            var startProblem = FindStartProblem(profile);
            if (startProblem.Length > 0)
            {
                // Error del usuario, no del sistema: aviso claro y sin máquina de estados rota.
                _log.Warn(startProblem);
                _notifications.Warn("No se pudo iniciar la optimización", startProblem);
                return;
            }

            // Tras un error previo, una nueva petición explícita del usuario permite reintentar.
            if (_state.State == ProgramState.Error)
            {
                _state.TryTransition(ProgramState.Idle, "nueva optimización solicitada");
            }

            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            _currentProfile = profile;
            _userConfirmedRelay = autoApproved;
            _pendingRelayId = null;
            _operationTask = RunOptimizationWorkerAsync(_operationCts.Token);
        }
    }

    /// <summary>Confirma (desde la UI) el relay recomendado cuando el flujo está en WaitingUser.</summary>
    public void ConfirmPendingRelay(string? relayId = null)
    {
        lock (_gate)
        {
            if (_state.State != ProgramState.WaitingUser)
            {
                return;
            }

            _pendingRelayId = relayId ?? _pendingRelayId;
            _userConfirmedRelay = true;
        }
    }

    public async Task CancelWaitingUserAsync()
    {
        lock (_gate)
        {
            _userConfirmedRelay = false;
            _pendingRelayId = null;
        }

        await StopOptimizationAsync("usuario canceló la selección", emergency: false).ConfigureAwait(false);
    }

    public async Task StopOptimizationAsync(string reason, bool emergency = false)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _operationCts;
        }

        if (cts is null)
        {
            await CleanupTunnelAsync(reason).ConfigureAwait(false);
            _state.TryTransition(ProgramState.Idle, reason);
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignorado.
        }

        if (emergency)
        {
            // Emergencia: restaura la red aunque el worker esté bloqueado.
            _log.Warn("¡BOTÓN DE EMERGENCIA! Restaurando red…");
            await _killSwitch.ForceDisableAsync().ConfigureAwait(false);
            await _tunnel.DisconnectAsync("emergencia: restaurar red", CancellationToken.None)
                .ConfigureAwait(false);
            _notifications.Info("Red restaurada",
                "Se detuvo el túnel y se desactivó el kill switch. Revisa la conexión.");
        }

        try
        {
            if (_operationTask is { } task)
            {
                await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // El worker ya habrá registrado su error.
        }

        if (emergency || _state.State is ProgramState.Error)
        {
            _state.TryTransition(ProgramState.Idle, reason);
        }
    }

    public void Dispose()
    {
        try
        {
            StopOptimizationAsync("disposición de la aplicación", emergency: false)
                .GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Nunca lanzar desde Dispose.
        }

        _operationCts?.Dispose();
    }

    // ================= worker =================

    private async Task RunOptimizationWorkerAsync(CancellationToken ct)
    {
        var profile = _currentProfile!;
        var settings = Settings;

        // Preflight defensivo (RequestOptimization ya valida, pero el perfil podría haberse
        // editado entre la petición y el arranque del worker): fallo limpio, sin máquina de
        // estados rota ni perfil fantasma.
        var startProblem = FindStartProblem(profile);
        if (startProblem.Length > 0)
        {
            _log.Warn(startProblem);
            _notifications.Warn("No se pudo iniciar la optimización", startProblem);
            lock (_gate)
            {
                _operationCts?.Dispose();
                _operationCts = null;
                _operationTask = null;
                _currentProfile = null;
            }

            _state.TryTransition(ProgramState.Idle, "no se pudo iniciar la optimización");
            return;
        }

        var target = FirstUsableTarget(profile)!;
        var primarySpec = BuildTargetSpec(target, settings);
        _currentTargetDisplay = PrimaryTargetDisplay(profile);
        _lastSwitchUtc = null;
        _sustainedWinnerTicks = 0;
        _lastRecommendedWinner = null;
        _tunnelFailures = 0;
        _targetViaTunnelFailures = 0;
        _rollingDirect = null;
        _rollingOptimized = null;

        _currentSession = _sessions.StartSession(
            profile.Name, profile.Id, _currentTargetDisplay, profile.RouteMode);

        try
        {
            // 1) Medir ruta directa.
            if (!_state.TryTransition(ProgramState.ProbingDirect, "optimización: medir ruta directa"))
            {
                throw new InvalidOperationException("Estado no válido para iniciar la optimización.");
            }

            var directResult = await _probeEngine.ProbeAsync(primarySpec, deep: false, ct).ConfigureAwait(false);
            _rollingDirect = directResult.Summary;
            _currentSession!.DirectMetrics ??= directResult.Summary;
            _sessions.SetDirectMetrics(_currentSession, directResult.Summary);
            AddSessionEvent(SessionEventCategory.Info, $"Ruta directa medida: {directResult.Summary}");
            PublishMetrics();

            if (!directResult.Summary.Usable)
            {
                AddSessionEvent(SessionEventCategory.Warning,
                    "La ruta directa no es utilizable: " +
                    (directResult.Summary.UnavailableReason ?? "sin datos"));
                if (Settings.Probing.TcpFallbackWhenIcmpBlocked && directResult.Summary.Kind == ProbeKind.Icmp)
                {
                    // El motor ya reintentó con TCP; seguimos con lo que haya.
                }
            }

            // 2) Medir relays candidatos (habilitados, no bloqueados y con endpoint válido).
            var relayCandidates = _relays.GetEnabled()
                .Where(r => r.HasEndpoint && !profile.BlockedRelayIds.Contains(r.Id))
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Name)
                .ToList();
            var relaysSinEndpoint = _relays.GetEnabled()
                .Count(r => !r.HasEndpoint && !profile.BlockedRelayIds.Contains(r.Id));
            if (relaysSinEndpoint > 0)
            {
                AddSessionEvent(SessionEventCategory.Warning,
                    $"{relaysSinEndpoint} relay(s) habilitado(s) sin endpoint válido se omitieron; revísalos en la sección Relays.");
            }

            var relayMeasurements = new List<RelayMeasurement>();
            if (relayCandidates.Count > 0 &&
                _state.TryTransition(ProgramState.ProbingRelays, "optimización: medir relays"))
            {
                foreach (var relay in relayCandidates)
                {
                    ct.ThrowIfCancellationRequested();
                    var relaySpec = new ProbeTargetSpec
                    {
                        Label = $"relay {relay.Name}",
                        Host = relay.EndpointHost,
                        Kind = ProbeKind.Icmp,
                    };
                    var probe = await _probeEngine.ProbeAsync(relaySpec, deep: false, ct).ConfigureAwait(false);
                    relayMeasurements.Add(new RelayMeasurement
                    {
                        RelayId = relay.Id,
                        RelayName = relay.Name,
                        Endpoint = probe.Summary,
                    });
                    AddSessionEvent(SessionEventCategory.Info, $"Relay {relay.Name}: {probe.Summary}");
                }

                // Guardar salud en el propio relay.
                foreach (var m in relayMeasurements)
                {
                    var relay = _relays.GetById(m.RelayId);
                    if (relay is null || m.Endpoint is null)
                    {
                        continue;
                    }

                    relay.Health = new RelayHealth
                    {
                        MeasuredUtc = DateTimeOffset.UtcNow,
                        AvgLatencyMs = m.Endpoint.AvgMs,
                        LossPercent = m.Endpoint.LossPercent,
                        JitterMs = m.Endpoint.JitterMs,
                        Note = m.Endpoint.Note,
                    };
                    _relays.Save(relay, out _);
                }
            }

            // 3) Selección de ruta.
            _state.TryTransition(ProgramState.SelectingRoute, "optimización: seleccionar ruta");
            var selectedRelay = SelectRelay(profile, relayCandidates, relayMeasurements);
            if (selectedRelay is null && profile.RouteMode != RouteMode.Direct)
            {
                // Sin relay: seguir en directo y solo monitorear.
                AddSessionEvent(SessionEventCategory.Info,
                    "Sin relay utilizable; el modo seleccionado requiere un relay. Se mantiene ruta directa.");
            }

            var mode = profile.RouteMode == RouteMode.Direct
                ? RouteMode.Direct
                : RouteMode.TunnelGameDestinations;

            // 4) ¿Conectar túnel?
            if (selectedRelay is not null && profile.RouteMode != RouteMode.Direct)
            {
                if (!_userConfirmedRelay && !settings.AutoSwitch.Enabled)
                {
                    _pendingRelayId = selectedRelay.Id;
                    _state.TryTransition(ProgramState.WaitingUser,
                        $"relay «{selectedRelay.Name}» medido; esperando confirmación");

                    // Esperar confirmación o cancelación.
                    while (!_userConfirmedRelay)
                    {
                        await Task.Delay(300, ct).ConfigureAwait(false);
                    }

                    ct.ThrowIfCancellationRequested();
                    if (_pendingRelayId != selectedRelay.Id)
                    {
                        selectedRelay = _relays.GetById(_pendingRelayId) ?? selectedRelay;
                    }
                }

                await ConnectTunnelAsync(profile, selectedRelay, mode, ct).ConfigureAwait(false);
            }
            else
            {
                // Ruta directa: estado Active + monitoreo de comparación (diagnóstico continuo).
                _state.TryTransition(ProgramState.Active, "ruta directa seleccionada");
                _state.TryTransition(ProgramState.Monitoring, "monitoreo en ruta directa");
                AddSessionEvent(SessionEventCategory.Info,
                    "Optimización en modo directo: se monitorea la calidad para recomendar relays si mejoran la ruta.");
            }

            // 5) Bucle de monitoreo.
            await MonitorLoopAsync(profile, primarySpec, mode, relayCandidates, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AddSessionEvent(SessionEventCategory.Warning, "Optimización cancelada.");
            await CleanupTunnelAsync("usuario detuvo la optimización").ConfigureAwait(false);
            _state.TryTransition(ProgramState.Idle, "optimización detenida por el usuario");
        }
        catch (Exception ex)
        {
            _log.Error("Optimización falló: " + ex);
            AddSessionEvent(SessionEventCategory.Error, "Error: " + ex.Message);
            _notifications.Error("Error de optimización", ex.Message);
            await CleanupTunnelAsync("error en optimización").ConfigureAwait(false);
            _state.TryTransition(ProgramState.Error, "optimización falló: " + ex.Message);
        }
        finally
        {
            if (_currentSession is { } session)
            {
                _sessions.EndSession(session,
                    _state.State == ProgramState.Idle ? "detenida por el usuario" : "finalizada");
            }

            lock (_gate)
            {
                _currentProfile = null;
                _currentSession = null;
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }
    }

    private async Task ConnectTunnelAsync(
        GameProfile profile,
        RelayNode relay,
        RouteMode mode,
        CancellationToken ct)
    {
        var settings = Settings;

        // Validación de configuración del relay ANTES de intentar nada en la red:
        // errores claros en español en vez de fallos crípticos de la herramienta.
        if (!relay.HasEndpoint)
        {
            throw new InvalidOperationException(
                $"El relay «{relay.Name}» no tiene un endpoint válido (host:puerto). Corrígelo en Relays.");
        }

        if (!Tunneling.WireGuardConfigParser.IsValidKey(relay.PublicKey))
        {
            throw new InvalidOperationException(
                $"El relay «{relay.Name}» no tiene una clave pública válida. " +
                "Cópiala del servidor WireGuard ([Peer] PublicKey) o importa su .conf en Relays.");
        }

        // Clave privada: cargada o solicitada al usuario (evento → UI).
        if (!_relays.HasUsablePrivateKey(relay))
        {
            NeedsPrivateKey?.Invoke(this, relay);
            // El flujo pide la clave en la UI; si no llega, no se puede conectar.
            throw new InvalidOperationException(
                $"El relay «{relay.Name}» no tiene clave privada. Añádela en Relays o importa un archivo .conf.");
        }

        // Resolver IPs destino y comprobar cobertura del relay.
        var destHosts = profile.Targets
            .Select(t => !string.IsNullOrWhiteSpace(t.Domain) ? t.Domain!.Trim() : t.IpAddress?.Trim())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Cast<string>()
            .ToList();
        var destIps = await RouteCalculator.ResolveDestinationsAsync(destHosts, ct).ConfigureAwait(false);

        var config = WireGuardConfigBuilder.Build(relay, mode, destIps, settings.Tunnel.Mtu, settings.Tunnel.DnsOverride);
        if (config is null)
        {
            throw new InvalidOperationException("No se pudo construir la configuración del túnel.");
        }

        if (!_state.TryTransition(ProgramState.Connecting, $"conectando con {relay.Name}"))
        {
            return;
        }

        AddSessionEvent(SessionEventCategory.Tunnel, $"Conectando túnel WireGuard con «{relay.Name}» ({mode}).");
        _notifications.Info("Conectando túnel",
            $"Se solicitará permiso de administrador para crear el túnel con «{relay.Name}».");

        var tunnelName = "GRO-" + relay.Id[..Math.Min(6, relay.Id.Length)];
        var (ok, error) = await _tunnel.ConnectAsync(config, tunnelName, mode, relay.Id, relay.Name, ct)
            .ConfigureAwait(false);

        if (!ok)
        {
            _state.TryTransition(ProgramState.Error, "no se pudo conectar el túnel: " + error);
            throw new InvalidOperationException("No se pudo conectar el túnel: " + error);
        }

        // Kill switch opcional (requiere elevación; se activa después del túnel).
        if (settings.Tunnel.KillSwitchEnabled)
        {
            var ksResult = await _killSwitch.EnableAsync(tunnelName,
                new[] { relay.EndpointHost }, ct).ConfigureAwait(false);
            if (!ksResult.Ok)
            {
                _notifications.Warn("Kill switch no activado",
                    "No se pudo activar el kill switch: " + ksResult.Error +
                    ". Si el túnel se cae, la red volverá a salir por la ruta normal.");
            }
        }

        _currentSession!.RelayId = relay.Id;
        _currentSession.RelayName = relay.Name;
        _sessions.SetRelay(_currentSession, relay);

        _state.TryTransition(ProgramState.Active, "túnel activo");
        _lastSwitchUtc = DateTimeOffset.UtcNow;
        AddSessionEvent(SessionEventCategory.RouteChange, $"Túnel activo vía «{relay.Name}» (modo {mode}).");
        PublishMetrics();
    }

    private async Task MonitorLoopAsync(
        GameProfile profile,
        ProbeTargetSpec targetSpec,
        RouteMode mode,
        List<RelayNode> relays,
        CancellationToken ct)
    {
        var settings = Settings;
        var intervalSeconds = Math.Max(2, settings.Tunnel.HealthCheckIntervalSeconds);
        var activeRelay = _tunnel.Active is null ? null : _relays.GetById(_tunnel.Active.RelayId);

        AddSessionEvent(SessionEventCategory.State,
            activeRelay is null
                ? "Monitoreo en ruta directa: se comparará con relays medidos."
                : $"Monitoreo del túnel por «{activeRelay.Name}» cada {intervalSeconds} s.");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct).ConfigureAwait(false);

            var directProbe = await _probeEngine.ProbeAsync(targetSpec, deep: false, ct).ConfigureAwait(false);
            _rollingDirect = MergeRolling(_rollingDirect, directProbe.Summary);
            _currentSession!.DirectMetrics = _rollingDirect;
            _sessions.SetDirectMetrics(_currentSession, _rollingDirect);
            PublishMetrics();

            var tunnelActive = _tunnel.Active is not null;

            if (!tunnelActive)
            {
                // Modo directo: se siguen midiendo relays para poder recomendar.
                var relayMeasurements = new List<RelayMeasurement>();
                var enabled = relays.Where(r => r.Enabled && !profile.BlockedRelayIds.Contains(r.Id)).ToList();
                foreach (var relay in enabled)
                {
                    var probe = await _probeEngine.ProbeAsync(
                        new ProbeTargetSpec
                        {
                            Label = $"relay {relay.Name}",
                            Host = relay.EndpointHost,
                            Kind = ProbeKind.Icmp,
                        }, deep: false, ct).ConfigureAwait(false);
                    relayMeasurements.Add(new RelayMeasurement
                    {
                        RelayId = relay.Id,
                        RelayName = relay.Name,
                        Endpoint = probe.Summary,
                    });
                }

                var measurements = BuildMeasurements(_rollingDirect, relayMeasurements, profile);
                var options = ScoringOptions.FromSettings(Settings.AutoSwitch, Settings.Probing);
                var evaluation = ScoreEngine.Evaluate(measurements, options, currentId: "direct");
                LastRecommendation = evaluation;
                RecommendationUpdated?.Invoke(this, evaluation);

                var decision = AutoSwitchPolicy.ShouldSwitch(evaluation, DateTimeOffset.UtcNow,
                    _lastSwitchUtc, _sustainedWinnerTicks, Settings.AutoSwitch, intervalSeconds);
                if (decision.Kind == SwitchDecisionKind.SwitchToWinner)
                {
                    // Auto-switch a relay.
                    var winnerRelay = _relays.GetById(evaluation.WinnerId?.Replace("relay:", string.Empty));
                    if (winnerRelay is not null && _userConfirmedRelay)
                    {
                        await ConnectTunnelAsync(profile, winnerRelay, mode, ct).ConfigureAwait(false);
                        activeRelay = winnerRelay;
                    }
                    else if (winnerRelay is not null)
                    {
                        _state.TryTransition(ProgramState.WaitingUser, "relay recomendado; esperando confirmación");
                        _notifications.Info("Relay recomendado",
                            $"«{winnerRelay.Name}» parece mejor que la ruta directa. Confírmalo en el panel.");
                    }
                }
                else
                {
                    _sustainedWinnerTicks = 0;
                }

                continue;
            }

            // ---- Túnel activo: salud del túnel y comparación directo vs túnel ----
            var tunnelStatus = await _tunnel.GetStatusAsync(ct).ConfigureAwait(false);
            var tunnelHealthy = tunnelStatus.State is TunnelProviderState.Active;

            // Probe al destino a través del túnel (cuando el túnel enruta el destino, la probe viaja
            // por WireGuard). Como referencia de "ruta directa" se usa la muestra anterior al túnel.
            var optimizedProbe = await _probeEngine.ProbeAsync(targetSpec, deep: false, ct).ConfigureAwait(false);
            var targetOk = optimizedProbe.Summary.Successes > 0;

            _rollingOptimized = MergeRolling(_rollingOptimized, optimizedProbe.Summary);
            _currentSession!.OptimizedMetrics = _rollingOptimized;
            _sessions.SetOptimizedMetrics(_currentSession, _rollingOptimized);

            var failback = FailbackPolicy.ShouldFailback(
                tunnelHealthy,
                targetOk,
                _tunnelFailures,
                _targetViaTunnelFailures,
                _rollingDirect,
                _rollingOptimized,
                Settings.AutoSwitch,
                DateTimeOffset.UtcNow);

            if (!tunnelHealthy)
            {
                _tunnelFailures++;
                if (_state.State != ProgramState.Degraded)
                {
                    _state.TryTransition(ProgramState.Degraded, "el túnel no responde");
                }
            }
            else
            {
                _tunnelFailures = Math.Max(0, _tunnelFailures - 1);
            }

            _targetViaTunnelFailures = targetOk ? 0 : _targetViaTunnelFailures + 1;

            PublishMetrics();

            if (failback.Kind != FailbackKind.None)
            {
                _notifications.Warn("Túnel degradado", failback.ReasonEs);
                AddSessionEvent(SessionEventCategory.Tunnel, "Failback: " + failback.ReasonEs);
                if (failback.Kind == FailbackKind.TunnelWorse && !Settings.AutoSwitch.AutoFailback)
                {
                    // Sin auto-failback: avisamos y dejamos decidir al usuario (la sesión sigue).
                    AddSessionEvent(SessionEventCategory.Warning,
                        "El túnel empeora la conexión. Usa «Detener» o confirma el cambio a ruta directa.");
                    _state.TryTransition(ProgramState.Degraded, "el túnel empeora la conexión");
                    continue;
                }

                await FailbackToDirectAsync("failback: " + failback.ReasonEs, ct).ConfigureAwait(false);
                activeRelay = null;
                continue;
            }

            // Auto-switch entre relays (o a directo si mejora claramente).
            var relayMeasurements2 = new List<RelayMeasurement>();
            foreach (var relay in relays.Where(r => r.Enabled && !profile.BlockedRelayIds.Contains(r.Id)))
            {
                var probe = await _probeEngine.ProbeAsync(
                    new ProbeTargetSpec
                    {
                        Label = $"relay {relay.Name}",
                        Host = relay.EndpointHost,
                        Kind = ProbeKind.Icmp,
                    }, deep: false, ct).ConfigureAwait(false);
                relayMeasurements2.Add(new RelayMeasurement
                {
                    RelayId = relay.Id,
                    RelayName = relay.Name,
                    Endpoint = probe.Summary,
                });
            }

            var measurements2 = new List<CandidateMeasurement>();
            // Candidato actual (túnel activo): medición a través del túnel.
            var activeTunnel = _tunnel.Active!;
            if (_rollingOptimized is { Usable: true })
            {
                measurements2.Add(new CandidateMeasurement
                {
                    CandidateId = "relay:" + activeTunnel.RelayId,
                    DisplayName = "Túnel actual (" + activeTunnel.RelayName + ")",
                    Kind = RouteCandidateKind.Relay,
                    RelayId = activeTunnel.RelayId,
                    Measured = _rollingOptimized,
                });
            }

            // Ruta directa (referencia histórica).
            measurements2.Add(new CandidateMeasurement
            {
                CandidateId = "direct",
                DisplayName = "Ruta directa",
                Kind = RouteCandidateKind.Direct,
                Measured = _rollingDirect,
            });

            // Otros relays (solo endpoint medido → penalización de tramo desconocido).
            foreach (var m in relayMeasurements2.Where(m => m.RelayId != activeTunnel.RelayId))
            {
                var relay = _relays.GetById(m.RelayId);
                if (relay is null)
                {
                    continue;
                }

                var learning = LearningsFor(relay.Id, TargetRegion(profile));
                measurements2.Add(new CandidateMeasurement
                {
                    CandidateId = "relay:" + relay.Id,
                    DisplayName = "Relay " + relay.Name,
                    Kind = RouteCandidateKind.Relay,
                    RelayId = relay.Id,
                    Measured = m.Endpoint,
                    LearnedTailMs = learning,
                    LoadPercent = relay.EstimatedLoadPercent,
                    Availability = relay.Availability,
                    BlockedForGame = profile.BlockedRelayIds.Contains(relay.Id),
                });
            }

            var options2 = ScoringOptions.FromSettings(Settings.AutoSwitch, Settings.Probing);
            var evaluation2 = ScoreEngine.Evaluate(measurements2, options2,
                currentId: "relay:" + activeTunnel.RelayId, isTunnelActive: true);
            LastRecommendation = evaluation2;
            RecommendationUpdated?.Invoke(this, evaluation2);

            if (evaluation2.WinnerId == "direct" &&
                evaluation2.ChangeRecommended)
            {
                _sustainedWinnerTicks =
                    _lastRecommendedWinner == "direct" ? _sustainedWinnerTicks + 1 : 1;
                _lastRecommendedWinner = "direct";

                var decision = AutoSwitchPolicy.ShouldSwitch(evaluation2, DateTimeOffset.UtcNow,
                    _lastSwitchUtc, _sustainedWinnerTicks, Settings.AutoSwitch, intervalSeconds);
                if (decision.Kind == SwitchDecisionKind.SwitchToWinner)
                {
                    await FailbackToDirectAsync("la ruta directa es claramente mejor", ct).ConfigureAwait(false);
                    activeRelay = null;
                    _sustainedWinnerTicks = 0;
                }
            }
            else if (evaluation2.WinnerId?.StartsWith("relay:", StringComparison.Ordinal) == true &&
                     evaluation2.WinnerId != "relay:" + activeTunnel.RelayId)
            {
                // Otro relay parece claramente mejor (endpoint medido + tramo aprendido o confirmación).
                var winnerRelay = _relays.GetById(evaluation2.WinnerId["relay:".Length..]);
                if (winnerRelay is not null)
                {
                    var decision = AutoSwitchPolicy.ShouldSwitch(evaluation2, DateTimeOffset.UtcNow,
                        _lastSwitchUtc, _sustainedWinnerTicks, Settings.AutoSwitch, intervalSeconds);
                    if (decision.Kind == SwitchDecisionKind.SwitchToWinner && _userConfirmedRelay)
                    {
                        AddSessionEvent(SessionEventCategory.RouteChange,
                            $"Cambiando de relay a «{winnerRelay.Name}»…");
                        await DisconnectTunnelForSwitchAsync(ct).ConfigureAwait(false);
                        await ConnectTunnelAsync(profile, winnerRelay, mode, ct).ConfigureAwait(false);
                        activeRelay = winnerRelay;
                        _sustainedWinnerTicks = 0;
                    }
                }
            }
            else
            {
                _sustainedWinnerTicks = 0;
            }
        }
    }

    private async Task DisconnectTunnelForSwitchAsync(CancellationToken ct)
    {
        if (!_state.TryTransition(ProgramState.Switching, "cambiando de relay"))
        {
            return;
        }

        await CleanupTunnelAsync("cambio de relay").ConfigureAwait(false);
        _state.TryTransition(ProgramState.Connecting, "conectando nuevo relay");
    }

    private async Task FailbackToDirectAsync(string reason, CancellationToken ct)
    {
        if (!_state.TryTransition(ProgramState.FailingBack, reason))
        {
            return;
        }

        AddSessionEvent(SessionEventCategory.RouteChange, "Volviendo a la ruta directa: " + reason);
        _notifications.Info("Vuelta a ruta directa", reason);
        await CleanupTunnelAsync(reason).ConfigureAwait(false);
        _rollingOptimized = null;
        _state.TryTransition(ProgramState.Active, "ruta directa");
        _state.TryTransition(ProgramState.Monitoring, "monitoreo en ruta directa");
        _lastSwitchUtc = DateTimeOffset.UtcNow;
    }

    private async Task CleanupTunnelAsync(string reason)
    {
        // Orden seguro: primero kill switch (deja de bloquear), luego túnel (quita rutas/DNS).
        // Timeout amplio: si la elevación (UAC) queda sin responder, no bloquear la app.
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await _killSwitch.DisableAsync(cleanupCts.Token).ConfigureAwait(false);
        var (_, error) = await _tunnel.DisconnectAsync(reason, cleanupCts.Token).ConfigureAwait(false);
        if (error is not null)
        {
            _log.Error("No se pudo detener el túnel limpiamente: " + error);
            AddSessionEvent(SessionEventCategory.Error,
                "No se pudo detener el túnel limpiamente: " + error +
                ". Usa el botón de emergencia o el script de restauración (docs/tunel-admin.md).");
        }
    }

    // ================= helpers =================

    private RelayNode? SelectRelay(GameProfile profile, List<RelayNode> candidates, List<RelayMeasurement> measured)
    {
        if (profile.PreferredRelayId is { } preferred)
        {
            var preferredRelay = candidates.FirstOrDefault(r => r.Id == preferred);
            if (preferredRelay is not null && !profile.BlockedRelayIds.Contains(preferred))
            {
                return preferredRelay;
            }
        }

        // El mejor relay medido.
        return measured
            .Where(m => m.Endpoint is { Usable: true })
            .OrderBy(m => m.Endpoint!.AvgMs)
            .Select(m => _relays.GetById(m.RelayId))
            .FirstOrDefault(r => r is not null && !profile.BlockedRelayIds.Contains(r!.Id))!;
    }

    private List<CandidateMeasurement> BuildMeasurements(
        ProbeSummary? direct,
        List<RelayMeasurement> relayMeasurements,
        GameProfile profile)
    {
        var list = new List<CandidateMeasurement>();
        if (direct is not null)
        {
            list.Add(new CandidateMeasurement
            {
                CandidateId = "direct",
                DisplayName = "Ruta directa",
                Kind = RouteCandidateKind.Direct,
                Measured = direct,
            });
        }

        var region = TargetRegion(profile);
        foreach (var m in relayMeasurements)
        {
            var relay = _relays.GetById(m.RelayId);
            if (relay is null)
            {
                continue;
            }

            list.Add(new CandidateMeasurement
            {
                CandidateId = "relay:" + relay.Id,
                DisplayName = "Relay " + relay.Name,
                Kind = RouteCandidateKind.Relay,
                RelayId = relay.Id,
                Measured = m.Endpoint,
                LearnedTailMs = LearningsFor(relay.Id, region),
                LoadPercent = relay.EstimatedLoadPercent,
                Availability = relay.Availability,
                BlockedForGame = profile.BlockedRelayIds.Contains(relay.Id),
            });
        }

        return list;
    }

    private double? LearningsFor(string relayId, string region)
    {
        try
        {
            return _store.LoadLearnings()
                .FirstOrDefault(l => l.RelayId == relayId && l.Region == region)
                ?.TailAvgMs;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Problema que impide optimizar/diagnosticar un perfil, o cadena vacía si es válido.
    /// Usado por la UI (mensajes claros) y por el orquestador (preflight).
    /// </summary>
    public static string FindStartProblem(GameProfile profile)
    {
        if (profile is null)
        {
            return "Selecciona un juego primero.";
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return "El juego no tiene nombre; asígnale uno en la sección Juegos.";
        }

        if (FirstUsableTarget(profile) is null)
        {
            return profile.Targets.Count == 0
                ? $"El perfil «{profile.Name}» no tiene servidores objetivo. Añade al menos uno (dominio o IP) en la sección Juegos."
                : $"El perfil «{profile.Name}» no tiene ningún servidor objetivo con dominio o IP válidos. Revísalos en la sección Juegos.";
        }

        return string.Empty;
    }

    /// <summary>Primer objetivo del perfil con dominio o IP utilizable (null si no hay).</summary>
    private static GameServerTarget? FirstUsableTarget(GameProfile profile)
    {
        foreach (var target in profile.Targets)
        {
            var host = !string.IsNullOrWhiteSpace(target.Domain)
                ? target.Domain!.Trim()
                : target.IpAddress?.Trim();
            if (!string.IsNullOrWhiteSpace(host))
            {
                return target;
            }
        }

        return null;
    }

    private static string TargetRegion(GameProfile profile) =>
        profile.Targets.FirstOrDefault()?.Region ?? "desconocida";

    private static string PrimaryTargetDisplay(GameProfile profile)
    {
        var target = profile.Targets.FirstOrDefault();
        return target is null ? "(sin destino)" : target.DisplayHost;
    }

    private static ProbeTargetSpec BuildTargetSpec(GameServerTarget target, AppSettings settings)
    {
        var host = !string.IsNullOrWhiteSpace(target.Domain)
            ? target.Domain!.Trim()
            : target.IpAddress!.Trim();
        var preferred = settings.Probing.UseIcmp ? target.DefaultProbe : ProbeKind.TcpConnect;
        var effectiveKind = target.DefaultProbe switch
        {
            ProbeKind.HttpGet => ProbeKind.HttpGet,
            ProbeKind.Udp => ProbeKind.Udp,
            _ => preferred == ProbeKind.Icmp ? ProbeKind.Icmp : ProbeKind.TcpConnect,
        };
        if (effectiveKind == ProbeKind.Icmp && !settings.Probing.UseIcmp)
        {
            effectiveKind = ProbeKind.TcpConnect;
        }

        var port = target.Ports.FirstOrDefault(p => p > 0) > 0
            ? target.Ports.First(p => p > 0)
            : settings.Probing.TcpDefaultPort;

        return new ProbeTargetSpec
        {
            Label = host,
            Host = host,
            Kind = effectiveKind,
            Port = port,
            HttpUrl = target.HttpHealthUrl,
        };
    }

    private static ProbeSummary? MergeRolling(ProbeSummary? rolling, ProbeSummary sample)
    {
        if (!sample.HasData)
        {
            return rolling;
        }

        if (rolling is null || !rolling.HasData || !sample.Usable)
        {
            return sample;
        }

        // Media móvil exponencial ligera para suavizar la serie en vivo.
        const double alpha = 0.4;
        return new ProbeSummary
        {
            TargetLabel = sample.TargetLabel,
            Kind = sample.Kind,
            Attempts = sample.Attempts,
            Successes = sample.Successes,
            Failures = sample.Failures,
            AvgMs = rolling.AvgMs.HasValue
                ? alpha * sample.AvgMs + (1 - alpha) * rolling.AvgMs
                : sample.AvgMs,
            MinMs = rolling.MinMs.HasValue && sample.MinMs.HasValue
                ? Math.Min(rolling.MinMs.Value, sample.MinMs.Value)
                : sample.MinMs,
            MaxMs = rolling.MaxMs.HasValue && sample.MaxMs.HasValue
                ? Math.Max(rolling.MaxMs.Value, sample.MaxMs.Value)
                : sample.MaxMs,
            P95Ms = sample.P95Ms,
            P99Ms = sample.P99Ms,
            JitterMs = sample.JitterMs,
            StdDevMs = sample.StdDevMs,
            LossPercent = rolling.LossPercent.HasValue && sample.LossPercent.HasValue
                ? alpha * sample.LossPercent + (1 - alpha) * rolling.LossPercent
                : sample.LossPercent,
            IcmpReliable = sample.IcmpReliable,
            Note = sample.Note,
        };
    }

    private void PublishMetrics()
    {
        MetricsUpdated?.Invoke(this, new MetricsSnapshot
        {
            Direct = _rollingDirect,
            Optimized = _rollingOptimized,
            RelayEndpoint = null,
            RelayName = _tunnel.Active?.RelayName,
            TunnelActive = _tunnel.Active is not null,
        });
    }

    private void AddSessionEvent(SessionEventCategory category, string message)
    {
        if (_currentSession is { } session)
        {
            _sessions.AppendEvent(session, category, message, _state.State.ToString());
            SessionEventAdded?.Invoke(this,
                session.Events[^1]);
        }
    }

    private void LogInfo(string message)
    {
        _log.Info(message);
    }

    private void LogError(string message)
    {
        _log.Error(message);
    }

    public event EventHandler<RelayNode>? NeedsPrivateKey;
}
