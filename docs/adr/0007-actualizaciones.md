# ADR-0007 — Actualizaciones firmadas

**Estado:** Planificado (Fase 8)

Tauri updater con firma Ed25519/minisign, verificación obligatoria antes de instalar
y clave pública compilada. Releases con `SHA256SUMS` firmados con Sigstore/cosign
keyless (OIDC de GitHub Actions) y provenance SLSA. El chequeo es **opt-in** y no
ocurre en modo offline. Rotación de claves documentada en `SECURITY.md`.
Hoy `tauri.conf.json` mantiene `updater.active = false` a propósito: nunca se
distribuye un binario sin verificación de firma.
