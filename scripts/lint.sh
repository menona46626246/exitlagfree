#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
cargo clippy --workspace --all-targets
cargo clippy -p tramo-core -p tramo-config -p tramo-ipc -p tramo-netctl --all-targets -- -D warnings || {
  echo "AVISO: clippy estricto en crates endurecidos reporta avisos (ver salida)" >&2
  exit 1
}
