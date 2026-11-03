# ADR-0005 — Scoring en ms-equivalentes

**Estado:** Aceptado

Fórmula y pesos en `docs/00-propuesta-tecnica.md` §3.4 y `docs/metricas.md`.
Decisiones clave: jitter pesa 2× (la varianza duele más que la media), la carga del
nodo pesa 0,5× porque es **autodeclarada y no confiable**, `geo_cost_ms` es 0 por
defecto y nunca se infiere de la ubicación del usuario, y la pérdida se convierte con
`min(300, 300*loss/max(0.05, 1-loss))`.

Histéresis `max(8 ms, 12 %)` durante 3 ventanas, cooldown 60 s, máximo 3 cambios
por 10 min, failover duro ante 15 timeouts o pérdida >10 %. Modo por defecto
`solo medir`. `direct` es siempre candidato con carga 0: si la física no ayuda,
la ruta directa gana y la UI lo dice literalmente.
