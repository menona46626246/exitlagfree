# STATE — GameRoute Optimizer

Última actualización: 2026-09-08 — **CI en verde** (run `34215593479`, commits `f74c9b5` → `0d723ee`).

## Entorno de desarrollo de esta sesión
- Máquina local: Linux (Debian) **sin SDK de .NET** y con nuget.org bloqueado → la compilación y los tests
  se ejecutan en **GitHub Actions** (`windows-latest` + `ubuntu-latest`) contra este repositorio; el estado
  real de compilación/tests es el del workflow CI (`Actions` → `CI`). El código C# de net8.0 (no-WPF) también
  es compilable en Linux con SDK 8 si se dispone de él.
- El job de Windows compila la solución completa (incluida la app WPF), ejecuta la suite xUnit y hace un
  **smoke-test** de la UI (`--smoke-test`: construye servicios + ventana principal y sale con código 0).
- El job de Linux compila Core + Cli + Service y ejecuta los tests del núcleo.

## Fases

| Fase | Contenido | Estado |
|---|---|---|
| 0 | Scaffold: sln, 5 proyectos, CI (sin acciones de terceros), README, PLAN | ✅ CI verde |
| 1 | Modelos + configuración + SQLite + JSON | ✅ (CI verde; tests de roundtrip a ampliar) |
| 2 | Diagnóstico: probes (ICMP/TCP/UDP/HTTP), traceroute, métricas, scoring + vista UI | ✅ implementado y compilado; medición real pendiente de validar en Windows físico |
| 3 | Gestión de juegos: CRUD, import/export JSON, detección de instalados | ✅ implementado (detección usa rutas comunes; solo en Windows) |
| 4 | Relays: CRUD, importación `.conf`, claves privadas cifradas con DPAPI | ✅ implementado |
| 5 | Túnel WireGuard: locator de `wireguard.exe`, activación/desactivación, restauración | ✅ implementado; requiere WireGuard instalado y UAC (validación en máquina real pendiente) |
| 6 | Enrutamiento: rutas por destino/globales, kill switch, failback, botón de emergencia | ✅ implementado y compilado; operaciones privilegiadas vía CLI/Service elevado |
| 7 | Monitoreo: sesión, eventos, historial, gráficos en vivo, exportación Markdown | ✅ implementado |
| 8 | Auto-optimización: histéresis, cooldown, estabilidad, failover | ✅ implementado (scoring + orquestador) |
| 9 | Seguridad/UX final: avisos legales, modo oscuro español, botón de emergencia, logs locales | ✅ implementado |
| 10 | Tests ampliados y documentación final | 🔄 tests básicos en verde; ampliar cobertura; docs completas |

## Qué valida el CI hoy
- ✅ Compilación Release de Core, Cli, Service, App (WPF) y Tests.
- ✅ Tests xUnit (hoy: SmokeTests básicos) en Linux y Windows.
- ✅ Smoke-test de la UI en Windows (la ventana se construye y cierra sola con código 0).
- ⏳ Pendiente de validación **en una máquina Windows real** (no automatizable en runners): creación del
  túnel WireGuard, UAC/elevación, cambio de rutas y kill switch, sondas reales a servidores de juego.

## Bloqueos
- Ninguno activo. Riesgo controlado: sin SDK local → validación vía CI (documentado en PLAN.md).

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
