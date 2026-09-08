# Arquitectura — GameRoute Optimizer

Aplicación de escritorio Windows (C#/.NET 8, WPF) **local-first**: sin cuentas, sin nube, sin telemetría.

## Proyectos (`src/`)

| Proyecto | TFM | Rol |
|---|---|---|
| `GameRouteOptimizer.Core` | net8.0 | Toda la lógica: modelos, configuración, SQLite, probing, scoring, routing, túneles, sesiones, operaciones privilegiadas. No depende de WPF. |
| `GameRouteOptimizer.Cli` | net8.0 | CLI (`diag`, `trace`, `run-op`, `agent`) y helper elevado que la app lanza con UAC. |
| `GameRouteOptimizer.Service` | net8.0 | Servicio opcional/agente con el mismo contrato de operaciones privilegiadas. |
| `GameRouteOptimizer.App` | net8.0-windows | UI WPF en español (MVVM), tema oscuro. |
| `GameRouteOptimizer.Tests` | net8.0 | Suite xUnit sin red externa. |

## Capas y flujo

```
┌─────────────┐   eventos/estado    ┌──────────────────────────────┐
│   App (WPF) │ ──────────────────▶ │  OptimizationOrchestrator    │
│  (MVVM/es)  │ ◀────────────────── │  (máquina de estados)        │
└─────────────┘                     └───────┬──────────┬───────────┘
                                            │          │
                       ┌────────────────────┘          └──────────────────┐
                       ▼                                               ▼
              Probing/Diagnóstico                               Túnel + Rutas
        ProbeEngine · TracerouteEngine · ScoreEngine     TunnelManager · RouteCalculator
        (ICMP/TCP/UDP/HTTP, nunca hosts ajenos)           WireGuardConfigBuilder · KillSwitch
                                                                    │
                                                                    ▼
                                    ┌──────── PrivilegedOps (IPrivilegedOps) ────────┐
                                    │ CliElevatedRunner / OpPipeServer / OpExecutor │
                                    │ (wireguard.exe, netsh, rutas, DNS, firewall)  │
                                    └───────────────────────────────────────────────┘
```

- **Almacenamiento**: SQLite (`ConfigStore`, archivo `gro.db` local) con juegos, relays, sesiones,
  configuración y aprendizaje de tramo final. Claves privadas WireGuard **nunca** en la base: se cifran
  con DPAPI (`CurrentUser`) o se mantienen solo en memoria.
- **Privilegios**: la app corre sin elevación. Al activar túnel/kill switch se relanza un helper elevado
  (CLI/Service) que ejecuta una operación atómica JSON (`PrivilegedOp`) y devuelve el resultado
  (`PrivilegedOpResult`). Cada operación es reversible y queda en el diario de operaciones.
- **Restauración**: todo cambio de ruta/DNS registra su inversa exacta; el botón «Detener y restaurar red»
  ejecuta la restauración aunque el worker esté bloqueado. El kill switch siempre se desactiva al detener.
- **Eventos**: el orquestador publica `StateChanged`, `MetricsUpdated`, `SessionEventAdded`,
  `RecommendationUpdated`, `DiagnosticsCompleted`; la UI los re-emite en el Dispatcher (hilo de UI).

## Diseño de métricas y recomendación

- Cada destino se mide en series cortas (rápido/profundo) con rate-limit bajo.
- Si ICMP no es fiable (servidores que lo bloquean), se etiqueta y se usa TCP connect como referencia.
- El scoring convierte pérdida/jitter/colas a «ms equivalentes» y aplica histéresis:
  no se cambia de ruta por mejoras menores (≤ `MinImprovementMs`), se exige estabilidad
  (`StabilityWindowSeconds`) y cooldown (`CooldownSeconds`).
- Resultado: `ScoringOutput` con clasificación, confianza y explicación en español para la UI.

## Estados (resumen)

`Idle · DetectingGame · ProbingDirect · ProbingRelays · SelectingRoute · WaitingUser · Connecting ·
Active · Monitoring · Degraded · Switching · FailingBack · Stopping · Error`
Toda transición se registra en logs y en la sesión; los fallos con túnel activo disparan failback automático.
