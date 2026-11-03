#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Idempotente: crea el workspace solo si no existe. Seguro de reejecutar.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
if [[ ! -f Cargo.toml ]]; then
  cat > Cargo.toml <<'TOML'
[workspace]
resolver = "2"
members = ["app/src-tauri", "core", "config", "probe", "engine", "tunnel", "netctl", "netctl/helper", "ipc", "api", "node/agent", "cli"]
[workspace.package]
version = "0.1.0"
edition = "2021"
rust-version = "1.82"
license = "Apache-2.0"
TOML
fi
mk() { if [[ ! -f "$1/Cargo.toml" ]]; then cargo new --lib --vcs none --name "$2" "$1"; fi; }
mk core tramo-core
mk config tramo-config
mk probe tramo-probe
mk engine tramo-engine
mk tunnel tramo-tunnel
mk netctl tramo-netctl
mk ipc tramo-ipc
mk api tramo-api
mk cli tramoctl
mk node/agent tramo-node-agent
mk app/src-tauri tramo-desktop
if [[ ! -f netctl/helper/Cargo.toml ]]; then cargo new --bin --vcs none --name tramo-helper netctl/helper; fi
cargo generate-lockfile
echo "Workspace listo. Ejecuta: bash scripts/test.sh"
