# ADR-0001 — Rust como lenguaje principal y Tauri 2 como desktop

**Estado:** Aceptado · **Fecha:** 2026-11-03

## Contexto
Se comparó Go+Wails frente a Rust+Tauri frente a Electron. Verificación de licencias
realizada contra upstream: `wireguard-go` es **MIT** (históricamente GPLv2),
`boringtun` BSD-3-Clause, `wintun`/`wireguard-nt` GPLv2 como drivers del sistema,
Tauri Apache-2.0/MIT. Por tanto la licencia **no** decide el stack.

## Decisión
Rust en un único Cargo workspace + Tauri 2 + React/TS/Vite.

## Motivos
1. Memoria segura en el helper privilegiado (superficie de ataque principal).
2. Tauri 2 aporta bandeja, notificaciones, autoinicio, updater firmado y capabilities
   con un consumo de RAM muy inferior a Electron (métrica de calidad exigida).
3. Un lenguaje, un lockfile, un esquema de tipos `serde` compartido UI↔motor.
4. Backends: WireGuard del kernel por netlink (Linux), Wintun/WireGuardNT por ABI
   (Windows), `boringtun` como fallback userspace.

## Coste aceptado
Desarrollo más lento y cross-build a Windows más delicado (`cargo-xwin` en CI).
Plan B: `wireguard-go` (MIT) aislado en subproceso tras el mismo trait `TunnelBackend`.
Electron se descarta por RAM/CPU y superficie de ataque; Wails por carecer de updater
firmado y modelo de capabilities.
