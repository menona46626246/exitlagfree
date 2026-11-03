# Cómo montar un nodo relay de prueba

## Requisitos

- Host Linux con **IP pública** (o port-forward UDP 51820). Detrás de CG-NAT el nodo
  no es alcanzable: es una limitación física, no un bug.
- Kernel con módulo `wireguard`, Docker + Compose, o Ansible y `wireguard-tools`.
- Un par de claves generado localmente. **Nunca commitees claves privadas.**

## Docker Compose

```bash
bash scripts/gen-keys.sh                       # genera claves con 0600
cp node/wireguard/wg0.conf.tpl node/wireguard/wg0.conf
# editar wg0.conf con las claves generadas (archivo ignorado por Git)
cp node/.env.example .env
docker compose -f node/compose/compose.yaml up --build
curl -s http://127.0.0.1:8081/v1/health | jq
```

`compose.yaml` levanta dos servicios: `relay` (WireGuard, `NET_ADMIN`,
`ip_forward=1`, configuración montada **solo lectura**) y `agent` (health en
`:8081`, `read_only`, `no-new-privileges`, sin PII). La API de catálogo se ejecuta
aparte: `cargo run -p tramo-api -- --nodes config/examples/nodes.json`.

## Ansible

```bash
ansible-playbook -i mi-inventory.ini node/ansible/playbook.yml \
  -e @vault.yml   # tramo_relay_private_key y tramo_client_public_key
```

El playbook instala `wireguard-tools`, habilita `ip_forward`, plantilla `wg0.conf`
con modo `0600` y `no_log: true`, habilita `wg-quick@wg0` y recuerda revisar el
firewall del host. Las claves se gestionan con **ansible-vault**, nunca en el repo.

## Exposición segura

- Exponer solo UDP/51820 y el health detrás de un proxy inverso con TLS.
- `TRAMO_API_KEY` opcional protege `/v1/nodes` (cabecera `X-Tramo-Key`).
- Rate limit: 60 peticiones/minuto/IP. La API es de **solo lectura**: no acepta
  POST, cuerpos, configuración, scripts ni binarios → sin ejecución remota de código.
- TLS no está en el proceso Rust: termínalo con Caddy/Traefik.
- Rotación de claves: añadir peer nuevo → verificar `wg show` → retirar el viejo.
