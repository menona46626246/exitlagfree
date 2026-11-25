# ARCHITECTURE.md — Arquitectura de GameRoute Optimizer

## Principios

1. **Modularidad**: núcleo multiplataforma (`Core`) sin UI; capa privilegiada separada
   (`Service`); UI desacoplada por EventBus e interfaces.
2. **Honestidad**: las estimaciones se marcan como tales; la confianza acompaña cada
   recomendación; el estado de red siempre visible.
3. **Reversibilidad**: todo cambio de red (rutas, túnel, firewall, DNS) tiene restore
   explícito + snapshot automático + botón de emergencia.
4. **Mínima superficie**: solo herramientas oficiales WireGuard y comandos estándar del
   sistema; sin drivers, sin hooks, sin DPI.

## Proyectos

```
src/
├─ GameRouteOptimizer.Core      net8.0 (multiplataforma)
│   ├─ Models/        Entidades y enums (GameProfile, RelayNode, ProbeSummary…)
│   ├─ Probing/       ProbeEngine, transporte (ICMP/TCP/UDP/HTTP), traceroute, stats
│   ├─ Scoring/       ScoreEngine con referencias absolutas y penalizaciones
│   ├─ AutoSwitch/    HysteresisPolicy (histéresis, cooldown, failback)
│   ├─ Routing/       RouteAnalyzer, RouteCalculator, IRouteApplier, RoutePlan
│   ├─ Tunneling/     WireGuardConfigParser, WireGuardTunnelManager, KillSwitchManager
│   ├─ Profiles/      ProfileManager (CRUD + import/export JSON)
│   ├─ Relay/         RelayManager (secretos cifrados), RelayProber
│   ├─ Game/          GameManager (detección de procesos, vigilancia, scan autorizado)
│   ├─ Storage/       SessionDatabase (SQLite)
│   ├─ Services/      ConfigStore, DataStore, LogService, SessionRecorder,
│   │                 NotificationService, OptimizerOrchestrator
│   ├─ StateMachine/  OptimizerStateMachine
│   ├─ Events/        EventBus + AppEvent
│   ├─ Ipc/           IpcMessage, framing JSON, NamedPipeIpcServer/Client
│   └─ Platform/      PlatformInfo, CommandRunner (sin shell), SecretProtector (DPAPI/AES)
├─ GameRouteOptimizer.Service   net8.0 (elevado en Windows)
│   ├─ PrivilegedOperations/  PrivilegedOps (netsh/route/ip), NetshFirewallExecutor
│   ├─ RouteService/          SystemRouteApplier (IRouteApplier real)
│   └─ Program.cs             Servidor IPC + handlers + snapshot de rutas
├─ GameRouteOptimizer.App       net8.0-windows, WPF, MVVM, cero dependencias NuGet
│   ├─ ViewModels/    Main + 7 pantallas + editores (wrappers INotifyPropertyChanged)
│   ├─ Views/         XAML en español, tema oscuro, diálogos
│   ├─ Controls/      LatencyChart (OnRender/StreamGeometry)
│   ├─ Services/      AppServices (composición manual), IpcTunnelManager
│   └─ Infrastructure/Mvvm.cs (ObservableBase, RelayCommand, AsyncRelayCommand)
├─ GameRouteOptimizer.Cli       net8.0 (probe/trace/score/relay/profile/tunnel-check)
└─ GameRouteOptimizer.Tests     xUnit — 80 pruebas de toda la lógica central
```

## Decisiones clave (resumen de PLAN.md)

| # | Decisión | Razón |
|---|----------|-------|
| D1 | Cero dependencias NuGet en la App; MVVM y gráficos propios | Compilación robusta, sin cadenas de paquetes |
| D2 | LogService propio estilo Serilog | Cero dependencias; rollover diario + ring buffer UI |
| D3 | WireGuard solo con herramientas oficiales | Sin drivers propios; compatible con actualizaciones |
| D4 | Secretos: DPAPI (Win) / AES-GCM keyfile (Linux) | Clave privada nunca en claro ni en exports |
| D5 | relay→juego = estimación/historial/desconocido | Honestidad: no se puede medir sin agente |
| D6 | UI sin elevación + Servicio elevado por IPC | Privilegios mínimos; diagnóstico siempre disponible |
| D7 | Kill switch por firewall (reglas GameRouteKS_*) | Reversible y entendible; limitación documentada |
| D8 | Traceroute por TTL con fallback a tracert/traceroute | Funciona sin raw sockets |
| D9 | CI en Linux para Core/Service/Cli/Tests | Lógica central verificada sin red externa (fakes) |

## Máquina de estados

Estados: `Idle, DetectingGame, ProbingDirect, ProbingRelays, SelectingRoute, WaitingUser,
Connecting, Active, Monitoring, Degraded, Switching, FailingBack, Stopping, Error`.

- Tabla de transiciones permitidas en `OptimizerStateMachine.AllowedTransitions`.
- Toda transición: log + evento al bus + hook de cancelación; en fallo → `RestoreRequested`
  → `Stopping` → `Idle` (o `Error` si no hay ruta de restauración posible).

## Flujo de datos de una optimización

```
UI (Dashboard) ──▶ OptimizerOrchestrator.StartOptimizationAsync
    ├─▶ RouteAnalyzer.ProbeDirectAsync   (ProbeEngine)
    ├─▶ RouteAnalyzer.ProbeRelayAsync    (por relay)
    ├─▶ ScoreEngine.EvaluateAll          (score + explicación)
    ├─▶ HysteresisPolicy                 (decisión de cambio / failback)
    ├─▶ ITunnelManager.StartAsync        (IpcTunnelManager → Servicio → wireguard.exe)
    ├─▶ RouteCalculator.ApplyPlan        (Servicio: netsh route; snapshot previo)
    └─▶ MonitorLoopAsync                 (probes ligeras; auto-switch; failback)
            └─▶ SessionRecorder + EventBus + LogService (sesión, UI, logs)
```

## Modelo de concurrencia

- `EventBus` thread-safe con snapshot de handlers.
- Orchestrator: un `CancellationTokenSource` por sesión; el monitoreo corre en Task
  aparte; la cancelación restaura red.
- `CommandRunner` con timeouts y kill de procesos; sin shell.
- Escrituras de config atómicas (tmp + replace).

## Extensibilidad

- `ITunnelManager` / `IRouteApplier` / `IFirewallExecutor`: implementaciones simuladas
  (tests) y reales (Servicio). Un futuro WireGuard-API nativo o plataforma extra solo
  necesita implementar esas interfaces.
- Nuevas métricas: añadir a `RouteCandidate` + pesos en `ScoreWeights`.
- Nuevas vistas: VM + View + DataTemplate en `MainWindow.xaml`.
