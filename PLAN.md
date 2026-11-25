# GameRoute Optimizer — PLAN.md

**Versión:** 1.0 · **Fecha:** 2026-09-08 · **Estado del plan:** activo

## 1. Visión general

GameRoute Optimizer es una aplicación de escritorio para Windows 10/11 que ayuda a reducir
latencia, pérdida de paquetes y variabilidad (jitter) en videojuegos en línea mediante:

1. **Diagnóstico** de la ruta de red actual (ping, TCP connect, traceroute, pérdida, jitter).
2. **Optimización manual**: el usuario define relays/servidores WireGuard propios y la app
   mide, puntúa y establece el túnel hacia el relay elegido.
3. **Optimización automática**: la app selecciona el mejor relay según métricas con
   histéresis y cooldown, con failback automático a la ruta directa.

El programa **no promete ping mágico**: elige la mejor ruta disponible entre las que el
usuario configura, mide resultados con honestidad y restaura la red si algo falla.

## 2. Principios éticos y legales (no negociables)

- Sin inyección en juegos, sin lectura de memoria, sin hooking, sin bypass de anti-cheat.
- Sin DPI ni captura de tráfico sensible; solo probing activo a endpoints autorizados.
- Sin evasión de geo-bloqueos, bans o políticas de servicio.
- Sin telemetría, analytics, cuentas, pagos, publicidad ni crash reporting externo.
- Sin backend cloud; todo local. Sin drivers propios; solo integración oficial WireGuard.
- Prohibido escanear puertos masivamente o saturar servidores.

## 3. Stack técnico (decisiones)

| Área | Decisión | Justificación |
|------|----------|---------------|
| Plataforma | Windows 10/11 (principal) | Objetivo del producto |
| App escritorio | C# / .NET 8 + WPF (MVVM) | Requisito del usuario |
| Gráficos | Control propio `LatencyChart` (StreamGeometry/OnRender) | **D1** — evita dependencias NuGet externas y garantiza compilación |
| MVVM | `ObservableBase` + `RelayCommand` propios (~120 líneas) | **D1** — cero dependencias externas en la App |
| BD local | SQLite vía `Microsoft.Data.Sqlite` con fallback JSON | Requisito; fallback si el paquete no restaura |
| Túnel | WireGuard con herramientas oficiales (`wireguard.exe /installtunnelservice`, `wg-quick` en Linux) | **D3** — sin drivers propios; uso de tooling oficial |
| Adaptador | Wintun (provisto por la instalación oficial de WireGuard) | Requisito |
| Privilegios | UI sin elevación + Servicio elevado instalable (named pipes) | **D6** — elevación solo cuando es necesaria |
| IPC | Named pipes locales + JSON con prefijo de longitud | Simple, sin dependencias, multiplataforma |
| Logs | `LogService` propio estilo Serilog (file sink con rollover diario + ring buffer en memoria) | **D2** — cero dependencias; "Serilog o similar" |
| Tests | xUnit | Requisito |
| Lint | `.editorconfig` + analizadores por defecto + `dotnet format` | Requisito |
| Empaquetado | `dotnet publish` + zip (installer/scripts) + script Inno opcional | Requisito |
| Secretos | DPAPI (P/Invoke `CryptProtectData`) en Windows; AES-GCM con keyfile 0600 en Linux | **D4** |
| Idioma | UI y documentación en español; código/API en inglés | Requisito |

### Decisiones registradas (ADR resumido)

- **D1 — Cero dependencias NuGet en la App WPF.** MVVM y gráficos son pequeños y propios;
  así el build de Windows solo requiere el SDK y no falla por versiones de paquetes.
  Core usa exclusivamente `Microsoft.Data.Sqlite`.
- **D2 — Logger propio.** API minimalista estilo Serilog (`Log.Information/Warning/Error`),
  sink de archivo con rollover diario y ring buffer para la UI.
- **D3 — WireGuard solo con herramientas oficiales.** Windows: `wireguard.exe
  /installtunnelservice` y `/uninstalltunnelservice`, `wg.exe show`. Linux: `wg-quick`.
  No se implementan drivers ni se toca el kernel.
