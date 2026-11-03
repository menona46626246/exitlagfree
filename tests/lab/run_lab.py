#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
"""Benchmark reproducible del laboratorio local (sin root, sin Docker).

Mide una ruta "directa" real contra un eco UDP con perdida/jitter inyectados,
simula un relay con sobrecarga fija, puntua ambas rutas y aplica histeresis.
Escribe tests/lab/report.json. NO es una prueba de un tunel real: el tunel
pertenece a la Fase 3 y se medira con `netem.sh` en un host Linux con root.
"""
from __future__ import annotations

import json
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

HERE = Path(__file__).parent
sys.path.insert(0, str(HERE))
from score_oracle import (  # noqa: E402
    DEFAULT_WEIGHTS,
    hysteresis_margin,
    metrics_from_samples,
    score_route,
    verify_fixtures,
)

RELAY_OVERHEAD_MS = 12.0
RELAY_LOAD = 0.15
COUNT = 60
INTERVAL_MS = 20
K_WINDOWS = 3
MARGIN_MS = 8.0
RATIO = 0.12


def start_echo() -> tuple[subprocess.Popen, int]:
    proc = subprocess.Popen(
        [sys.executable, str(HERE / "echo_server.py"), "--port", "0",
         "--loss", "0.02", "--delay-ms", "3", "--jitter-ms", "2", "--seed", "42"],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True,
    )
    assert proc.stdout is not None
    line = proc.stdout.readline().strip()
    if not line.startswith("PORT="):
        raise RuntimeError(f"el eco no arranco: {line!r}")
    return proc, int(line.split("=", 1)[1])


def simulate_relay(samples: list[dict]) -> list[dict]:
    """Un relay anade propagacion + encapsulado. Se modela como retardo fijo;
    se etiqueta como SIMULACION en el reporte para no inventar capacidades."""
    out = []
    for s in samples:
        rtt = s["rtt_ms"]
        out.append({"seq": s["seq"], "rtt_ms": None if rtt is None else rtt + RELAY_OVERHEAD_MS})
    return out


def main() -> int:
    vectors = verify_fixtures(HERE / "fixtures" / "scoring_cases.json")
    proc, port = start_echo()
    try:
        time.sleep(0.25)
        t0 = time.monotonic()
        raw = subprocess.check_output(
            [sys.executable, str(HERE / "measure_probe.py"),
             "--target", f"127.0.0.1:{port}", "--count", str(COUNT),
             "--interval-ms", str(INTERVAL_MS), "--timeout-ms", "200"],
            text=True,
        )
        elapsed_ms = (time.monotonic() - t0) * 1000.0
        measured = json.loads(raw)
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc.kill()

    direct = measured["metrics"]
    relay = metrics_from_samples(simulate_relay(measured["samples"]), measured["window_ms"], 0)

    scores = {
        "direct": score_route(direct, 0.0, 0.0, DEFAULT_WEIGHTS),
        "relay_simulado": score_route(relay, RELAY_LOAD, 0.0, DEFAULT_WEIGHTS),
    }
    incumbent = "direct"
    challenger = min(scores, key=scores.get)
    margin = hysteresis_margin(scores[incumbent], MARGIN_MS, RATIO)
    would_switch = challenger != incumbent and scores[challenger] + margin < scores[incumbent]

    # Falsos positivos: en una red estable ningun cambio deberia dispararse.
    decisions = []
    streak = 0
    for window in range(K_WINDOWS + 2):
        if would_switch:
            streak += 1
        else:
            streak = 0
        decisions.append({"window": window + 1, "streak": streak, "decision": "keep" if not would_switch else ("apply" if streak >= K_WINDOWS else "keep")})
    false_positives = sum(1 for d in decisions if d["decision"] == "apply")

    report: dict[str, Any] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "reproducibility": {
            "seed": 42, "count": COUNT, "interval_ms": INTERVAL_MS,
            "echo_loss": 0.02, "echo_delay_ms": 3, "echo_jitter_ms": 2,
            "relay_overhead_ms": RELAY_OVERHEAD_MS, "relay_load": RELAY_LOAD,
            "command": "python3 tests/lab/run_lab.py",
            "note": "El relay es una SIMULACION con retardo fijo; no hay tunel real en este bloque.",
        },
        "reference_vectors_verified": len(vectors),
        "measurement": {
            "protocol": "udp", "samples": direct["samples"], "received": direct["received"],
            "p50_ms": direct["p50_ms"], "p95_ms": direct["p95_ms"], "p99_ms": direct["p99_ms"],
            "jitter_ms": direct["jitter_ms"], "jitter_rfc3550_ms": direct["jitter_rfc3550_ms"],
            "loss": direct["loss"], "tail_ms": direct["tail_ms"],
            "elapsed_ms": round(elapsed_ms, 1),
        },
        "relay_simulated": {
            "p50_ms": relay["p50_ms"], "jitter_ms": relay["jitter_ms"],
            "loss": relay["loss"], "load": RELAY_LOAD,
        },
        "scoring": {"weights": DEFAULT_WEIGHTS, "scores": scores, "hysteresis_margin_ms": margin},
        "decision": {
            "incumbent": incumbent, "recommended": challenger,
            "direct_is_better": scores["direct"] <= scores["relay_simulado"] + margin,
            "reason": "La ruta directa gana: el relay anade sobrecarga sin evitar un tramo malo." if scores["direct"] <= scores["relay_simulado"] + margin else "El relay supera la histeresis (poco habitual en laboratorio local).",
            "windows": decisions,
        },
        "quality": {
            "false_positive_route_changes": false_positives,
            "ttd_ms": None, "ttf_ms": None,
            "note": "TTD/TTF requieren inyectar caida con tc netem (netem.sh, root) en Fase 3.",
        },
    }
    out = HERE / "report.json"
    out.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({
        "vectores_verificados": len(vectors),
        "p50_ms": direct["p50_ms"], "p95_ms": direct["p95_ms"], "p99_ms": direct["p99_ms"],
        "jitter_ms": round(direct["jitter_ms"], 3), "loss": direct["loss"],
        "score_direct": round(scores["direct"], 3),
        "score_relay_simulado": round(scores["relay_simulado"], 3),
        "recomendado": challenger,
        "falsos_positivos": false_positives,
        "reporte": str(out),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
