# Permisos, UAC/polkit y restauración

| Acción | Windows | Linux |
|---|---|---|
| Medir UDP/TCP | Ninguno | Ninguno |
| Medir ICMP | `IcmpSendEcho` sin elevar | `CAP_NET_RAW` o `ping_group_range` |
| Crear TUN | Servicio + UAC puntual | `CAP_NET_ADMIN` + polkit |
| Aplicar rutas | WFP etiquetado | `ip rule`/`ip route` en tabla propia |
| Kill switch (opt-in) | WFP etiquetado | `nft inet tramo` |
| Autoinicio | Startup por usuario | systemd *user* / `~/.config/autostart` |

**Reducción de privilegios:** tras crear el TUN, su fd pasa al proceso de datos y el
helper conserva solo las capacidades de routing; las sondas con `CAP_NET_RAW` viven
en un proceso hijo sin capacidades de routing. El IPC se autentica con
`SO_PEERCRED` (Linux) o SID del Named Pipe (Windows) y acepta únicamente 6 comandos.

**Cómo no romper la red:** snapshot previo → allowlist obligatoria (loopback, LAN,
DHCP, DNS, endpoint WG) → reglas etiquetadas con ID único → verificación de la tabla
efectiva → lease de 1 s con reversión a los 3 s → `preflight` antes de activar. Si el
motor, la UI o el helper mueren, las rutas propias se retiran solas.

**Fugas:** el kill switch está **desactivado por defecto**. Ante caída del túnel, la
política por defecto es `revert_direct`: el tráfico vuelve a salir por el ISP y la UI
lo advierte. La alternativa `block` es opt-in y bloquea solo el tráfico del perfil.
No se promete anonimato ni se oculta esta limitación.

**Secretos:** DPAPI/Credential Manager (Windows), Secret Service/libsecret (Linux) o
archivo `0600` con advertencia. Las claves privadas nunca salen del equipo ni se
envían a la API de nodos.
