# STATE — GameRoute Optimizer

Última actualización: 2026-09-08 (fase 0 completada, CI en marcha).

## Entorno de desarrollo de esta sesión
- Máquina local: Linux (Debian) **sin SDK de .NET** y con nuget.org bloqueado → la compilación y los tests
  se ejecutan en **GitHub Actions** (windows-latest + ubuntu-latest) contra este repositorio; el estado real
  de compilación/tests es el del workflow CI (`Actions` → `CI`). El código C# de net8.0 (no-WPF) también es
  compilable en Linux con SDK 8 si se dispone de él.

## Fases

| Fase | Contenido | Estado |
|---|---|---|
| 0 | Scaffold: sln, 5 proyectos, CI, README, PLAN | ✅ hecho (a falta de validación CI) |
| 1 | Modelos + configuración + SQLite + JSON | ⏳ pendiente |
| 2 | Diagnóstico: probes, traceroute, métricas, scoring, UI | ⏳ pendiente |
| 3 | Gestión de juegos + detección | ⏳ pendiente |
| 4 | Relays + importación WireGuard | ⏳ pendiente |
| 5 | Túnel: providers, activación/desactivación | ⏳ pendiente |
| 6 | Enrutamiento: rutas por destino/globales, kill switch, fallback | ⏳ pendiente |
| 7 | Monitoreo: sesión, eventos, historial, gráficos | ⏳ pendiente |
| 8 | Auto-optimización: histéresis, cooldown, failover | ⏳ pendiente |
| 9 | Seguridad/UX final | ⏳ pendiente |
| 10 | Tests y documentación final | ⏳ pendiente |

## Bloqueos
- Ninguno activo. Riesgo controlado: sin SDK local → validación vía CI (documentado en PLAN.md).

## Comandos útiles (con SDK 8)
```powershell
dotnet build src/GameRouteOptimizer.sln -c Release
dotnet test  src/GameRouteOptimizer.sln -c Release
```

## Cómo continuar si esta sesión se interrumpe
1. Lee PLAN.md secciones 6-7 y este STATE.md.
2. Sigue la fase ⏳ más temprana; cada fase debe dejar `dotnet build` + `dotnet test` en verde (CI) y
   actualizar PLAN/STATE/ROADMAP.
3. Reglas de oro: no introducir telemetría ni dependencias cloud; toda operación de red privilegiada debe
   pasar por la capa de operaciones (Cli/Service elevado); los tests no dependen de red externa.
