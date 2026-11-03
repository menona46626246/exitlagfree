#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Entorno de desarrollo local: API, agente de salud y UI. Ctrl+C limpia todo.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
pids=()
cleanup() { for p in "${pids[@]:-}"; do kill "$p" 2>/dev/null || true; done; }
trap cleanup EXIT INT TERM
cargo run -p tramo-api -- --addr 127.0.0.1:8080 --nodes config/examples/nodes.json & pids+=("$!")
cargo run -p tramo-node-agent -- --addr 127.0.0.1:8081 & pids+=("$!")
if command -v pnpm >/dev/null 2>&1; then
  pnpm --dir app/ui dev & pids+=("$!")
else
  echo "pnpm no disponible: la UI no se ha arrancado" >&2
fi
echo "API :8080 · agente :8081 · UI :5173"
wait