- **D4 — Secretos.** Clave privada WireGuard cifrada con DPAPI (CurrentUser) en Windows.
  En Linux, AES-GCM con keyfile con permisos 0600 (documentado como menor garantía).
  Los exports JSON excluyen secretos salvo opción explícita.
- **D5 — Latencia relay→juego.** No se puede medir relay→servidor sin un agente en el relay.
  Se usa: (a) valor estimado configurado por el usuario (`RelayToGameMs`), (b) historial de
  sesiones, o (c) penalización + menor confianza si es desconocido. Documentado en
  `docs/limitaciones.md`.
- **D6 — Modelo de privilegios.** El diagnóstico no requiere elevación y corre en-proceso
  en la App. Túneles/rutas/kill switch requieren el *Servicio* (elevado) comunicado por
  named pipes locales; si el servicio no está instalado, la app muestra guía y corre en
  *modo diagnóstico*.
- **D7 — Kill switch por firewall de Windows.** Se agregan reglas (bloqueo outbound global +
  permisos para `wireguard.exe` y el servicio) con nombre `GameRouteKS_*`, completamente
  reversibles. Limitación documentada: es grueso (por programa, no por proceso de juego).
- **D8 — Traceroute por TTL** usando `System.Net.NetworkInformation.Ping` con `PingOptions.Ttl`;
  fallback a `tracert`/`traceroute` del sistema si ICMP no está disponible.
- **D9 — CI en Linux.** Core, Cli, Service y Tests compilan y pasan tests en Linux; la App
  WPF (`net8.0-windows`) se compila en Windows (`scripts/build.ps1`). Esto permite verificar
  la lógica central aquí sin red externa (tests usan fakes/loopback).

## 4. Arquitectura (resumen; detalle en ARCHITECTURE.md)

```
GameRouteOptimizer.sln
├─ src/GameRouteOptimizer.Core      (net8.0) Modelos, probing, scoring, routing,
│                                            tunneling, perfiles, storage, eventos, IPC,
│                                            máquina de estados, orquestador
├─ src/GameRouteOptimizer.Service   (net8.0) Servicio elevado: túneles, rutas, kill switch,
│                                            servidor IPC, operaciones privilegiadas
├─ src/GameRouteOptimizer.App       (net8.0-windows, WPF) UI española, MVVM, gráficos,
│                                            cliente IPC, modo diagnóstico en-proceso
├─ src/GameRouteOptimizer.Cli       (net8.0) CLI multiplataforma (probe/trace/score/…)
├─ src/GameRouteOptimizer.Tests     (net8.0, xUnit) Pruebas de toda la lógica central
├─ docs/                            10 guías en español
├─ installer/                       scripts de publicación/instalación
├─ examples/                        JSON de ejemplo (juego, relays, conf WireGuard)
└─ scripts/                         build.ps1 / build.sh / run-tests.sh / run-tests.ps1
```

Módulos clave: `ProfileManager`, `GameManager`, `RelayManager`, `ProbeEngine`,
`TracerouteEngine`, `RouteAnalyzer`, `ScoreEngine`, `TunnelManager`, `RouteManager`,
`KillSwitchManager`, `DnsManager`, `HealthMonitor`, `SessionRecorder`, `LogService`,
`ConfigStore`, `NotificationService`, `IpcClient`, `IpcServer`, `OptimizerOrchestrator`.

### Máquina de estados

`Idle → DetectingGame → ProbingDirect → ProbingRelays → SelectingRoute →
(Connecting → Active → Monitoring → (Switching | Degraded | FailingBack) → Stopping) → Idle`,
con `WaitingUser` y `Error` alcanzables. Toda transición se registra, se publica a la UI y
restaura la red si falla.

## 5. Fases y criterios de aceptación

