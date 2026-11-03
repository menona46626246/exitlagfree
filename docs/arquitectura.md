# Arquitectura

## Capas

| Capa | Crate | Privilegios | I/O |
|---|---|---|---|
| Presentación | `app/ui` (React/TS/Vite) | No | Solo IPC local del WebView |
| Comandos UI | `app/src-tauri` (`tramo-desktop`) | No | Serde; runtime Tauri en Fase 6 |
| Orquestación | `engine` | No | Reloj monotónico inyectado |
| Lógica pura | `core` | No | **Ninguna** (`#![forbid(unsafe_code)]`) |
| Sondas | `probe` | Opcional `CAP_NET_RAW` | Sockets propios, nunca tráfico del juego |
| Túnel | `tunnel` | Solo en el helper | Trait + 3 backends |
| Control | `netctl` + `tramo-helper` | Sí, mínimo | Enum cerrado por UDS/Named Pipe |
| Config | `config` | No | JSON local, sin secretos en export |
| Nodos | `api`, `node/agent` | No | HTTP de solo lectura |

## Flujo

1. `engine::scheduler::tick` acumula `ProbeSample` por ruta en ventanas rodantes de 60 s.
2. Las sondas directas se excluyen del túnel con `SO_MARK` + `ip rule` (Linux) o bind
   a la IP física (Windows, **fiabilidad limitada y mostrada en UI**).
3. `core::metrics` → histograma de 1 ms, p50/p95/p99, jitter, pérdida, inestabilidad.
4. `core::score` → ms-equivalentes con pesos configurables.
5. `core::decide` → histéresis, K ventanas, cooldown, límite de cambios, failover duro.
6. `engine::state` emite `Decision.apply`; solo si es `true` se envía `ApplyRoute` al helper.
7. El helper aplica el `RoutePlan`, **verifica la tabla efectiva** y mantiene un lease
   de 3 s; si el motor muere, revierte sus propias reglas.
8. `app` recibe eventos agregados a 4 Hz.

## Invariantes

- `core` no conoce sockets, archivos, `tokio` ni reloj de pared.
- Ningún backend devuelve `Ok` sin verificar la ruta aplicada.
- El helper nunca acepta cadenas libres: solo un enum de 6 comandos y un framing de 64 KiB.
- La API no acepta POST ni cuerpos: no puede recibir configuración, scripts ni binarios.
- Toda decisión expone un `reason` legible para el usuario.
