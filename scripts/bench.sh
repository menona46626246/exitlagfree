#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
python3 tests/lab/run_lab.py
echo "Reporte: tests/lab/report.json"
echo "Para TTD/TTF con tunel real (root, Fase 3): sudo bash tests/lab/netem.sh"
echo "Throughput: iperf3 contra un servidor PROPIO, nunca automatico."
