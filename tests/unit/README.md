# Tests unitarios

Viven junto al código (`#[cfg(test)]`) para que cualquier cambio en `core/` tenga
que pasar sus pruebas. Cobertura mínima del MVP:

- `core::percentile`: nearest-rank, cuantización 1 ms, saturación a 2000 ms.
- `core::metrics`: p50/p95/p99, jitter (media y RFC 3550), pérdida con huecos,
  timeouts consecutivos, ventana sin respuestas → `None`.
- `core::score`: valores de referencia de `loss_eq`, validación de pesos,
  configurabilidad, coste geográfico opcional, pérdida vs. pequeña ventaja de p50.
- `core::decide`: histéresis, K ventanas, modos, cooldown, failover duro, límite
  de cambios, metadata obsoleta, elegibilidad por muestras, prioridad de nodo.
- `config`: CIDR, reglas, destinos, export sin secretos, rechazo de campos tipo
  `hook`/`script`/`command`.
- `ipc`: framing de 64 KiB, rechazo de comandos arbitrarios, socket 0600.
- `netctl`: lease, snapshot, allowlist del kill switch, plan determinista.

Ejecutar: `cargo test --workspace`.
