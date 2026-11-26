# STATE.md — Estado del proyecto

**Última actualización:** 2026-11-25 · **Versión:** 1.0.0 · **Entorno de desarrollo:** Windows 10/11 + GitHub Actions CI (`windows-latest`)

## ✅ Completado

| Fase | Entregables | Verificación |
|------|-------------|--------------|
| 0 — Scaffold | Solución, 5 proyectos, configs, `.editorconfig`, README | Compila (Core/Service/Cli/Tests en Linux) |
| 1 — Modelos y config | GameProfile/GameServer/RelayNode/AppSettings/TunnelProfile; ConfigStore/DataStore JSON atómicos; SessionDatabase SQLite; import/export JSON de perfiles y relays | 80 tests verdes |
| 2 — Diagnóstico | ProbeEngine (ICMP/TCP/UDP/HTTP + fallback + rate limit), TracerouteEngine (TTL + fallback), ProbeSummaryCalculator (p95/p99/jitter/estabilidad), ScoreEngine | Tests de stats y fallbacks; CLI `probe`/`trace` demostrados |
| 3 — Gestión de juegos | ProfileManager CRUD, detección de procesos, detección de instalados (autorizada), vigilancia auto-start | Tests de perfiles |
| 4 — Relays | RelayManager, import .conf WG con cifrado de clave privada, RelayProber, export sin secretos | Tests verifican que la clave nunca queda en claro |
| 5 — Túnel | WireGuardConfigParser (validación dura), WireGuardTunnelManager (herramientas oficiales, parser `wg show`, backoff), SecretProtector (DPAPI/AES-GCM) | Test: conf inválida rechazada antes de aplicar |
| 6 — Routing | RouteCalculator (planes + snapshot + restore), KillSwitchManager (GameRouteKS_*, reversible), PrivilegedOps reales (netsh/route/ip) | Tests de generación y restauración de rutas; kill switch activa/desactiva en simulado |
| 7 — Monitoreo | SessionRecorder (SQLite), eventos, antes/después, informes Markdown/JSON, NotificationService | Tests de eventos, historial e informes |
| 8 — Auto-optimización | HysteresisPolicy (umbral, cooldown, límite horario, consistencia), failback por caída y por calidad, failover entre relays | 8 tests de anti-flapping/failback |
| Servicio + IPC | Servicio elevado con servidor IPC (ping/status/tunnel/routes/killswitch/emergency), snapshot de rutas, logging; cliente IPC en App/CLI | Compila; IPC test end-to-end (ping/pong) |
| App WPF | UI española MVVM estilo oscuro/carmesí frameless: Dashboard, Juegos (+diálogo), Relays (+diálogo), Diagnóstico, Sesión, Configuración, Logs, Asistente inicial; LatencyChart propio; ícono `app.ico`; botón de emergencia | Compilada y smoke-testeada en Windows (`dist/GameRouteOptimizer.exe`) |
| 10 — Tests y docs | **80/80 xUnit verdes**; README, PLAN, STATE, ROADMAP, ARCHITECTURE, SECURITY; 10 guías en `docs/`; ejemplos JSON; scripts build/install/pack | Ver sección Comandos |

## 🔄 En progreso

- Nada bloqueante. El código está completo y la lógica central verificada.

## ⏳ Pendiente (no bloquea la Definición de Completado)

- [x] Compilar y smoke-testear la **App WPF** en Windows (`dotnet build GameRouteOptimizer.sln` y `dotnet run --project src/GameRouteOptimizer.App`). Resueltos los imports y clases duplicadas en el code-behind.
- [x] `dotnet format` y limpieza de advertencias del compilador en toda la solución; CI configurado en `.github/workflows/ci.yml`.
- Probar túnel real contra un relay WireGuard real (no existe relay en el entorno de
  desarrollo; el flujo está cubierto con simulados y validaciones reales).

## 🚧 Bloqueos del entorno (con mitigación aplicada)

| Bloqueo | Mitigación |
|---------|-----------|
| Sin Windows en el entorno de build actual (Debian 13) | D9: Core/Service/Cli/Tests compilan y pasan 80 tests en Linux; App compila en Windows con `scripts\build.ps1` |
| Sin un relay WireGuard real disponible | Modo túnel implementado contra herramientas oficiales; validación y estado cubiertos por tests con fakes + demo real de validación `.conf` |
| ICMP sin permisos en contenedor | El motor de probes ya maneja `ProbeUnsupportedException` con fallback a TCP (test incluido); la CLI demostró TCP probe real |

## Comandos verificados

```bash
# Linux (verificado en este entorno)
./scripts/build.sh                 # Core, Service, Cli, Tests + 80/80 tests
./scripts/run-tests.sh

# Windows (producir y ejecutar la app completa)
powershell -File scripts\build.ps1 -Release
dotnet run --project src/GameRouteOptimizer.App -c Release
powershell -File installer\install-service.ps1      # como admin (modo túnel)
powershell -File installer\create-installer.ps1     # paquete portable zip
```

## Instrucciones para continuar (si se retoma en otra sesión)

1. `dotnet build GameRouteOptimizer.sln` en Windows (todo incluida la App).
2. `dotnet test src/GameRouteOptimizer.Tests` — debe seguir 80/80.
3. Smoke test de UI: añadir juego (examples/game-profile.example.json), añadir relay
   (importar examples/wireguard-tunnel.example.conf), diagnóstico a 1.1.1.1.
4. Instalar servicio y probar túnel real; revisar `route-snapshot.json` y restauración.
5. Marcar pendientes de arriba conforme se cierren.
