using GameRouteOptimizer.Core.AutoSwitch;
using GameRouteOptimizer.Core.Events;
using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Probing;
using GameRouteOptimizer.Core.Relay;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Scoring;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.StateMachine;
using GameRouteOptimizer.Core.Tunneling;

namespace GameRouteOptimizer.Core.Services;

/// <summary>Opciones de una optimización.</summary>
public sealed class OptimizationOptions
{
    public bool Automatic { get; set; }

    /// <summary>Relay elegido manualmente (si Automatic = false).</summary>
    public string? ManualRelayId { get; set; }

    public ProbeMode ProbeMode { get; set; } = ProbeMode.Quick;
}

/// <summary>Live metrics para la UI.</summary>
public sealed class LiveMetrics
{
    public double DirectPingMs { get; set; } = double.NaN;

    public double OptimizedPingMs { get; set; } = double.NaN;

    public double DirectLoss { get; set; } = double.NaN;

    public double OptimizedLoss { get; set; } = double.NaN;

    public double JitterMs { get; set; } = double.NaN;

    public double Stability { get; set; } = double.NaN;

    public List<double> LatencySeries { get; } = new();

    public List<double> LossSeries { get; } = new();
}

/// <summary>
/// Orquestador principal: coordina la máquina de estados, probing, scoring, túnel,
/// monitoreo y auto-switch para los tres modos (diagnóstico, manual, automático).
/// </summary>
public sealed class OptimizerOrchestrator : IDisposable
{
    private readonly EventBus _events;
    private readonly OptimizerStateMachine _stateMachine;
    private readonly ProbeEngine _probeEngine;
    private readonly TracerouteEngine _traceroute;
    private readonly RouteAnalyzer _analyzer;
    private readonly ITunnelManager _tunnelManager;
    private readonly SessionRecorder _sessions;
    private readonly AppSettings _settings;
    private readonly HysteresisPolicy _hysteresis;
    private readonly Func<RelayNode, string?> _privateKeyResolver;

    private CancellationTokenSource? _sessionCts;
    private readonly object _gate = new();

    public OptimizerOrchestrator(
        EventBus events,
        OptimizerStateMachine stateMachine,
        ProbeEngine probeEngine,
        TracerouteEngine traceroute,
        RouteAnalyzer analyzer,
        ITunnelManager tunnelManager,
        SessionRecorder sessions,
        AppSettings settings,
        Func<RelayNode, string?>? privateKeyResolver = null)
    {
        _events = events;
        _stateMachine = stateMachine;
        _probeEngine = probeEngine;
        _traceroute = traceroute;
        _analyzer = analyzer;
        _tunnelManager = tunnelManager;
        _sessions = sessions;
        _settings = settings;
        _hysteresis = new HysteresisPolicy(HysteresisSettings.FromSettings(settings));
        _privateKeyResolver = privateKeyResolver ?? (_ => null);

        _tunnelManager.StatusChanged += OnTunnelStatusChanged;
    }

    public OptimizerStateMachine StateMachine => _stateMachine;

    public LiveMetrics Metrics { get; } = new();

    public string? ActiveSessionId => _sessions.Current?.Id;

    public bool IsRunning
    {
        get
        {
            var state = _stateMachine.Current;
            return state is not OptimizerState.Idle and not OptimizerState.Error and not OptimizerState.WaitingUser;
        }
    }

    // ---------- MODO 1: DIAGNÓSTICO ----------

