#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
"""Eco UDP de diagnostico para el laboratorio. Inyecta retardo, jitter y
perdida de forma determinista (semilla fija). Solo escucha en 127.0.0.1."""
from __future__ import annotations

import argparse
import json
import random
import socket
import sys
import time


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=0)
    ap.add_argument("--loss", type=float, default=0.02)
    ap.add_argument("--delay-ms", type=float, default=3.0)
    ap.add_argument("--jitter-ms", type=float, default=2.0)
    ap.add_argument("--seed", type=int, default=42)
    args = ap.parse_args()
    rng = random.Random(args.seed)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((args.host, args.port))
    print(f"PORT={sock.getsockname()[1]}", flush=True)
    print(json.dumps({"loss": args.loss, "delay_ms": args.delay_ms, "jitter_ms": args.jitter_ms, "seed": args.seed}), file=sys.stderr, flush=True)
    try:
        while True:
            data, peer = sock.recvfrom(65535)
            if rng.random() < args.loss:
                continue
            delay = max(0.0, args.delay_ms + rng.uniform(-args.jitter_ms, args.jitter_ms)) / 1000.0
            time.sleep(delay)
            sock.sendto(data, peer)
    except KeyboardInterrupt:
        return 0
    finally:
        sock.close()


if __name__ == "__main__":
    raise SystemExit(main())
