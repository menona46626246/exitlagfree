#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
case "${1:-compose}" in
  compose)
    docker compose -f node/compose/compose.yaml up --build
    ;;
  down)
    docker compose -f node/compose/compose.yaml down
    ;;
  ansible)
    shift || true
    ansible-playbook -i "${2:-node/ansible/inventory.example.ini}" node/ansible/playbook.yml
    ;;
  *)
    echo "Uso: deploy-node.sh [compose|down|ansible]" >&2
    exit 1
    ;;
esac
