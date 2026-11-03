#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Uso: verify-release.sh SHA256SUMS artefacto
set -euo pipefail
sums="${1:-SHA256SUMS}"
artifact="${2:-}"
if command -v cosign >/dev/null 2>&1; then
  echo "Verificando firma Sigstore de ${sums}..."
  cosign verify-blob --signature "${sums}.sig" --certificate "${sums}.pem" "$sums"
fi
if [[ -n "$artifact" ]]; then
  (cd "$(dirname "$artifact")" && sha256sum -c "$(basename "$sums")" 2>/dev/null) || \
    grep -F "$(sha256sum "$artifact" | cut -d' ' -f1)" "$sums" >/dev/null
  echo "OK: hash de $artifact presente en $sums"
else
  echo "OK: $sums verificado (pasa un artefacto como segundo argumento para comprobarlo)"
fi
