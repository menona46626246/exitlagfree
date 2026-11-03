#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
cargo build --release --locked -p tramoctl -p tramo-api -p tramo-node-agent -p tramo-helper
pnpm --dir app/ui install --frozen-lockfile
pnpm --dir app/ui build
echo "Binarios en target/release. El empaquetado .deb/AppImage llega en Fase 8."
echo "Para el cliente con UI: cargo tauri build (Fase 6, requiere WebKitGTK)."
