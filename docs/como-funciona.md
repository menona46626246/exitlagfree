# Cómo funciona GameRoute Optimizer

## Visión general

El programa tiene tres modos que comparten el mismo motor de medición:

1. **Diagnóstico**: mide y explica tu ruta actual. Sin túnel, sin privilegios.
2. **Optimización manual**: tú eliges el relay; la app mide, conecta y monitorea.
3. **Optimización automática**: la app elige el mejor relay por score, cambia de ruta
   solo si la mejora es clara y estable (histéresis + cooldown), y vuelve a la directa
   si el túnel falla o empeora.

## Flujo interno de una optimización

```
Idle → DetectingGame → ProbingDirect → ProbingRelays → SelectingRoute
     → Connecting (túnel WireGuard vía Servicio) → Active → Monitoring
        ↳ (Switching | Degraded | FailingBack) → Stopping → Idle
```

Cada transición se registra en logs, se publica al EventBus (UI + sesión) y, si algo
falla, se ejecuta la ruta de restauración: detener túnel, restaurar rutas originales y
desactivar el kill switch si aplica.

## Componentes principales

| Componente | Responsabilidad |
|------------|-----------------|
| `ProbeEngine` | ICMP/TCP/UDP/HTTP con rate limit, timeouts y fallback automático |
| `TracerouteEngine` | Traceroute por TTL con muestras por salto; fallback a tracert/traceroute |
| `ProbeSummaryCalculator` | Media, p50/p95/p99, pérdida, jitter y estabilidad 0-100 |
| `ScoreEngine` | Puntúa 0-100 cada candidato con pesos y penalizaciones; explica en español |
| `HysteresisPolicy` | Decide cambios de ruta sin flapping (umbral, cooldown, límite horario) |
| `RouteAnalyzer` | Construye candidatos (directa + relays) y los compara |
| `WireGuardTunnelManager` | Activa/detiene túneles con herramientas oficiales WireGuard |
| `RouteCalculator` | Planes de rutas por destino con snapshot para restaurar |
| `KillSwitchManager` | Reglas de firewall reversibles (GameRouteKS_*) |
| `OptimizerStateMachine` | Estados y transiciones con cancelación y restauración |
| `SessionRecorder` | Historial SQLite, eventos, antes/después, informes |
| `OptimizerOrchestrator` | Coordina todo lo anterior (el "cerebro") |
| Servicio elevado | Ejecuta túneles/rutas/firewall con privilegios; IPC por named pipe local |

## Cómo se mide un relay (importante)

- **Usuario → relay**: se mide de verdad (ICMP o TCP al endpoint).
- **Relay → servidor del juego**: **no se puede medir sin un agente dentro del relay**.
  La app usa: (a) la estimación que tú configuras por relay (`relayToGameMs`),
  (b) el historial de sesiones, o (c) lo marca como "desconocido" y **penaliza el score
  y baja la confianza** de la recomendación.
- La latencia total de un candidato relay es `usuario→relay + relay→juego (estimada)`.
  Esto se muestra siempre en la UI y en `docs/limitaciones.md`.

## IPC entre UI y Servicio

La UI corre sin elevación. Las operaciones que las necesitan (túnel, rutas, kill switch)
se envían por named pipe local `gameroute-optimizer-svc` con mensajes JSON enmarcados
(prefijo de longitud). La clave privada solo viaja por ese pipe local en el momento de
conectar el túnel. Si el servicio no está instalado, la app sigue funcionando en modo
diagnóstico y lo indica claramente.

## Datos y archivos

- `settings.json`, `profiles.json`, `relays.json` — configuración (JSON, escritura atómica).
- `sessions.db` — historial de sesiones y eventos (SQLite).
- `machine.key` — clave local para el protector de secretos en Linux (0600).
- `logs/gameroute-YYYYMMDD.log` — logs con rotación diaria.
- Todo bajo `%APPDATA%\GameRouteOptimizer` (o `GAMEROUTE_DATA`).
