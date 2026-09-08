# STATE — GameRoute Optimizer

Última actualización: 2026-09-08 — **CI en verde** (run `34229410950`, HEAD `fe77ff6`: Linux + Windows ✅, **54/54 tests** + smoke-test de la UI).

## Entorno de desarrollo de esta sesión
- Máquina local: Linux (Debian) **sin SDK de .NET** y con nuget.org bloqueado → la compilación y los tests
  se ejecutan en **GitHub Actions** (`windows-latest` + `ubuntu-latest`) contra este repositorio; el estado
  real de compilación/tests es el del workflow CI (`Actions` → `CI`).
- El job de Windows compila la solución completa (incluida la app WPF), ejecuta la suite xUnit y hace un
  **smoke-test** de la UI (`--smoke-test`: construye servicios + ventana principal y sale con código 0).
  Si build/tests fallan, el workflow vuelca las líneas relevantes como anotaciones del job (legibles en
  `gh run view` o en la pestaña del job).
- El job de Linux compila Core + Cli + Service y ejecuta los tests del núcleo.

## Fases

| Fase | Contenido | Estado |
|---|---|---|
| 0 | Scaffold: sln, 5 proyectos, CI (sin acciones de terceros), README, PLAN | ✅ CI verde |
| 1 | Modelos + configuración + SQLite + JSON | ✅ (CI verde; tests de roundtrip) |
| 2 | Diagnóstico: probes (ICMP/TCP/UDP/HTTP), traceroute, métricas, scoring + vista UI | ✅ implementado y compilado; medición real pendiente de validar en Windows físico |
| 3 | Gestión de juegos: CRUD, import/export JSON, detección de instalados | ✅ implementado (detección usa rutas comunes; solo en Windows) |
| 4 | Relays: CRUD, importación `.conf`, claves privadas cifradas con DPAPI | ✅ implementado |
| 5 | Túnel WireGuard: locator de `wireguard.exe`, activación/desactivación, restauración | ✅ implementado; requiere WireGuard instalado y UAC (validación en máquina real pendiente) |
| 6 | Enrutamiento: rutas por destino/globales, kill switch, failback, botón de emergencia | ✅ implementado y compilado; operaciones privilegiadas vía CLI/Service elevado |
| 7 | Monitoreo: sesión, eventos, historial, gráficos en vivo, exportación Markdown | ✅ implementado |
| 8 | Auto-optimización: histéresis, cooldown, estabilidad, failover | ✅ implementado (scoring + orquestador + políticas con tests) |
| 9 | Seguridad/UX inicial: avisos legales, español, botón de emergencia, logs locales | ✅ implementado |
| 10 | Tests ampliados y documentación | ✅ suite xUnit (54 tests) en verde en CI Linux y Windows |
| 11 | Pulido UX: panel/estado/gráficos, diagnóstico comparativo, relays, sesión, notificaciones, tema oscuro/claro, rendimiento | ✅ implementado y en CI verde (ver «Pulido UX» abajo) |

## Qué valida el CI hoy
- ✅ Compilación Release de Core, Cli, Service, App (WPF) y Tests.
- ✅ Tests xUnit (54 tests, suite completa) en Linux y Windows.
- ✅ Smoke-test de la UI en Windows (la ventana se construye —incluye XAML de las 7 secciones— y cierra sola con código 0).
- ⏳ Pendiente de validación **en una máquina Windows real** (no automatizable en runners): creación del
  túnel WireGuard, UAC/elevación, cambio de rutas y kill switch, sondas reales a servidores de juego.

