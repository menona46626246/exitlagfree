# Solución de problemas

## La app arranca pero el Dashboard dice "Servicio elevado NO disponible"

- Es normal si no instalaste el servicio: el **diagnóstico funciona igual**; solo los
  túneles/rutas/kill switch necesitan el servicio.
- Instálalo: `installer\install-service.ps1` **como administrador** (requiere .NET 8
  runtime). Verifica con `gameroute service-status` o el botón *Comprobar servicio*.

## "WireGuard NO detectado"

- Instala WireGuard for Windows desde el sitio oficial (incluye Wintun). El servicio usa
  `wireguard.exe` de `C:\Program Files\WireGuard\` y `wg.exe` del PATH.
- No se implementan drivers propios por diseño.

## El túnel no conecta

1. Valida el .conf: `gameroute tunnel-check --conf archivo.conf` (o importa por UI: la
   validación es previa).
2. Comprueba claves: la PrivateKey debe ser la del **cliente** y PublicKey la del
   **servidor relay**.
3. Firewall local o del VPS: el puerto UDP (51820) debe estar abierto **en el relay**.
4. Mira los logs: `%APPDATA%\GameRouteOptimizer\logs\` y el Visor de eventos de Windows
   (servicio) — más abajo cómo leerlos.
5. Prueba MTU 1280 si tu red hace tunneling doble (PPPoE, CGNAT con encapsulación).

## Páginas no cargan / carga lenta con el túnel activo

- Clásico problema de MTU: baja `TunnelMtu` a 1280-1360 en *Configuración* y reintenta.
- Si usas *túnel global* con DNS del relay, prueba DNS 1.1.1.1/9.9.9.9 o "solo juego".

## Métricas ICMP con 100 % de pérdida pero el juego funciona

- El servidor bloquea ICMP. Configura el servidor del perfil con `preferredProtocol: Tcp`
  y un puerto del juego; la app medirá con TCP connect y lo indicará en las notas.

## "Pérdida extrema" excluye mi relay

- Revisa el trayecto: mide el relay con `gameroute relay test <id>`.
- Recuerda: los endpoints WireGuard responden por UDP; que no respondan a ICMP/TCP no
  significa caída. Si tu relay responde a ping, ajústalo como servidor del perfil.

## Cambios de ruta constantes (flapping)

- Sube `SwitchCooldownSeconds` (p. ej. 600), `MinImprovementLatencyMs` (p. ej. 15-20) y
  baja `MaxSwitchesPerHour`. Todo en *Configuración*.

## Mi internet quedó bloqueado (kill switch activo y túnel caído)

- Es el comportamiento esperado del kill switch. Desactívalo con cualquiera de estas vías:
  1. App → *Configuración* → *Desactivar kill switch*.
  2. App → botón rojo *Detener y restaurar red* (emergencia).
  3. Manual: `netsh advfirewall firewall delete rule name="GameRouteKS_BlockAll"`
     y también las reglas `GameRouteKS_Allow_*`.

## Restaurar red manualmente (emergencia sin app)

```bat
:: Túnel (WireGuard oficial)
"C:\Program Files\WireGuard\wireguard.exe" /uninstalltunnelservice gameroute-0

:: Kill switch
netsh advfirewall firewall delete rule name="GameRouteKS_BlockAll"
netsh advfirewall firewall delete rule name="GameRouteKS_Allow_WireGuard"
netsh advfirewall firewall delete rule name="GameRouteKS_Allow_Service"

:: Rutas añadidas por destino (ejemplo): eliminar la /32 del servidor
route delete 203.0.113.10

:: DNS
ipconfig /flushdns
```

El servicio además **restaura automáticamente** el snapshot de rutas al arrancar si se
cayó sin limpiar, y detener el servicio (Ctrl+C / sc stop) restaura la red.

## Dónde mirar qué pasó

- `%APPDATA%\GameRouteOptimizer\logs\gameroute-YYYYMMDD.log` — rotación diaria.
- Pestaña *Logs* (filtro por nivel, exportable).
- Pestaña *Sesión* → eventos con timestamps (caídas de túnel, cambios de ruta).
- Datos del servicio (si usas modo sistema): `C:\ProgramData\GameRouteOptimizer\logs\`.
