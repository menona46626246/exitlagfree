# Política de seguridad

## Reporte de vulnerabilidades
Abrir un *private vulnerability report* en GitHub (pestaña Security → Report a vulnerability)
o contactar al mantenedor por el canal privado indicado en el README. No abrir issues
públicos para vulnerabilidades sin divulgar. Objetivo de respuesta inicial: 7 días.

## Superficie y controles
- `core`, `ipc` y `config` prohíben `unsafe`; el helper privilegiado solo acepta un
  enum cerrado de comandos por IPC autenticado (UDS con `SO_PEERCRED` / Named Pipe con SID).
- Las claves WireGuard se generan localmente y nunca se envían a la API de nodos.
- Actualizaciones verificadas con Ed25519/minisign antes de instalarse; releases con
  `SHA256SUMS` firmados con Sigstore/cosign (OIDC keyless) y provenance SLSA.
- Sin ejecución remota de código: la API no acepta configuración, scripts ni binarios.

## Alcance
No se consideran vulnerabilidades del proyecto los bloqueos de firewall/antivirus a TUN,
las políticas anti-VPN de juegos de terceros ni la imposibilidad física de reducir la
latencia por debajo del mínimo de propagación.

Ver `docs/seguridad.md` para el threat model completo.
