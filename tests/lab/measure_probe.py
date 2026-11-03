#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
"""Sonda UDP real contra el eco del laboratorio; imprime metricas en JSON."""
from __future__ import annotations

import argparse
import json
import socket
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from score_oracle import metrics_from_samples, score_route  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", required=True, help="host:puerto")
    ap.add_argument("--count", type=int, default=60)
    ap.add_argument("--interval-ms", type=int, default=20)
    ap.add_argument("--timeout-ms", type=int, default=200)
    args = ap.parse_args()
    host, port = args.target.rsplit(":", 1)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(args.timeout_ms / 1000.0)
    samples: list[dict] = []
    for seq in range(args.count):
        payload = seq.to_bytes(8, "big") + b"tramoprobe"
        sent = time.monotonic()
        try:
            sock.sendto(payload, (host, int(port)))
            data, _ = sock.recvfrom(65535)
            rtt = (time.monotonic() - sent) * 1000.0
            if len(data) < 8 or data[:8] != seq.to_bytes(8, "big"):
                rtt = None
        except socket.timeout:
            rtt = None
        samples.append({"seq": seq, "rtt_ms": rtt})
        remaining = args.interval_ms / 1000.0 - (time.monotonic() - sent)
        if remaining > 0:
            time.sleep(remaining)
    sock.close()

    window_ms = max(10_000, args.interval_ms * args.count)
    m = metrics_from_samples(samples, window_ms, 0)
    out = {
        "target": args.target,
        "protocol": "udp",
        "samples": samples,
        "window_ms": window_ms,
        "metrics": m,
        "score_direct": score_route(m, 0.0, 0.0) if m else None,
    }
    print(json.dumps(out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
