#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
# El modulo WireGuard del kernel lo proporciona el HOST (cap_add NET_ADMIN).
# No se ejecuta codigo remoto: la configuracion se monta como volumen de solo lectura.
WGCFG="${WGCFG:-/etc/wireguard/wg0.conf}"
if [ ! -r "$WGCFG" ]; then
  echo "ERROR: falta $WGCFG. Copia node/wireguard/wg0.conf.tpl y rellena las claves." >&2
  exit 1
fi
echo "tramo-relay: levantando interfaz con la configuracion montada"
wg-quick up "$WGCFG" || { echo "wg-quick fallo: revisa NET_ADMIN y el modulo wireguard del kernel" >&2; exit 1; }
trap 'wg-quick down "$WGCFG" || true' INT TERM
echo "tramo-relay: activo (UDP 51820)"
while :; do sleep 3600 & wait $!; done
