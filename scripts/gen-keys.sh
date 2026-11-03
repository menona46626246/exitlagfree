#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Genera un par WireGuard LOCAL. La clave privada nunca se envia a ningun nodo.
set -euo pipefail
OUT="${1:-./tramo-key}"
if ! command -v wg >/dev/null 2>&1; then
  echo "ERROR: falta 'wg' (wireguard-tools). Instalalo y reintenta." >&2
  exit 1
fi
umask 077
priv="$(wg genkey)"
printf '%s\n' "$priv" > "$OUT"
printf '%s\n' "$priv" | wg pubkey > "${OUT}.pub"
chmod 600 "$OUT"
echo "Clave privada: $OUT (0600) — NO la commitees ni la envies al relay"
echo "Clave publica: ${OUT}.pub — esta si se comparte con el operador del nodo"
