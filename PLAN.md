# PLAN — GameRoute Optimizer

> Estado: activo. Última actualización: 2026-09-08.
> Documento vivo: la implementación debe reflejar este plan y este plan debe reflejar la implementación.
> Ver `STATE.md` para el estado real de cada fase, `ROADMAP.md` para mejoras futuras y `ARCHITECTURE.md` para el detalle técnico.

## 1. Resumen del producto

**GameRoute Optimizer** es una aplicación de escritorio para Windows (10/11) que ayuda a jugadores online a
elegir la mejor ruta de red hacia los servidores de sus juegos. Hace diagnóstico honesto (latencia, pérdida,
jitter, traceroute), compara la ruta directa contra relays WireGuard del propio usuario, y — solo cuando el
usuario lo autoriza y aporta los relays — crea un túnel WireGuard para encaminar el tráfico seleccionado.

Es una herramienta **ética, local y transparente**:

- Sin telemetría, sin analytics, sin cuentas, sin pagos, sin publicidad, sin crash-reporting externo.
- Sin backend en la nube: todo corre en la máquina del usuario.
- No inyecta DLL, no hace hooking, no lee memoria de juegos, no toca anti-cheat.
- No intercepta ni inspecciona tráfico del juego (nada de DPI); solo encapsula tráfico en el túnel WireGuard
  que el propio usuario configura.
- No escanea puertos, no satura servidores, no adivina endpoints privados.
- Usa únicamente relays/endpoints aportados por el usuario (nada de relays de terceros ocultos).
- No promete "ping mágico": mide, compara y explica con métricas; documenta los límites físicos.

## 2. Stack y decisiones (por defecto, sin cambios solicitados)

