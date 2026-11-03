# Configuración en Windows

- **Permisos:** la UI corre `asInvoker`. El helper se instala como servicio con UAC
  puntual para crear el adaptador Wintun/WireGuardNT. No se pide elevación en cada
  arranque. ICMP usa `IcmpSendEcho` (sin elevación).
- **Drivers:** Wintun/WireGuardNT son binarios firmados del sistema, usados por su
  ABI pública y distribuidos con su aviso GPLv2. Tramo **no instala drivers propios**.
- **Firewall:** reglas WFP etiquetadas y numeradas; nunca `netsh` global. Se permiten
  explícitamente el endpoint UDP de WireGuard, loopback, DHCP, DNS y la LAN configurable.
- **Antivirus:** si bloquea el TUN, el preflight muestra el error y se degrada a
  «solo medir». Tramo nunca desactiva el antivirus ni pide exclusiones sin consentimiento.
- **Autoinicio:** `tauri-plugin-autostart` por usuario (carpeta Startup), sin admin.
- **WebView2:** requerido para la UI; el instalador incluye bootstrapper.
- **Limitación:** split tunneling por proceso requiere un callout WFP firmado y está
  fuera del MVP. Se usan reglas por CIDR/puerto/protocolo. Ver ADR-0003.
- **VPN corporativa:** se detectan adaptadores previos y el modo auto queda bloqueado
  hasta confirmación explícita; no se modifican sus reglas.
