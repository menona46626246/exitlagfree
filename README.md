# GameRoute Optimizer

Aplicación de escritorio para Windows (C#/.NET 8, WPF) que mide tu conexión hacia los servidores de tus
juegos, compara la **ruta directa** contra **relays WireGuard propios**, recomienda la mejor opción con
métricas reales y, si lo autorizas, crea un túnel WireGuard para encaminar solo el tráfico que eliges.
Sin cuentas, sin nube, sin telemetría: todo corre en tu máquina.

> ⚠️ Herramienta honesta: no promete bajar tu ping por arte de magia. Documenta límites físicos, avisa
> cuando una métrica no es fiable y nunca actúa sobre tu red sin tu confirmación. Revisa `docs/limitaciones.md`
> y `docs/advertencias-legales-tos.md` antes de usar.

## Documentación

| Documento | Contenido |
|---|---|
| [PLAN.md](PLAN.md) | Arquitectura, decisiones, fases, criterios de aceptación |
| [STATE.md](STATE.md) | Qué está hecho, en progreso, pendiente y bloqueado |
| [ROADMAP.md](ROADMAP.md) | Mejoras futuras |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Diseño técnico detallado |
| [SECURITY.md](SECURITY.md) | Seguridad y privacidad |
| [docs/](docs/) | Guías de uso: juegos, relays, túnel, métricas, solución de problemas… |
| [examples/](examples/) | Configuración JSON de ejemplo (juego, relays, perfil) |

## Requisitos

- Windows 10/11 x64.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (para compilar) o el runtime para ejecutar
  el portable.
- [WireGuard](https://www.wireguard.com/install/) instalado **solo** si quieres el modo túnel (opcional).

## Compilar y probar

```powershell
dotnet restore src/GameRouteOptimizer.sln
dotnet build   src/GameRouteOptimizer.sln -c Release
dotnet test    src/GameRouteOptimizer.sln -c Release
dotnet run     --project src/GameRouteOptimizer.App -c Release
```

## Estructura

```
src/
  GameRouteOptimizer.Core/      Lógica pura: modelos, probing, scoring, routing, túneles (net8.0)
  GameRouteOptimizer.App/       Interfaz WPF en español (net8.0-windows)
  GameRouteOptimizer.Service/   Operaciones privilegiadas (rutas/DNS/WireGuard) vía named pipes
  GameRouteOptimizer.Cli/       CLI: diagnóstico sin UI y ejecutor elevado de operaciones
  GameRouteOptimizer.Tests/     Suite xUnit (sin red externa)
docs/ installer/ examples/ scripts/
```

Estado actual detallado: [STATE.md](STATE.md).
