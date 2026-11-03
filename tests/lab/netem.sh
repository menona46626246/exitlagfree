#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Laboratorio con network namespaces + tc netem. REQUIERE root y kernel Linux.
# Fase 3: medir TTD/TTF inyectando caida del nodo. No se ejecuta en CI sin root.
set -euo pipefail
if [[ "$(id -u)" -ne 0 ]]; then
  echo "Este script requiere root (CAP_NET_ADMIN). No se ejecuta nada." >&2
  exit 1
fi
NS="${NS:-tramo-lab}"
cleanup() { ip netns del "$NS" 2>/dev/null || true; }
trap cleanup EXIT
ip netns add "$NS"
ip link add veth0 type veth peer name veth1
ip link set veth1 netns "$NS"
ip addr add 10.99.0.1/30 dev veth0
ip link set veth0 up
ip netns exec "$NS" ip addr add 10.99.0.2/30 dev veth1
ip netns exec "$NS" ip link set veth1 up
# Ruta base: 20 ms, jitter 3 ms, 1% perdida
tc qdisc add dev veth0 root netem delay 20ms 3ms loss 1%
echo "Laboratorio listo. Para inyectar caida: tc qdisc change dev veth0 root netem loss 100%"
echo "Ejecuta las sondas con: tramoctl measure --target 10.99.0.2:51820 --count 60"
echo "El cleanup se ejecuta al salir (trap EXIT)."