| Fase | Alcance | Criterio de aceptación |
|------|---------|------------------------|
| 0 | Scaffold: sln, 5 proyectos, configs, README inicial | `dotnet build` compila los proyectos no-WPF |
| 1 | Modelos, settings, SQLite/JSON, import/export | Round-trip JSON de perfiles/relays sin pérdida; tests ok |
| 2 | Diagnóstico: ping/TCP/UDP/HTTP, traceroute, métricas, scoring básico | Métricas correctas sobre muestras sintéticas; timeouts sin excepciones |
| 3 | Gestión de juegos: perfiles, detección instalados, detección en ejecución, auto-start | Detección por nombre de proceso funciona; perfiles CRUD ok |
| 4 | Relays: CRUD, import WG, medición, estado | Relay desde .conf válido se importa sin secretos en claro |
| 5 | Túnel: parser/validación WG, start/stop con backoff, DNS, MTU | Conf inválida rechazada antes de aplicar; status monitor ok |
| 6 | Routing: planes por destino, snapshot/restore, kill switch, fallback | Restore re-agrega rutas originales; kill switch reversible (simulado) |
| 7 | Monitoreo: sesión, eventos, historial, antes/después, informes | Sesión graba eventos y exporta informe JSON/Markdown |
| 8 | Auto-optimización: histéresis, cooldown, failover, notificaciones | No flapping con mejora mínima; failback al caer el túnel |
| 9 | Seguridad/UX final: privilegios, advertencias, export, limpieza | Sin secretos en claro; advertencias visibles en UI |
| 10 | Tests y documentación | Suite xUnit verde; 10 docs en `docs/` completas |

## 6. Modelo de datos principal

- `GameProfile`: nombre, ejecutables, ruta, argumentos, servidores (dominio/IP/puertos/región/protocolo),
  preferencia de protocolo, modo de ruta (Directo/TúnelGlobal/TúnelSoloJuego/Split),
  relay preferido, relays bloqueados, auto-start, notas, activo.
- `GameServer`: dominio, IP, puertos, región, protocolos de prueba aceptados, health URL opcional.
- `RelayNode`: id, nombre, proveedor, país/región, ciudad, endpoint, puerto, public key,
  allowed IPs, DNS interno opcional, `RelayToGameMs` opcional (estimación), carga/costo/prioridad
  opcionales, activo, métricas de salud, `PrivateKeyRef` (opcional, cifrado).
- `AppSettings`: probing (intervalos, timeouts, rate limit, modo), auto-switch (umbral,
  cooldown, máx cambios/hora), kill switch, MTU, DNS, logs, privacidad, modo seguro.
- `ProbeSummary`: media, p50/p95/p99, pérdida, jitter, estabilidad 0-100, nº muestras, fiabilidad ICMP.
- `RouteScore` + `Recommendation`: score 0-100, razón textual en español, confianza 0-1.

## 7. Scoring (fórmula)

Score ponderado sobre métricas normalizadas entre candidatos alcanzables:

```
score = 100 * (wLat * (1 - normLatencia) + wLoss * (1 - normLoss)
             + wJit * (1 - normJitter) + wStab * estabilidad/100)
      − penalDesconocido − penalCarga − penalSaltosExtra − penalHistorialCaidas
```

Pesos por defecto: `wLat=0.50, wLoss=0.25, wJit=0.15, wStab=0.10`. Exclusiones: candidato
inalcanzable (score 0) y pérdida ≥ 20 % (extrema). Explicación en español del factor
dominante y confianza según nº de muestras y fiabilidad de las probes.

## 8. Riesgos y mitigaciones

| Riesgo | Mitigación |
|--------|-----------|
| ICMP bloqueado por servidores de juego | Cadena de fallback ICMP→TCP→HTTP y aviso de fiabilidad |
| Mal uso del kill switch (aislamiento) | Advertencia previa, botón "Detener y restaurar red", desactivación con un clic |
| Relay→juego no medible | Estimación del usuario + historial + confianza reducida (D5) |
| Cambios de ruta constantes (flapping) | Histéresis + cooldown + máx. cambios/hora |
| Falta de privilegios | Servicio elevado instalable + modo diagnóstico sin túnel |
| Compilación WPF fuera de Windows | D9: Core/Cli/Service/Tests verificados en Linux; App documentada para Windows |

## 9. Fuera de alcance (explícito)

Evasión de geo-bloqueos/bans, DPI, lectura de memoria, hooking, drivers propios, cuentas,
pagos, telemetría, backend cloud, escaneo masivo de puertos, relays de terceros sin
autorización explícita del usuario.
