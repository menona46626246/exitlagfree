#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Bateria completa. Funciona sin toolchain Rust (la parte Rust se valida en CI).
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
echo "== 1/5 == sin marcas de terceros"
bash scripts/check-no-branding.sh
echo "== 2/5 == JSON y Python validos"
python3 - <<'PY'
import json, pathlib, py_compile, sys
roots = [pathlib.Path("config"), pathlib.Path("api"), pathlib.Path("app/src-tauri"), pathlib.Path("tests/lab/fixtures")]
files = [p for r in roots for p in r.rglob("*.json")]
for p in files:
    json.loads(p.read_text())
print(f"  {len(files)} JSON validos")
for p in pathlib.Path("tests/lab").glob("*.py"):
    py_compile.compile(str(p), doraise=True)
print("  Python del laboratorio compila")
PY
echo "== 3/5 == vectores analiticos del scoring"
python3 tests/lab/score_oracle.py
echo "== 4/5 == laboratorio UDP reproducible"
python3 tests/lab/run_lab.py
echo "== 5/5 == Rust (si hay toolchain; si no, lo ejecuta CI)"
if command -v cargo >/dev/null 2>&1; then
  cargo clippy --workspace --all-targets
  cargo test --workspace
else
  echo "  cargo no disponible en este entorno: fmt/clippy/test se validan en .github/workflows/ci.yml"
fi
echo "OK: bateria completa"
