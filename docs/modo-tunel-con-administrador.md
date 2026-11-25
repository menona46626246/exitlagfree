# Modo túnel (con administrador / Servicio elevado)

El túnel es la parte que **sí** necesita privilegios: crear una interfaz, enrutamiento y
(posiblemente) reglas de firewall. GameRoute Optimizer lo hace con el **Servicio
elevado** y **herramientas oficiales de WireGuard** — sin drivers propios.

## Arquitectura

```
┌─────────────────────┐  named pipe local   ┌──────────────────────────┐
│  App WPF (usuario)  │  "gameroute-        │  Servicio (LocalSystem)  │
│  diagnóstico propio │   optimizer-svc"    │  wireguard.exe /install… │
│  sin elevación      │ ──────────────────▶ │  netsh route / firewall  │
└─────────────────────┘  JSON + length       │  wg-quick (Linux)        │
                                             └──────────────────────────┘
                                                        │
                                                        ▼
                                              Wintun / interfaz WG
```

## Instalación paso a paso (Windows)

1. Instala **WireGuard for Windows** (instalador oficial; incluye Wintun).
2. Publica e instala el servicio **como administrador**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File installer\install-service.ps1
   ```
   El script publica el binario, registra el servicio `GameRouteOptimizerService`
   (inicio manual, LocalSystem) y lo arranca.
3. Abre la app: en *Configuración* → *Comprobar servicio / WireGuard* debe decir que el
   servicio está disponible y WireGuard detectado.
4. Desde el Dashboard, *Optimizar*. La UI enviará la configuración del túnel por el pipe
   local; el servicio validará de nuevo y ejecutará:
   - `wireguard.exe /installtunnelservice <conf>` (crea la interfaz Wintun), y
   - rutas por destino (`netsh interface ipv4 add route … store=active`) o el túnel
     global vía AllowedIPs `0.0.0.0/0`.
5. **Detener**: botón *Detener* → `/uninstalltunnelservice` + restauración de rutas.
   El botón de emergencia hace lo mismo + kill switch off + flush DNS.

## Qué hace el servicio exactamente

- **Antes de aplicar**: revalida el `.conf` (claves, CIDR, endpoint, MTU). Si es
  inválido, **no toca nada** y devuelve el error.
- **Snapshot de rutas**: guarda el estado original de los prefijos afectados en
  `ProgramData\GameRouteOptimizer\route-snapshot.json`. Si el servicio se cae sin
  limpiar, al arrancar **restaura automáticamente**.
- **Kill switch (opcional)**: reglas `GameRouteKS_BlockAll` (bloqueo outbound) +
  `GameRouteKS_Allow_WireGuard` y `GameRouteKS_Allow_Service`. Completamente reversibles.
- **Failback**: si el túnel cae o empeora, la política de histéresis ordena volver a
  directa (el orchestrator detiene el túnel y restaura).
- **Logs** del servicio en `C:\ProgramData\GameRouteOptimizer\logs\`.

## Linux (CLI/servicio)

El mismo núcleo compila en Linux; el túnel usa `wg-quick up|down`. Para pruebas
manuales de un túnel sin servicio:

```bash
sudo wg-quick up mi-relay.conf      # aplica conf estándar de WireGuard
sudo wg show                        # estado
sudo wg-quick down mi-relay.conf    # restaura
```

## Seguridad del modo túnel

- La clave privada se descifra solo en el momento de escribir el `.conf` temporal del
  túnel (permisos restrictivos) y viaja por el pipe local, no por red.
- Todas las operaciones del servicio usan listas de argumentos (sin shell) para evitar
  inyección.
- `uninstall-service.ps1` detiene el servicio, elimina reglas `GameRouteKS_*` y túneles
  `gameroute-*`, dejando el sistema limpio.
