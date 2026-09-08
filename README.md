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

## Guía de uso (resumen)

1. **Juegos**: crea tu perfil (nombre + ejecutable) y añade al menos un servidor objetivo (dominio/IP +
   puertos) en la sección *Juegos*. Guarda.
2. **Relays**: importa tu propio `.conf` de WireGuard o crea un relay con su endpoint y clave pública.
   Usa *Probar* para medirlo; la clave privada se guarda cifrada con DPAPI y nunca se exporta.
3. **Diagnóstico** (sin túnel): mide ruta directa vs relays y traza la ruta. La tabla de comparación te
   dice, con las mismas sondas, si algún relay es realmente mejor.
4. **Panel principal**: elige el juego y pulsa **Optimizar**. El programa mide, compara y:
   - si el modo es *ruta directa*: monitorea y te avisa solo si un relay mejora de forma clara;
   - si el modo usa túnel: pide confirmación (o cambia solo si el auto-switch está activado con
     histéresis, estabilidad y cooldown) y crea el túnel WireGuard.
5. **Sesión**: eventos en vivo (cambios de ruta, avisos, errores), historial y exportación del informe
   Markdown.
6. **Configuración**: tema **oscuro/claro** (se aplica al guardar), sondas, auto-switch y kill switch
   (requiere administrador; lee su aviso antes de activarlo).
7. Ante cualquier problema con la red: botón **🛑 Detener y restaurar red**, siempre visible.

Más detalle y solución de problemas en `docs/` (guías de juegos, relays y túnel). Las métricas son
estimaciones honestas: lee `docs/limitaciones.md` para saber qué puede y qué no puede hacer.

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