| Área | Decisión | Notas |
|---|---|---|
| Plataforma | Windows 10/11 x64 | App WPF. Núcleo multiplataforma (compila y corre en Linux también). |
| Lenguaje/plataforma | C# / .NET 8 | LTS. `global.json` con roll-forward tolerante. |
| UI | WPF + XAML, MVVM, tema oscuro/claro | Temas mediante diccionarios de recursos (`Themes/Dark.xaml` y `Light.xaml`) intercambiables en caliente con `DynamicResource`; sin toolkit externo: MVVM mínimo propio (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`) para reducir dependencias y riesgos de versión. |
| Gráficos | Control de gráfico propio (WPF `Canvas`/`Polyline`) | Se descartó LiveCharts2/ScottPlot en v1 para no depender de paquetes externos de UI; ver ROADMAP para su integración futura. |
| BD local | SQLite (`Microsoft.Data.Sqlite`) | Configuración, juegos, relays, sesiones, historial. |
| Túnel | WireGuard (estándar) | Dos vías reales: (a) herramientas oficiales `wireguard.exe`/`wg.exe` si WireGuard está instalado; (b) modo "manual" que genera la configuración lista para importar. Proveedor simulado **solo en tests**. |
| Adaptador | Wintun vía WireGuard oficial | Nunca drivers propios. |
| Privilegios | UI normal + operaciones privilegiadas bajo demanda | El App invoca el helper `GameRouteOptimizer.Cli run-op` elevado con UAC solo para: crear/borrar interfaz de túnel, rutas, DNS, kill switch. Nada elevado en reposo. |
| IPC | Named pipes para el modo agente (Servicio) | `GameRouteOptimizer.Service` hostea las operaciones privilegiadas vía pipe `gro.privileged`; el App usa `IpcClient`. El CLI también puede ejercer de agente. |
| Logs | Serilog a archivo local + buffer en memoria para la UI | Configurable desde la UI (nivel, rotación). |
| Tests | xUnit + mocks | Los tests de red real quedan detrás de `ITest` opt-in o usan transportes falsos; toda la lógica central se prueba sin red. |
| Lint/format | `.editorconfig` + analizadores; `dotnet format` documentado | |
| Empaquetado | Scripts de build + script Inno Setup (installer) + portable | Sin firma de código (documentado). |
| Icono/presentación | Nombre propio "GameRoute Optimizer" | Sin marcas ajenas. |
| Idioma | UI/docs en español; código/API en inglés | |

Decisiones menores autónomas (regla 2 del encargo):
1. **Sin servicio residente por defecto**: el helper elevado se lanza bajo demanda y termina solo. Menos superficie, menos riesgo de red aislada por un servicio zombie. El modo agente (pipe) existe y se documenta para quien quiera operación residente.
2. **Sin paquetes de UI de terceros** (LiveCharts2/ScottPlot/CommunityToolkit): se implementa lo necesario a mano; compila con menos riesgo.
3. **Los relays siempre son del usuario.** No hay lista pública de relays: sin backend no puede haberla. El usuario importa/configura sus propios endpoints WireGuard (los que ya paga o administra).
4. **El "split tunneling" por proceso no es posible sin drivers propios** (prohibidos): se implementa *routing por destino* (solo las IP/destinos del juego entran al túnel). Cuando no se puede enrutar por destino, se advierte que el túnel será global.
5. **DNS del túnel**: solo IPv4 literales se aplican a la interfaz (lo que acepta `netsh`); en modo
   «solo destinos» los servidores DNS se encaminan por el túnel si el relay los cubre (anti-fuga DNS) y,
   si no, se avisa. Se restaura al detener.
6. **Kill switch**: solo cuando el usuario lo activa y con aviso explícito. Bloquea la **salida por
   defecto** de los perfiles de firewall (Domain/Private/Public) y añade excepciones explícitas para la
   interfaz del túnel, el endpoint UDP del relay y la red local (reglas `GRO_KillSwitch_*`); el estado
   previo se guarda en `%ProgramData%\GameRouteOptimizer` y se restaura al desactivar. Se desactiva
   siempre al salir/detener/emergencia y también al arrancar si quedó activo de una sesión anterior
   (estado persistido `network_state` en SQLite). Sin privilegios no se activa y se explica el fallo.
7. **Menos avisos UAC**: cada acción compuesta (activar túnel = instalar → esperar interfaz → DNS;
   detener = desinstalar) se ejecuta como **una secuencia en una sola elevación** (`Sequence`).

## 3. Arquitectura (resumen)

```
┌─────────────────────────────┐    named pipe (modo agente) o proceso elevado bajo demanda
│ GameRouteOptimizer.App (WPF)│ ─────────────────────────────────────────────┐
│  Views / ViewModels / MVVM  │                                             ▼
└──────────┬──────────────────┘                        ┌─────────────────────────────┐
           │ in-process                              │ GameRouteOptimizer.Service   │
           ▼                                          │  IpcServer + operaciones     │
┌─────────────────────────────┐                       │  privilegiadas (rutas, WG,   │
│ GameRouteOptimizer.Core     │                       │  DNS, firewall)              │
│  Modelos · Managers · Probe │                       └──────────────┬──────────────┘
│  Engine · Score · State     │                                      │ invoca
│  Machine · Tunneling (abst) │                                      ▼
└─────────────────────────────┘                     herramientas oficiales del SO /
      ▲                 ▲                           WireGuard oficial (con UAC)
      │                 │
┌─────┴──────┐   ┌───────┴────────┐
│ SQLite     │   │ Serilog local  │
└────────────┘   └────────────────┘
```

- El **núcleo** (`Core`, `net8.0`) es puro C# y se prueba en cualquier SO. Los puntos que tocan el sistema
  (ICMP, sockets, rutas, WireGuard) están detrás de interfaces con implementaciones reales y otras simuladas
  **solo para tests**.
- Las operaciones privilegiadas (rutas, DNS, firewall, interfaz WireGuard) NO se ejecutan desde la UI:
  se serializan como operaciones JSON y las ejecuta un proceso elevado (`Cli run-op` o `Service`), con
  resultado JSON. Esto permite auditoría (logs locales de cada operación) y evita mantener la UI elevada.
- Máquina de estados global (Idle → Probing → Connecting → Active/Monitoring → …) con transiciones
  registradas, cancelables cuando es seguro y con restauración de red en fallo (ver sección 9).

## 4. Modelo de datos (resumen)

- `GameProfile`: nombre, ejecutables (lista), ruta, argumentos opcionales, targets de servidor (dominio/IP,
  puertos, región, tipo de probe: ICMP/TCP/UDP/HTTP), preferencia de protocolo, modo de ruta (directo /
  túnel global / solo destinos), relay preferido, relays bloqueados, reglas especiales, notas, autoStart/
  autoStop, última sesión.
- `RelayNode`: id, nombre, proveedor opcional, país/región, ciudad, endpoint WireGuard (host:puerto),
  public key, allowed IPs, DNS interno opcional, salud (latencia/loss/jitter), carga estimada opcional,
  costo opcional, prioridad, activo. La **private key** se guarda cifrada (DPAPI en Windows) o solo en
  memoria si DPAPI no está disponible; jamás en exportaciones JSON.
- `AppSettings`: tema, idioma, probing (intervalos, timeouts, tamaño de ráfaga, modo rápido/profundo),
  auto-switch (habilitado, umbrales, histéresis, cooldown), kill switch, DNS, MTU, logs, privacidad,
  servicio/privilegios.
- `SessionRecord`: sesión con línea temporal de eventos (cambios de ruta, caídas, errores) y métricas.

## 5. Funcionalidad

### Modo 1 — Diagnóstico (sin túnel)
- Probes: ICMP (si permitido), TCP connect (latencia de handshake), UDP opcional (timeout controlado),
  HTTP/S GET contra endpoints de salud definidos por el usuario. Rate limit bajo, timeouts configurables,
  modos rápido/profundo.
- Traceroute/tracert simplificado con latencia por salto y detección de saltos problemáticos
  (pérdida alta / latencia anómala).
- Comparación ruta directa vs. relays candidatos y recomendación con explicación y confianza.

### Modo 2 — Optimización manual
- El usuario añade/importa relays WireGuard, el programa los mide (host del relay), estima calidad de la
  ruta relay→juego cuando hay datos del usuario (o marca la estimación como baja confianza), y aplica el
  túnel elegido con rutas por destino o globales. El usuario siempre confirma.

### Modo 3 — Optimización automática
- El AutoOptimizer puntúa candidatos (directo + relays), aplica histéresis (no cambia por mejoras
  mínimas/inestables), cooldown entre cambios, failover automático a directo si el túnel cae o empeora,
  y notifica cada cambio importante.

### Gestión de juegos
- CRUD manual, detección de instalados buscando ejecutables comunes **solo con autorización del usuario**,
  detección de proceso en ejecución, auto-start/auto-stop opcional por juego, import/export JSON de perfiles.

### Monitoreo e historial
- Dashboard con estado grande, ping/pérdida/jitter en vivo, gráfico de latencia y pérdida en tiempo real,
  eventos, logs recientes y notificaciones; historial de sesiones con antes/después y exportación.

## 6. Fases, entregables y criterios de aceptación

| Fase | Contenido | Criterio de aceptación | Estado |
|---|---|---|---|
| 0 | Scaffold: solución, proyectos, configs, README | Compila la solución (CI) y se documenta cómo construir | ✅ / en CI |
| 1 | Modelos + configuración + SQLite + JSON | Tests de roundtrip pasan | ✅ |
| 2 | Diagnóstico: probes, traceroute, métricas, scoring, UI | Tests de scoring/probes; vista Diagnóstico funcional | ✅ |
| 3 | Gestión de juegos + detección | CRUD + import/export + tests | ✅ |
| 4 | Relays + importación WireGuard | CRUD + parseo/validación + tests | ✅ |
| 5 | Túnel: providers, activación/desactivación | Restauración garantizada; tests con proveedor simulado | ✅ |
| 6 | Enrutamiento: rutas por destino/globales, kill switch, fallback | RouteCalculator + tests; operaciones privilegiadas | ✅ |
| 7 | Monitoreo: sesión, eventos, historial, gráficos | SessionRecorder + UI de sesión + exportar | ✅ |
| 8 | Auto-optimización: histéresis, cooldown, failover | Tests de AutoOptimizer | ✅ |
| 9 | Seguridad/UX: advertencias, botón de emergencia, logs | UI en español con estados y advertencias | ✅ |
| 10 | Tests y documentación final | Suite xUnit verde en CI en Linux y Windows (33 tests: almacenamiento, juegos/relays, parser .conf, métricas/scoring, rutas, estados, probes con transporte simulado, DNS/cancelación, SQLite corrupta, auto-switch/cooldown/estabilidad, failback y session recorder); docs completas | ✅ |
| 11 | Pulido de experiencia (UX) | Panel con estado, métricas y gráficos en vivo; diagnóstico con tabla directa vs relays; relays con prueba rápida y carga; sesión con eventos coloreados y exportación; notificaciones claras; tema oscuro/claro; gráficos eficientes | ✅ (CI verde) |

Leyenda: ✅ hecho · 🔄 en curso · ⏳ pendiente · ⛔ bloqueado (detalle en STATE.md).

## 7. Cómo construir, probar y ejecutar (Windows)

```powershell
# Requisitos: .NET 8 SDK (https://dotnet.microsoft.com) y, para el modo túnel, WireGuard oficial
# (https://www.wireguard.com/install/) ejecutado como administrador una vez.

git clone <repo>
cd exitlagfree

# Compilar todo (solución)
dotnet restore src/GameRouteOptimizer.sln
dotnet build   src/GameRouteOptimizer.sln -c Release

# Tests
dotnet test    src/GameRouteOptimizer.sln -c Release

# Ejecutar la app
dotnet run --project src/GameRouteOptimizer.App -c Release

# Formato/análisis estático
dotnet format  src/GameRouteOptimizer.sln --verify-no-changes   # (sin --verify para aplicar)
```

En esta máquina de desarrollo (Linux sin SDK y con nuget.org bloqueado) la validación de compilación y
tests se ejecuta en GitHub Actions (`windows-latest`, SDK .NET 8 preinstalado) contra este mismo repositorio;
ver `.github/workflows/ci.yml`. Cualquier cambio debe dejar el CI verde.

## 8. Seguridad y privacidad (resumen; detalle en SECURITY.md)

- Cero telemetría/analytics/cuentas/pagos/publicidad/crash-reporting externo.
- Logs locales únicamente, con rotación y nivel configurable; el usuario puede borrarlos.
- Private keys WireGuard cifradas con DPAPI (Windows) o solo en memoria si DPAPI no está disponible.
  Exportaciones JSON sin claves por defecto (opción explícita de exportación cifrada documentada).
- No se almacenan credenciales de juegos ni tráfico.
- Kill switch: desactivable en todo momento; botón de emergencia "Detener y restaurar red" siempre visible
  en la UI; al salir la app se detiene el túnel y se restauran rutas/DNS (si el proceso pudo elevarse; si no,
  la app ofrece las instrucciones exactas y un script de restauración).
- Desinstalación limpia: script que elimina adaptadores creados, reglas de firewall, rutas y datos locales.

## 9. Máquina de estados

```
Idle ──(juego detectado / auto)──▶ DetectingGame ──▶ ProbingDirect ──▶ ProbingRelays
   ▲                                (sin auto) │                     │
   │                                         ┌──┴──▶ SelectingRoute ◀──┘
   │                                         │          │
   │                                       WaitingUser (manual) ─┘
   │                                         │ (usuario confirma / auto mejora clara)
   │                                         ▼
   │                                     Connecting ──▶ Active ◀──▶ Monitoring
   │                                         │ fallo       │ fallo/empeora
   │                                         ▼             ▼
Error ◀── cualquier fallo ◀── Stopping ◀── FailingBack ◀── Degraded ◀── Switching
```

Toda transición: se loguea, se muestra en UI, es cancelable si es segura, y si el túnel estaba activo el
fallo dispara restauración automática de rutas/DNS (failback) con reintentos con backoff.

## 10. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Dejar la red del usuario rota | Nunca se borran rutas originales (WireGuard añade y quita las suyas con la interfaz); activación reversible con rollback si falla a mitad; el estado de red se persiste (`network_state`) y el arranque recupera túnel/kill switch de sesiones anteriores; botón de emergencia «Detener y restaurar red» siempre visible. |
| Túnel que empeora la conexión o se cae | Comparación continua directo vs túnel; vigilancia del handshake de WireGuard (`wg show dump`); auto-failback con histéresis y N comprobaciones consecutivas. |
| Fuga DNS con túnel activo | DNS aplicado a la interfaz del túnel; en modo «solo destinos» los servidores DNS se encaminan por el túnel cuando el relay los cubre y, si no, se avisa explícitamente (el kill switch la elimina por completo). |
| Uso indebido (evasión de bans/geo) | Docs y UI: prohibido; herramienta solo para mejor ruta legítima. |
| Servidores bloquean ICMP | El sistema detecta y etiqueta la métrica como no fiable; TCP connect como alternativa. |
| Dependencias externas inestables | Mínimas (Serilog, SQLite, ProtectedData). Todo lo demás, BCL. |
| Falta de privilegios | Modo diagnóstico completo sin elevación; el túnel pide UAC solo al activarse y documenta el procedimiento manual alternativo. |

## 11. Advertencias legales y limitaciones (resumen; detalle en docs/limitaciones.md)

- GameRoute Optimizer no puede bajar el ping por debajo del límite físico (velocidad de la luz + ISP +
  equipo local). Si el problema es WiFi malo o ISP saturado, ninguna ruta lo arregla.
- Solo ayuda cuando la ruta directa es subóptima y un relay (del usuario) ofrece un camino mejor.
- Algunos juegos/plataformas prohíben VPN/proxies en sus términos; el usuario debe revisarlos. No usar
  para evadir bans, geo-restricciones o políticas de servicio.
- Las métricas hacia servidores de juego que bloquean probes son estimaciones; la UI lo indica.
