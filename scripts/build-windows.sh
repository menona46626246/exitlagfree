#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Ejecutar en PowerShell/Git Bash con MSVC Build Tools y WebView2.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
rustup target add x86_64-pc-windows-msvc
cargo build --release --locked --target x86_64-pc-windows-msvc -p tramoctl -p tramo-api -p tramo-node-agent -p tramo-helper
pnpm --dir app/ui install --frozen-lockfile
pnpm --dir app/ui build
echo "Binarios en target/x86_64-pc-windows-msvc/release. MSI/NSIS en Fase 8."
