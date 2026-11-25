# Cómo añadir relays WireGuard

Un **relay** es un servidor WireGuard **tuyo** (un VPS tuyo, o servidores que el dueño
te autorice a usar). La app no incluye ni vende relays y no usa servidores de terceros
sin tu autorización explícita.

## Requisitos

- Un servidor con WireGuard instalado y configurado (guía oficial: wireguard.com/quickstart).
- Tu archivo `.conf` local de esa conexión (formato wg-quick).

## Opción A: importar el .conf (recomendado)

1. Pestaña **Relays** → **Importar .conf WireGuard**.
2. La app valida la configuración (claves base64 de 32 bytes, AllowedIPs CIDR, endpoint,
   MTU 1200–1500) y crea el relay con:
   - endpoint host:puerto,
   - clave pública del peer,
   - AllowedIPs,
   - DNS interno (si lo define),
   - y **tu clave privada cifrada con DPAPI** (Windows). Nunca se guarda en claro ni se
     exporta en los JSON.
3. Ajusta campos opcionales con **Editar**: país/ciudad (para orientarte),
   `Relay→juego ms` (estimación que mejorará mucho el scoring: mira tu ping ideal en el
   juego y réstale el ping a tu relay), carga %, prioridad.

## Opción B: alta manual

*Relays* → *Añadir* → nombre, endpoint host/IP, puerto (51820 por defecto), clave pública
del peer, AllowedIPs (un CIDR por línea). También se pueden añadir por JSON:

```bash
gameroute relay import-wg mi-relay.conf     # desde .conf
gameroute relay list                        # listar
gameroute relay test <id>                   # medir salud
```

## Medición de salud

- **Medir salud** hace un probe rápido al endpoint (ICMP si responde, si no TCP).
- Importante: un endpoint WireGuard solo habla UDP/WireGuard; que no responda a ICMP/TCP
  **no significa** que el relay no funcione. La app lo indica siempre.
- El estado (Activo/Degradado/Sin respuesta) se muestra en la tabla.

## AllowedIPs: qué enruta cada relay

- Para **túnel solo juego**: los prefijos de `AllowedIPs` que cubran las IPs de tus
  servidores (p. ej. `203.0.113.10/32`) — o las /32 que la app genera desde el perfil.
- Para **túnel global**: `0.0.0.0/0` (todo el tráfico; se advierte).
- Puedes listar varias subredes de los servidores de tu juego.

## Conectar túnel

El túnel lo aplica el **Servicio elevado** (`installer\install-service.ps1`) usando las
herramientas oficiales de WireGuard (`wireguard.exe /installtunnelservice` en Windows,
`wg-quick` en Linux). Ver [modo-tunel-con-administrador.md](modo-tunel-con-administrador.md).

## Buenas prácticas

- Un relay por región que te interese; ponle nombre claro ("Relay Santiago").
- Completa `Relay→juego ms` si conoces esa latencia: sin ese dato la recomendación tiene
  menor confianza (se penaliza el "desconocido").
- Exporta tus relays a JSON **sin secretos** para respaldar la configuración.