    /// <summary>Diagnóstico completo de un servidor sin túnel: probes + traceroute + recomendación.</summary>
    public async Task<(ProbeSummary Probes, TracerouteResult Trace, RouteRecommendation Recommendation)>
        RunDiagnosticsAsync(GameProfile profile, GameServer server, IReadOnlyList<RelayNode> relays,
            ProbeMode mode, CancellationToken ct = default)
    {
        await _stateMachine.TransitionToAsync(OptimizerState.ProbingDirect, async _ =>
        {
            PublishInfo($"Diagnóstico iniciado hacia {server.Endpoint} (modo {(mode == ProbeMode.Quick ? "rápido" : "profundo")}).");
            await Task.CompletedTask;
        }, ct);

        ProbeSummary probes;
        var recommendation = new RouteRecommendation { SimpleExplanation = "Solo ruta directa medida (sin relays configurados)." };

        try
        {
            probes = await _probeEngine.ProbeAsync(server, mode, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            probes = ProbeSummary.Unreachable(server.Endpoint, 0, $"Error de medición: {ex.Message}");
        }

        // Traceroute (puede fallar por permisos: se reporta sin tumbar el diagnóstico).
        TracerouteResult trace;
        try
        {
            trace = await _traceroute.TraceAsync(server.Endpoint, ct);
            PublishInfo("Traceroute: " + trace.Explain());
        }
        catch (Exception ex)
        {
            trace = new TracerouteResult { Target = server.Endpoint };
            PublishWarning($"Traceroute no disponible: {ex.Message}");
        }

        if (relays.Count > 0)
        {
            try
            {
                recommendation = await _analyzer.CompareAsync(profile, server, relays, mode, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                PublishWarning($"No se pudo comparar con relays: {ex.Message}");
            }
        }

        var icmpNote = probes.IcmpUnreliable
            ? " Aviso: ICMP no fiable hacia este destino; considera TCP connect en la configuración del servidor."
            : "";

        PublishInfo($"Diagnóstico completado: media {probes.MeanMs:0.0} ms, pérdida {probes.LossRatio:P1}, jitter {probes.JitterMs:0.0} ms.{icmpNote}");
        await _stateMachine.TransitionToAsync(OptimizerState.Stopping);
        await _stateMachine.TransitionToAsync(OptimizerState.Idle);
        return (probes, trace, recommendation);
    }

    // ---------- MODO 2 y 3: OPTIMIZACIÓN ----------

    /// <summary>
    /// Inicia una sesión de optimización: mide, puntúa, selecciona (auto o manual),
    /// conecta el túnel y entra en monitoreo con auto-switch si corresponde.
    /// Cancelable en cualquier momento.
    /// </summary>
    public async Task StartOptimizationAsync(
        GameProfile profile, GameServer server, IReadOnlyList<RelayNode> relays,
        OptimizationOptions options, CancellationToken externalCt = default)
    {
        lock (_gate)
        {
            if (IsRunning) throw new InvalidOperationException("Ya hay una optimización en curso. Detén primero.");
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        }

        var ct = _sessionCts!.Token;
        var sessionMode = options.Automatic ? "Automatic" : "Manual";
        _sessions.StartSession(profile, server, sessionMode);
        Metrics.LatencySeries.Clear();
        Metrics.LossSeries.Clear();

        // 1) Probing directo.
        await _stateMachine.TransitionToAsync(OptimizerState.ProbingDirect, async _ =>
        {
            PublishInfo("Midiendo ruta directa…");
            await Task.CompletedTask;
        }, ct);
        var direct = await _analyzer.ProbeDirectAsync(server, options.ProbeMode, ct);
        _sessions.SetDirectBaseline(new ProbeSummary
        {
            Target = server.Endpoint,
            MeanMs = direct.Unreachable ? double.NaN : direct.LatencyMs,
            LossRatio = direct.LossRatio,
            JitterMs = direct.JitterMs,
            StabilityScore = direct.StabilityScore,
            SuccessCount = direct.SampleCount,
            TotalProbes = Math.Max(direct.SampleCount, 1)
        });
        Metrics.DirectPingMs = direct.Unreachable ? double.NaN : direct.LatencyMs;
        Metrics.DirectLoss = direct.LossRatio;
        PublishInfo($"Ruta directa: {direct.LatencyMs:0.0} ms, pérdida {direct.LossRatio:P1}.");

        // 2) Probing de relays + selección.
        RouteCandidate? selected = null;
        RouteRecommendation? recommendation = null;

        if (!string.IsNullOrWhiteSpace(options.ManualRelayId))
        {
            var relay = relays.FirstOrDefault(r => r.Id == options.ManualRelayId)
                        ?? throw new KeyNotFoundException("El relay manual no existe.");
            await _stateMachine.TransitionToAsync(OptimizerState.ProbingRelays, null, ct);
            var candidate = await _analyzer.ProbeRelayAsync(relay, server, options.ProbeMode, ct);
            selected = candidate;
            recommendation = new RouteRecommendation
            {
                Best = candidate,
                BestScore = new RouteScore { CandidateId = candidate.CandidateId, Value = 100, Explanation = "Selección manual del usuario.", Confidence = 1 },
                Candidates = { direct, candidate }
            };
            PublishInfo($"Relay manual '{relay.Name}' seleccionado por el usuario.");
        }
        else
        {
            await _stateMachine.TransitionToAsync(OptimizerState.ProbingRelays, async _ =>
            {
                PublishInfo("Midiendo relays…");
                await Task.CompletedTask;
            }, ct);
            recommendation = await _analyzer.CompareAsync(profile, server, relays, options.ProbeMode, ct);
            selected = recommendation.Best;

            if (recommendation.RecommendDirect && selected?.IsDirect == true)
            {
                PublishInfo($"Recomendación: mantener la ruta directa. {recommendation.SimpleExplanation}");
                _sessions.Record(EventSeverity.Info, EventCategory.Route,
                    "La ruta directa es la mejor opción; no se activa túnel.");
                await StopInternalAsync("Completed");
                return;
            }
            if (selected is null || selected.IsDirect)
            {
                PublishWarning("Ningún relay mejora la ruta directa. No se activa túnel.");
                _sessions.Record(EventSeverity.Warning, EventCategory.Route,
                    "Ningún relay supera a la ruta directa.");
                await StopInternalAsync("Completed");
                return;
            }
            PublishSuccess($"Selección: {selected.DisplayName}. {recommendation.SimpleExplanation} (confianza {recommendation.Confidence:P0})");
        }

        if (selected is null || selected.Relay is null)
        {
            await StopInternalAsync("Completed");
            return;
        }

        _sessions.SetSelectedCandidate(selected.CandidateId, selected.Relay.Name);

        // 3) Advertencia de túnel global si corresponde.
        if (profile.RoutingMode == RoutingMode.TunnelGlobal)
            PublishWarning("El túnel será GLOBAL (todo tu tráfico pasará por el relay). Usa 'solo juego' si prefieres dividir.");

        // 4) Conexión del túnel (delegada al manager: local en Servicio/CLI, IPC desde la UI).
        await _stateMachine.TransitionToAsync(OptimizerState.Connecting, null, ct);
        var status = await ConnectWithBackoffAsync(profile, selected.Relay, server, ct);
        if (!status.IsConnected)
        {
            _sessions.IncrementTunnelDrop();
            PublishWarning("No se pudo establecer el túnel estable. Restaurando ruta directa…");
            await StopInternalAsync("Failed");
            return;
        }

        // 5) Monitoreo continuo (auto-switch / failback).
        await _stateMachine.TransitionToAsync(OptimizerState.Active);
        PublishSuccess($"Túnel activo vía {selected.DisplayName}. Monitoreando…");
        _ = Task.Run(() => MonitorLoopAsync(profile, server, relays, selected, ct), ct);
    }

    /// <summary>Conecta con reintentos y backoff exponencial (2s, 4s, 8s… máx 30s).</summary>
    private async Task<TunnelStatus> ConnectWithBackoffAsync(GameProfile profile, RelayNode relay, GameServer server, CancellationToken ct)
    {
        var attempt = 0;
        var delayMs = 2000;
        while (attempt < Math.Max(1, _settings.MaxReconnectAttempts))
        {
            ct.ThrowIfCancellationRequested();
            attempt++;
            PublishInfo($"Conectando al relay '{relay.Name}' (intento {attempt})…");

            var tunnel = BuildTunnelProfile(profile, relay);
            var status = await _tunnelManager.StartAsync(new TunnelStartRequest
            {
                Tunnel = tunnel,
                TunnelName = "gameroute-" + relay.Id[..6],
                Mtu = _settings.TunnelMtu
            }, ct);

            if (status.IsConnected)
                return status;

            PublishWarning($"Intento {attempt} falló: {status.Detail}. Reintento en {delayMs / 1000} s…");
            try { await Task.Delay(delayMs, ct); }
            catch (OperationCanceledException) { throw; }
            delayMs = Math.Min(delayMs * 2, 30_000);
        }
        return new TunnelStatus { State = TunnelState.Failed, Detail = "Agotados los reintentos de conexión." };
    }

    /// <summary>Construye el TunnelProfile a partir del relay (clave privada cifrada → descifrada aquí, nunca en logs).</summary>
    private TunnelProfile BuildTunnelProfile(GameProfile profile, RelayNode relay)
    {
        var privateKey = _privateKeyResolver(relay);
        return new TunnelProfile
        {
            Name = "gameroute-" + relay.Id[..6],
            PrivateKey = privateKey ?? string.Empty,
            Address = relay.AllowedIps.Count > 0 && relay.AllowedIps[0].EndsWith("/32") == false
                ? relay.AllowedIps[0] : "10.66.66.2/32",
            Dns = string.IsNullOrWhiteSpace(_settings.TunnelDns) ? relay.InternalDns : _settings.TunnelDns,
            Mtu = _settings.TunnelMtu,
            PublicKey = relay.PublicKey,
            AllowedIps = profile.RoutingMode == RoutingMode.TunnelGlobal
                ? new List<string> { "0.0.0.0/0" }
                : new List<string>(relay.AllowedIps),
            Endpoint = $"{relay.EndpointHost}:{relay.EndpointPort}",
            PersistentKeepalive = 25
        };
    }

    /// <summary>Bucle de monitoreo: probes ligeras, detección de degradación, auto-switch con histéresis y failback.</summary>
    private async Task MonitorLoopAsync(GameProfile profile, GameServer server, IReadOnlyList<RelayNode> relays,
        RouteCandidate current, CancellationToken ct)
    {
        var lastSwitchUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
        var switchesLastHour = 0;
        var bestConsistentSince = DateTimeOffset.UtcNow;
        var bestSinceCandidateId = current.CandidateId;
        var lastSample = DateTimeOffset.UtcNow;

        await _stateMachine.TransitionToAsync(OptimizerState.Monitoring);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_settings.MonitorIntervalMs, ct);

                // Métricas en vivo del camino optimizado.
                var optimized = await _analyzer.ProbeRelayAsync(current.Relay!, server, ProbeMode.Quick, ct);
                Metrics.OptimizedPingMs = optimized.Unreachable ? double.NaN : optimized.LatencyMs;
                Metrics.OptimizedLoss = optimized.LossRatio;
                Metrics.JitterMs = optimized.JitterMs;
                Metrics.Stability = optimized.StabilityScore;
                lock (Metrics.LatencySeries)
                {
                    Metrics.LatencySeries.Add(double.IsNaN(Metrics.OptimizedPingMs) ? 0 : Metrics.OptimizedPingMs);
                    Metrics.LossSeries.Add(optimized.LossRatio * 100);
                    while (Metrics.LatencySeries.Count > 120) Metrics.LatencySeries.RemoveAt(0);
                    while (Metrics.LossSeries.Count > 120) Metrics.LossSeries.RemoveAt(0);
                }
                _sessions.SetOptimized(new ProbeSummary
                {
                    Target = server.Endpoint,
                    MeanMs = optimized.LatencyMs,
                    LossRatio = optimized.LossRatio,
                    JitterMs = optimized.JitterMs,
                    StabilityScore = optimized.StabilityScore,
                    SuccessCount = optimized.SampleCount,
                    TotalProbes = Math.Max(optimized.SampleCount, 1)
                });

                // Estado del túnel.
                var status = await _tunnelManager.GetStatusAsync(ct);
                if (status.State is TunnelState.Failed or TunnelState.Reconnecting or TunnelState.Disconnected)
                {
                    _sessions.IncrementTunnelDrop();
                    PublishWarning("Caída del túnel detectada.");
                    var decisionDown = _hysteresis.Evaluate(new AutoSwitchContext { TunnelDown = true });
                    if (decisionDown.Decision == AutoSwitchDecision.Failback)
                    {
                        await FailbackAsync("Failback a ruta directa tras caída del túnel.", ct);
                        return;
                    }
                    continue;
                }

                if (!_settings.AutoSwitchEnabled) continue;

                // Re-evaluar candidatos periódicamente (directa + relay actual + resto).
                var direct = await _analyzer.ProbeDirectAsync(server, ProbeMode.Quick, ct);
                Metrics.DirectPingMs = direct.Unreachable ? double.NaN : direct.LatencyMs;
                var candidates = new List<RouteCandidate> { direct, optimized };
                foreach (var relay in relays.Where(r => r.IsEnabled && r.Id != current.CandidateId && !profile.BlockedRelayIds.Contains(r.Id)))
                {
                    try { candidates.Add(await _analyzer.ProbeRelayAsync(relay, server, ProbeMode.Quick, ct)); }
                    catch { /* relay individual puede fallar; se omite */ }
                }

                var scores = new ScoreEngine().EvaluateAll(candidates);
                var best = scores.Best;
                if (best is null) continue;

                if (best.CandidateId != bestSinceCandidateId)
                {
                    bestSinceCandidateId = best.CandidateId;
                    bestConsistentSince = DateTimeOffset.UtcNow;
                }

                var sc = scores.AllScores.FirstOrDefault(s => s.CandidateId == current.CandidateId);
                var bs = scores.AllScores.FirstOrDefault(s => s.CandidateId == best.CandidateId);

                var decision = _hysteresis.Evaluate(new AutoSwitchContext
                {
                    CurrentCandidate = current,
                    BestCandidate = best,
                    CurrentScore = sc,
                    BestScore = bs,
                    SecondsSinceLastSwitch = (DateTimeOffset.UtcNow - lastSwitchUtc).TotalSeconds,
                    SwitchesLastHour = switchesLastHour,
                    SecondsBestConsistent = (DateTimeOffset.UtcNow - bestConsistentSince).TotalSeconds
                });

                if (decision.Decision == AutoSwitchDecision.NoChange) continue;

                if (decision.Decision is AutoSwitchDecision.Failback or AutoSwitchDecision.EmergencyRestore)
                {
                    await FailbackAsync(decision.Reason, ct);
                    return;
                }

                // AutoSwitchDecision.Switch
                PublishInfo(decision.Reason);
                await _stateMachine.TransitionToAsync(OptimizerState.Switching);
                var newRelay = best.Relay!;
                var newStatus = await ConnectWithBackoffAsync(profile, newRelay, server, ct);
                lastSwitchUtc = DateTimeOffset.UtcNow;
                switchesLastHour++;
                if (newStatus.IsConnected)
                {
                    current = best;
                    _sessions.SetSelectedCandidate(best.CandidateId, best.Relay!.Name);
                    _sessions.IncrementRouteSwitch();
                    PublishSuccess($"Ruta cambiada a {best.DisplayName}.");
                }
                else
                {
                    PublishWarning($"El nuevo relay '{newRelay.Name}' no conectó; se mantiene el actual.");
                }
                await _stateMachine.TransitionToAsync(OptimizerState.Monitoring);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error($"Error en el bucle de monitoreo: {ex.Message}", ex);
                PublishError($"Error de monitoreo: {ex.Message}");
                if (_stateMachine.CanTransitionTo(OptimizerState.Degraded))
                    await _stateMachine.TransitionToAsync(OptimizerState.Degraded);
            }
        }
    }

    private async Task FailbackAsync(string reason, CancellationToken ct)
    {
        _sessions.Record(EventSeverity.Warning, EventCategory.Route, reason);
        await _stateMachine.TransitionToAsync(OptimizerState.FailingBack);
        try { await _tunnelManager.StopAsync(ct); }
        catch (Exception ex) { Log.Warning($"Error al detener túnel durante failback: {ex.Message}"); }
        PublishWarning(reason + " Red restaurada a ruta directa.");
        await StopInternalAsync("Failback");
    }

    private void OnTunnelStatusChanged(object? sender, TunnelStatus status)
    {
        if (status.State == TunnelState.Failed && _stateMachine.Current is OptimizerState.Active or OptimizerState.Monitoring)
        {
            PublishWarning("El túnel reportó fallo.");
        }
    }

    /// <summary>Detiene la optimización y restaura la red.</summary>
    public async Task StopAsync(string reason = "Stopped by user")
    {
        _sessions.Record(EventSeverity.Info, EventCategory.Session, $"Detención solicitada: {reason}");
        await StopInternalAsync("Stopped");
    }

    private async Task StopInternalAsync(string finalStatus)
    {
        try
        {
            await _stateMachine.TransitionToAsync(OptimizerState.Stopping);
            try { await _tunnelManager.StopAsync(); }
            catch (Exception ex) { Log.Warning($"Al detener túnel: {ex.Message}"); }
            PublishInfo("Red restaurada a la configuración original.");
        }
        finally
        {
            try { _sessionCts?.Cancel(); } catch { }
            try
            {
                if (_sessions.Current is not null) _sessions.EndSession(finalStatus);
            }
            catch (Exception ex) { Log.Warning($"Al cerrar sesión: {ex.Message}"); }
            if (_stateMachine.Current != OptimizerState.Idle)
                await _stateMachine.TransitionToAsync(OptimizerState.Idle);
        }
    }

    private void PublishInfo(string m) => _events.Publish(new AppEvent { Severity = EventSeverity.Info, Category = EventCategory.Route, Message = m, State = _stateMachine.Current });
    private void PublishSuccess(string m) => _events.Publish(new AppEvent { Severity = EventSeverity.Success, Category = EventCategory.Route, Message = m, State = _stateMachine.Current });
    private void PublishWarning(string m) => _events.Publish(new AppEvent { Severity = EventSeverity.Warning, Category = EventCategory.Route, Message = m, State = _stateMachine.Current });
    private void PublishError(string m) => _events.Publish(new AppEvent { Severity = EventSeverity.Error, Category = EventCategory.System, Message = m, State = _stateMachine.Current });

    public void Dispose()
    {
        try { _sessionCts?.Cancel(); _sessionCts?.Dispose(); } catch { }
        _tunnelManager.StatusChanged -= OnTunnelStatusChanged;
    }
}
