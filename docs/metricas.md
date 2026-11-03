# Métricas de calidad: definiciones y medición

| Métrica | Definición operativa | Método | Objetivo de diseño |
|---|---|---|---|
| p50 / p95 / p99 RTT | Percentil nearest-rank sobre histograma de 1 ms (0–2000) en ventana de 60 s | `core::percentile` + vectores fijos | Exactitud ±1 ms |
| Jitter | `mean(\|RTT[i]-RTT[i-1]\|)` sobre recibidas consecutivas; además variante RFC 3550 `J += (\|D\|-J)/16` | `core::metrics` | Reportar ambas |
| Pérdida | `1 - recibidos/enviados` con gracia de reordenamiento | Número de secuencia | Sin falsos positivos por reordenamiento |
| Estabilidad de ruta | `1 - flaps/ventanas` + EWMA del score | `core::decide` | ≥0,95 en red estable |
| TTD (detección de caída) | Fallo inyectado → `Decision::Failover` | `tests/lab/netem.sh` (root, Fase 3) | ≤3 s con sondas a 200 ms |
| TTF (failover) | Decisión → primer probe exitoso por la nueva ruta | Laboratorio + `AppliedRouteProof` | ≤2 s |
| CPU / RAM | p95 de 10 min, separando UI/engine/helper | `scripts/bench.sh` | UI <120 MB, helper <60 MB, <2 % CPU en 4 núcleos |
| Impacto de throughput | Delta Mbps con/sin túnel | `iperf3` contra servidor **del usuario**, nunca automático | Reportar, no optimizar |
| Falsos positivos | Cambios de ruta sin degradación inyectada en 30 min | `run_lab.py` con semilla fija | 0 (logrado en el laboratorio actual) |

## Scoring

```
loss_eq  = min(300, 300*loss / max(0.05, 1-loss))
tail     = p95 - p50
jitter   = mean(|ΔRTT|)
instab   = clamp(0.6*(jitter/20) + 0.4*(flaps_10m/5), 0, 1)
score(r) = w_rtt*p50 + w_jit*jitter + w_loss*loss_eq + w_tail*tail
         + w_load*(100*load) + w_stab*(10*instab) + w_geo*geo_cost_ms
```

Pesos por defecto: `{rtt 1.0, jitter 2.0, loss 1.0, tail 1.0, load 0.5,
stability 1.5, geo 0.25}`, todos configurables en `[0, 10]`. Justificación en
ADR-0005. Histéresis `max(8 ms, 12 %)` durante 3 ventanas, cooldown 60 s, máximo
3 cambios/10 min, failover duro con 15 timeouts o pérdida >10 %.

## Resultado reproducible actual

`python3 tests/lab/run_lab.py` → `tests/lab/report.json`:

- 10 vectores analíticos verificados (Rust y Python comparten el fixture).
- p50 3,0 ms · p95 5,0 ms · p99 5,0 ms · jitter 1,287 ms · pérdida 1,67 %.
- Score directa 13,239 vs. relay simulado 32,739 → **recomendado: directa**.
- Falsos positivos de cambio de ruta: **0**.

Condiciones: semilla 42, 60 sondas UDP, intervalo 20 ms, eco en loopback con
2 % pérdida y 3 ± 2 ms, sobrecarga de relay simulada de 12 ms y carga 0,15.
