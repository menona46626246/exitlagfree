# Estrategia de pruebas

| Nivel | Ubicacion | Que cubre | Como ejecutar |
|---|---|---|---|
| Unit | `#[cfg(test)]` en cada crate + `core/tests/` | Percentiles, jitter, pérdida, scoring, histéresis, cooldown, failover, lease, framing, validación de config | `cargo test --workspace` |
| Integración | `tests/integration/` + oráculo Python | Vectores compartidos Rust↔Python, eco UDP real, IPC por UDS, API de nodos | `python3 tests/lab/run_lab.py` |
| E2E | `tests/e2e/` (Fase 6) | Dashboard, toggle, perfiles, cambio de modo con `tauri-driver` | `pnpm --dir app/ui test:e2e` |
| Laboratorio de red | `tests/lab/netem.sh` | TTD/TTF con `tc netem` (requiere root, Fase 3) | `sudo bash tests/lab/netem.sh` |

El laboratorio local (`run_lab.py`) **no necesita root ni Docker** y es
reproducible: semilla 42, 60 sondas, 20 ms de intervalo, 2 % de pérdida y
3 ± 2 ms de retardo inyectados en un eco UDP en loopback. Escribe
`tests/lab/report.json` (ignorado por Git) con las métricas p50/p95/p99,
jitter, pérdida, scores, histéresis y falsos positivos.

**Limitación honesta:** el relay del laboratorio es una simulación con retardo
fijo, no un túnel WireGuard real. Las pruebas de un túnel real llegan en Fase 3.
