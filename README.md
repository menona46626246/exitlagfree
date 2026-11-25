# README — GameRoute Optimizer

Aplicación de escritorio (Windows 10/11) para **diagnosticar y optimizar rutas de red en
videojuegos**: mide latencia, pérdida y jitter, compara tu ruta directa con relays
WireGuard tuyos, aplica el mejor camino disponible y **restaura la red si algo falla**.

> **Honestidad primero:** no existe "ping mágico". Si tu ruta directa ya es la mejor,
> este programa te lo dirá y no activará ningún túnel.

## Qué hace (y qué no hace)

| Sí hace | No hace |
|---------|---------|
| Medir ping/pérdida/jitter (ICMP, TCP connect, UDP, HTTP/S) | No inyecta nada en los juegos |
| Traceroute tipo mtr con detección de cuellos de botella | No lee memoria ni hace hooking |
| Comparar ruta directa vs relays tuyos con score explicado | No hace DPI ni captura tráfico |
| Aplicar túneles WireGuard (herramientas oficiales, Wintun) | No evade bans ni geo-bloqueos |
| Kill switch reversible por firewall de Windows | No tiene telemetría, cuentas ni pagos |
| Historial de sesiones con antes/después e informes | No usa drivers propios ni backend cloud |

## Estructura del repo

```
GameRouteOptimizer.sln
├─ src/GameRouteOptimizer.Core      Lógica central (net8.0, multiplataforma)
├─ src/GameRouteOptimizer.App       UI WPF en español (net8.0-windows)
├─ src/GameRouteOptimizer.Service   Servicio elevado (túneles, rutas, kill switch)
├─ src/GameRouteOptimizer.Cli       CLI multiplataforma
├─ src/GameRouteOptimizer.Tests     80 pruebas xUnit
├─ docs/                            10 guías detalladas en español
├─ installer/                       Scripts de instalación (servicio, paquete)
├─ examples/                        Perfiles/relays/conf de ejemplo
└─ scripts/                         build.ps1 / build.sh / run-tests
```

## Requisitos

- **Windows 10/11** (para la app y el servicio) con **.NET SDK 8** para compilar.
- **WireGuard for Windows** (instalador oficial de wireguard.com) — solo si quieres usar túneles.
- Para desarrollar en Linux: .NET SDK 8 compila Core/Service/Cli/Tests y pasa los tests
  (la App WPF requiere Windows).

## Compilar, probar y ejecutar

**Windows (todo incluida la App):**
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Release
# Ejecutar la app:
dotnet run --project src/GameRouteOptimizer.App -c Release
# Ejecutar la CLI:
dotnet run --project src/GameRouteOptimizer.Cli -c Release -- probe --host 1.1.1.1
```

**Linux / CI (Core, Service, Cli, Tests):**
```bash
./scripts/build.sh Release     # o: chmod +x scripts/build.sh primero
./scripts/run-tests.sh
```

**Empaquetar distribución portable (Windows):**
```powershell
powershell -ExecutionPolicy Bypass -File installer\create-installer.ps1
# genera dist\GameRouteOptimizer-1.0.0-win-x64.zip
```

## Guía rápida (5 minutos)

1. **Añade un juego** → pestaña *Juegos* → *Añadir* (nombre, ejecutable, servidor objetivo
   con dominio o IP y puertos). Puedes importar `examples/game-profile.example.json`.
2. **Añade relays** (opcional) → pestaña *Relays* → *Importar .conf WireGuard* o
   *Importar JSON* (`examples/relays.example.json`). La clave privada se cifra con DPAPI.
3. **Diagnostica** → pestaña *Diagnóstico* → escribe el servidor → *Medir* y *Traceroute*.
   No requiere administrador ni servicio.
4. **Compara** → *Comparar directa vs relays* → lee el score y la explicación.
5. **Optimiza** → pestaña *Dashboard* → *Optimizar*. Para que el túnel se aplique,
   instala el servicio elevado:

```powershell
# Como administrador:
powershell -ExecutionPolicy Bypass -File installer\install-service.ps1
```

6. **Detener siempre con**: botón *Detener* o el botón de emergencia
   *Detener y restaurar red* (sidebar). Desinstalación limpia:
   `installer\uninstall-service.ps1`.

## Privacidad

- Sin telemetría, analytics, cuentas, pagos, publicidad ni crash reporting externo.
- Logs y datos 100 % locales (`%APPDATA%\GameRouteOptimizer`).
- Claves privadas cifradas localmente (DPAPI, CurrentUser) y excluidas de los exports.

## Advertencias

- Algunos juegos prohíben VPN/túneles en sus términos de servicio: revísalos.
- El kill switch, si está activo, **bloquea tu internet** si el túnel cae (es su función);
  se desactiva desde *Configuración* o el botón de emergencia.
- No uses este programa para evadir bans, bloqueos geográficos ni políticas de servicio.

## Documentación completa

Ver `docs/` — [cómo funciona](docs/como-funciona.md),
[cómo añadir juegos](docs/como-anadir-juegos.md),
[cómo añadir relays WireGuard](docs/como-anadir-relays-wireguard.md),
[cómo optimizar](docs/como-optimizar.md),
[cómo leer métricas](docs/como-leer-metricas.md),
[limitaciones](docs/limitaciones.md),
[advertencias legales/ToS](docs/advertencias-legales.md),
[solución de problemas](docs/solucion-de-problemas.md),
[modo diagnóstico sin túnel](docs/modo-diagnostico-sin-tunel.md),
[modo túnel con administrador](docs/modo-tunel-con-administrador.md).
También: [ARCHITECTURE.md](ARCHITECTURE.md), [SECURITY.md](SECURITY.md),
[PLAN.md](PLAN.md), [STATE.md](STATE.md), [ROADMAP.md](ROADMAP.md).
