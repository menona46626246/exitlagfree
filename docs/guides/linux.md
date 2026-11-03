# Configuración en Linux

- **Kernel ≥ 5.6** con módulo `wireguard` (o `wireguard-go`/`boringtun` userspace).
- **Permisos:** `CAP_NET_ADMIN` + `CAP_NET_RAW` para el helper, vía systemd
  (`AmbientCapabilities=`, `NoNewPrivileges=yes`, `ProtectSystem=strict`,
  `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK`) o `setcap`. La
  instalación usa **polkit** `auth_admin` una sola vez. La UI nunca corre como root.
- **Sondas directas:** `SO_MARK` + `ip rule fwmark <M> lookup main` permiten medir la
  ruta directa aunque el túnel esté activo; el plan usa `suppress_prefixlength 0`
  para no secuestrar la default route salvo petición explícita.
- **Firewall:** tabla `nft inet tramo` propia con etiqueta y rollback; no se tocan
  las reglas del usuario ni de otra VPN.
- **Split tunneling por proceso (opt-in):** lanzar el juego en un *network namespace*
  con el túnel como ruta por defecto. Requiere `CAP_NET_ADMIN`, no intercepta paquetes.
- **DNS:** no se cambia el resolver salvo petición explícita; se admite IP literal y
  DoH opt-in. `rp_filter` se comprueba en el preflight.
- **Distribuciones:** AppImage o `.deb` (Fase 8). WebKitGTK es necesario para la UI.
