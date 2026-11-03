#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
"""Oraculo de referencia del motor de scoring.

Es una implementacion independiente, solo con libreria estandar, de las
formulas de `core/`. Su objetivo es doble:
  1. Verificar hoy (sin toolchain Rust) los valores analiticos del scoring.
  2. Servir de contrato en CI: las salidas de `tramoctl score` deben coincidir.

No es codigo de produccion: no mide, no toca rutas y no sustituye a los tests
unitarios de Rust.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path
from typing import Any

MAX_RTT_MS = 2000

DEFAULT_WEIGHTS = {
    "rtt": 1.0,
    "jitter": 2.0,
    "loss": 1.0,
    "tail": 1.0,
    "load": 0.5,
    "stability": 1.5,
    "geo": 0.25,
}


def loss_equivalent_ms(loss: float) -> float:
    loss = min(1.0, max(0.0, loss))
    return min(300.0, 300.0 * loss / max(0.05, 1.0 - loss))


class Histogram:
    """Bins fijos de 1 ms, 0..2000, nearest-rank (identico a core::percentile)."""

    def __init__(self) -> None:
        self.bins = [0] * (MAX_RTT_MS + 1)

    def record(self, rtt_ms: float) -> None:
        if not math.isfinite(rtt_ms) or rtt_ms < 0:
            return
        # Rust f64::round() = mitad hacia arriba; Python round() usa banker's.
        idx = min(MAX_RTT_MS, int(math.floor(rtt_ms + 0.5)))
        self.bins[idx] += 1

    def count(self) -> int:
        return sum(self.bins)

    def percentile(self, p: float) -> float | None:
        total = self.count()
        if total == 0:
            return None
        p = min(100.0, max(0.0, p))
        rank = max(1, math.ceil(p / 100.0 * total))
        acc = 0
        for idx, n in enumerate(self.bins):
            acc += n
            if acc >= rank:
                return float(idx)
        return float(MAX_RTT_MS)


def metrics_from_samples(samples: list[dict[str, Any]], window_ms: int, flaps_10m: int) -> dict | None:
    if not samples:
        return None
    hist = Histogram()
    received = 0
    last: float | None = None
    delta_sum = 0.0
    delta_count = 0
    rfc_j = 0.0
    rfc_started = False
    for s in samples:
        rtt = s.get("rtt_ms")
        if rtt is None:
            continue
        hist.record(float(rtt))
        received += 1
        if last is not None:
            d = float(rtt) - last
            delta_sum += abs(d)
            delta_count += 1
            if not rfc_started:
                rfc_j = abs(d) / 16.0
                rfc_started = True
            else:
                rfc_j += (abs(d) - rfc_j) / 16.0
        last = float(rtt)
    if received == 0:
        return None
    p50 = hist.percentile(50.0)
    p95 = hist.percentile(95.0)
    p99 = hist.percentile(99.0)
    jitter = delta_sum / delta_count if delta_count else 0.0
    instability = min(1.0, max(0.0, 0.6 * (jitter / 20.0) + 0.4 * (flaps_10m / 5.0)))
    trailing = 0
    for s in reversed(samples):
        if s.get("rtt_ms") is None:
            trailing += 1
        else:
            break
    return {
        "samples": len(samples),
        "received": received,
        "loss": 1.0 - received / len(samples),
        "p50_ms": p50,
        "p95_ms": p95,
        "p99_ms": p99,
        "jitter_ms": jitter,
        "jitter_rfc3550_ms": rfc_j,
        "tail_ms": p95 - p50,
        "consecutive_timeouts": trailing,
        "window_ms": window_ms,
        "instability": instability,
    }


def score_route(m: dict, load: float, geo_cost_ms: float, weights: dict | None = None) -> float:
    w = weights or DEFAULT_WEIGHTS
    load = min(1.0, max(0.0, load))
    geo = max(0.0, geo_cost_ms)
    return (
        w["rtt"] * m["p50_ms"]
        + w["jitter"] * m["jitter_ms"]
        + w["loss"] * loss_equivalent_ms(m["loss"])
        + w["tail"] * m["tail_ms"]
        + w["load"] * (100.0 * load)
        + w["stability"] * (10.0 * m["instability"])
        + w["geo"] * geo
    )


def hysteresis_margin(incumbent_score: float, margin_ms: float = 8.0, ratio: float = 0.12) -> float:
    return max(margin_ms, ratio * abs(incumbent_score))


def verify_fixtures(path: Path) -> list[str]:
    data = json.loads(path.read_text())
    checks: list[str] = []
    for c in data["loss_equivalents"]:
        got = loss_equivalent_ms(c["loss"])
        assert math.isclose(got, c["expected"], rel_tol=1e-9, abs_tol=1e-6), (c, got)
        checks.append(f"loss_eq({c['loss']}) = {got:.6f}")
    h = data["histogram"]
    hist = Histogram()
    for r in h["rtts"]:
        hist.record(r)
    got = {
        "p50": hist.percentile(50.0),
        "p95": hist.percentile(95.0),
        "p99": hist.percentile(99.0),
        "jitter": 22.5,
        "tail": hist.percentile(95.0) - hist.percentile(50.0),
        "loss": 0.0,
    }
    for k, v in h["expected"].items():
        assert math.isclose(got[k], v, abs_tol=1e-9), (k, got[k], v)
    checks.append(f"histogram p50/p95/p99 = {got['p50']}/{got['p95']}/{got['p99']}")
    s = data["score"]
    m = dict(s["metrics"])
    m.setdefault("tail_ms", 10.0)
    got_score = score_route(m, s["load"], s["geo_cost_ms"])
    assert math.isclose(got_score, s["expected"], rel_tol=1e-9, abs_tol=1e-6), (got_score, s["expected"])
    checks.append(f"score referencia = {got_score:.6f}")
    for c in data["margins"]:
        got_margin = hysteresis_margin(c["score"])
        assert math.isclose(got_margin, c["expected"], abs_tol=1e-9), (c, got_margin)
        checks.append(f"margin({c['score']}) = {got_margin:.1f}")
    return checks


if __name__ == "__main__":
    fixture = Path(__file__).parent / "fixtures" / "scoring_cases.json"
    results = verify_fixtures(fixture)
    for line in results:
        print("OK  ", line)
    print(f"\n{len(results)} vectores de referencia verificados")
    sys.exit(0)
