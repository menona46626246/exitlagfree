# Changelog

Todas las versiones siguen [Keep a Changelog](https://keepachangelog.com/es/1.1.0/) y SemVer.

## [0.1.0] - Sin publicar

### Añadido
- Propuesta técnica y ADRs (Fases 0-1).
- Workspace Cargo con 12 crates y licencia Apache-2.0.
- `core`: histograma de 1 ms, percentiles p50/p95/p99, jitter, pérdida, scoring
  configurable, histéresis, cooldown, failover y modos de decisión.
- `config`: esquema versionado, perfiles, reglas e import/export sin secretos.
- `probe`: sondas UDP, TCP e ICMP (según permisos) y diagnóstico.
- `tramoctl`: medición, scoring offline, diagnóstico y validación de config.
- Laboratorio reproducible (`tests/lab/`) con eco UDP, oráculo de scoring yreporte JSON.
- API de nodos y health, `compose.yaml`, playbook Ansible y CI.
- Documentación de build, permisos, limitaciones, seguridad y privacidad.

### Pendiente (siguientes bloques)
- Backends de túnel WireGuard y helper privilegiado con rollback.
- Split tunneling y UI Tauri completa.
