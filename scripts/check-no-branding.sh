#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Falla si algun artefacto nuevo reproduce la marca de terceros que dio nombre
# originalmente al repositorio. El patron se construye por concatenacion para
# que este propio script no contenga la cadena prohibida de forma contigua.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
MARCA="ex""it""la""g"
if grep -rIniE "$MARCA" . \
    --exclude-dir=.git \
    --exclude-dir=node_modules \
    --exclude-dir=target \
    --exclude-dir=__pycache__ \
    --exclude='*.pyc' \
    --exclude='report.json' ; then
  echo "ERROR: se encontro una marca de terceros en los artefactos." >&2
  echo "Renombra el repositorio y elimina cualquier mencion en codigo/docs." >&2
  exit 1
fi
echo "OK: ningun artefacto reproduce la marca de terceros"