## Robusteza (pasadas de estabilidad)
- **Métricas reales**: jitter sobre RTT en orden de llegada; percentiles/mín/máx/media sobre lista ordenada; sin datos → sin medias inventadas.
- **Probes**: UDP timeout devuelve `Timeout`; cancelación previa/superada se propaga (nunca «fallo DNS»); fallback ICMP→TCP solo anota «ICMP bloqueado» cuando TCP obtuvo datos; resolución con `EndpointResolver` inyectable.
- **Flujos parciales**: perfiles sin servidores y relays sin endpoint/clave se guardan; optimización/diagnóstico/túnel validan antes de operar y fallan limpio.
- **SQLite**: `busy_timeout` + `DefaultTimeout`; filas corruptas o subobjetos nulos se rehidratan a defaults.
- **Red/túnel**: secuencias de una sola elevación por acción con rollback; kill switch con reglas `GRO_KillSwitch_*`, excepciones túnel/endpoint/LAN y restauración del estado previo; estado de red persistido (`network_state`) y recuperación al arranque; auto-failback con N comprobaciones consecutivas y margen de histéresis.

## Pulido UX (fase 11, HEAD `fe77ff6`)
- **Panel principal**: franja de estado en vivo («midiendo…», «túnel activo…», avisos de degradación), jitter de la ruta directa y del túnel, hora de la última medición, botones Optimizar/Detener habilitados según estado, banner de confirmación de relay recomendado (confirmar/mantener directa) y tarjeta de **notificaciones recientes** (⛔/⚠/ⓘ con hora).
- **Diagnóstico**: tabla comparativa «ruta directa vs relays» (latencia, pérdida, jitter, utilizable) generada con las mismas sondas, además del detalle técnico y el traceroute existentes.
- **Relays**: prueba rápida «Probar» desde la propia lista (no solo en el editor), latencia/jitter/pérdida en cada fila y línea de carga estimada cuando el relay la declara.
- **Sesión**: eventos coloreados por categoría (ruta = verde, error = rojo, aviso = ámbar, túnel/estado = azul) para localizar cambios de ruta de un vistazo; exportación Markdown ya disponible.
- **Notificaciones claras** en el orquestador: «Optimización activa» (túnel conectado), «ruta directa en monitoreo», «Privilegios requeridos» (antes de intentar el túnel), «Túnel degradado» con failback, «Red restaurada» (emergencia/recuperación) — todas llegan a la barra de estado, a la tarjeta del panel y (errores) a un MessageBox.
- **Tema oscuro/claro**: diccionarios `Themes/Dark.xaml` y `Themes/Light.xaml` con la misma clave, `DynamicResource` en todas las vistas/estilos, `ThemeManager` que intercambia el diccionario en caliente y selector en Configuración (persistido en `AppSettings.Theme`, aplicado al arrancar y al guardar). Pinceles del estado global y del gráfico siguen al tema.
- **Gráficos**: el control se suscribe a cambios de colección (la serie se repinta con cada muestra), submuestreo a un punto por píxel, redibujo coalescido por frame y colores de ejes/rejilla desde el tema.
- **Honestidad**: textos que explican qué se está midiendo y que la herramienta avisará solo cuando haya una mejora clara; advertencia de modo TÚNEL GLOBAL persistente en el panel.

## Bloqueos
- Ninguno activo. Riesgo controlado: sin SDK local → validación vía CI (documentado en PLAN.md). Los logs
  de jobs de Actions no son descargables desde esta máquina; los errores se leen por anotaciones del job.

## Comandos útiles (con SDK 8)
```powershell
dotnet build src/GameRouteOptimizer.sln -c Release
dotnet test  src/GameRouteOptimizer.sln -c Release
dotnet run   --project src/GameRouteOptimizer.App -c Release
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

## Cómo continuar si esta sesión se interrumpe
1. Lee PLAN.md secciones 6-7 y este STATE.md.
2. Sigue la fase ⏳/🔄 más temprana; cada cambio debe dejar `dotnet build` + `dotnet test` en verde (CI) y
   actualizar PLAN/STATE/ROADMAP.
3. Reglas de oro: no introducir telemetría ni dependencias cloud; toda operación de red privilegiada debe
   pasar por la capa de operaciones (Cli/Service elevado); los tests no dependen de red externa.
