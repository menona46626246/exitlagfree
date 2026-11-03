# Tests de integración

- `core/tests/scoring_vectors.rs` compara la implementación Rust con los vectores
  analíticos de `tests/lab/fixtures/scoring_cases.json` usados por el oráculo Python.
- `tests/lab/run_lab.py` mide UDP real contra `echo_server.py` y genera `report.json`.
- `ipc/src/unix.rs` incluye un test que levanta un UDS real con permisos 0600 y
  verifica el despacho de comandos; otro verifica que un socket 0666 se rechaza.
- `api/src/registry.rs` y `ratelimit.rs` validan el contrato de nodos/health y el
  límite 60 req/min/IP.

Pendiente Fase 3: `tunnel_updown.rs` (100 ciclos up/down sin dejar rutas huérfanas),
`probe_netem.rs` (inyección de pérdida) y TTD/TTF con `netem.sh`.
