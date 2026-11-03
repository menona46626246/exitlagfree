#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
cargo fmt --all
echo "Formato aplicado. Commitea el resultado."
