# Guía — Túnel WireGuard

El modo túnel encamina tráfico por tu relay usando **WireGuard oficial de Windows**. Toda activación es
explícita y reversible; el programa pide elevación (UAC) en el momento de aplicarla.

## Antes de empezar
1. Instala [WireGuard](https://www.wireguard.com/install/) (la app lo localiza en las rutas estándar).
2. Configura un relay (docs/guia-relays.md) con clave privada cargada y medible.
3. Añade un juego con su servidor objetivo (docs/guia-juegos.md) y elige en el perfil el modo de túnel.

## Activar
1. En **Panel principal** selecciona el juego y pulsa **Optimizar**.
   - Si el perfil es *ruta directa*: solo mide y monitoriza (sin túnel).
   - Si es *túnel global* o *solo destinos del juego*: la app mide la ruta directa, mide los relays
     candidatos y compara (scoring). Si gana un relay:
     - auto-switch **activo** → conecta solo tras verificar mejora estable con histéresis;
     - auto-switch **inactivo** → te muestra la recomendación y pide confirmación.
2. Acepta el aviso de UAC si se activa el túnel. **Una sola elevación** cubre toda la activación: se
   instala la interfaz, se espera a que esté operativa y se aplica el DNS del túnel en la misma operación.
3. El panel muestra el túnel activo, su modo, y las métricas en vivo directa vs túnel.

## Modos
| Modo | Qué encamina | Cuándo |
|---|---|---|
| Directo | nada (solo mide) | diagnóstico y monitorización |
| Solo destinos del juego | solo las IPs de los servidores del perfil (+ el DNS del túnel si el relay lo cubre) | uso diario recomendado |
| Global | todo el tráfico del equipo | casos concretos (el juego usa muchos hosts/CDNs) |

## DNS del túnel
- El **DNS configurado** se aplica a la interfaz del túnel solo cuando son **IP IPv4 literales**
  (p. ej. `1.1.1.1`); es lo que acepta Windows (`netsh`).
- En modo *solo destinos del juego*, los servidores DNS también se **encaminan por el túnel** si el
  `AllowedIPs` del relay los cubre (protección básica contra fugas DNS). Si no los cubre, la app avisa:
  las consultas DNS podrían salir por la ruta directa; usa un DNS alcanzable por el relay o activa el
  kill switch.
- Si el DNS configurado contiene nombres o IPv6 (no aplicables en v1), se mantiene en el `.conf` del
  túnel pero **no** se aplica a la interfaz, y la app lo advierte en los registros.

## Detener y restaurar
- **Detener** (botón en Panel principal): desconecta el túnel; la desinstalación del servicio WireGuard
  elimina la interfaz, sus rutas y su DNS. Si la desinstalación falla, se intenta restaurar el DNS de la
  interfaz como operación aparte y se te avisa. Las rutas originales del sistema nunca se tocan.
- **🛑 Detener y restaurar red** (barra inferior, siempre visible): restauración de emergencia aunque algo
  falle; desactiva también el kill switch. Úsalo si te quedas sin conexión.
- **Recuperación al arrancar**: antes de cada sesión la app comprueba si la anterior dejó un túnel o el
  kill switch activos (estado persistido en la base de datos local) y los restaura. Si la app se cerró de
  golpe con el túnel activo, el siguiente arranque lo limpia (puede pedir una elevación).
- Auto-stop: si el perfil tiene *auto-stop al cerrar*, al cerrar el juego se restaura todo.

## Kill switch (opcional)
Actívalo en **Configuración → Túnel** (requiere administrador). Mientras el túnel esté activo:

- **Bloquea toda la salida de internet** salvo tres excepciones explícitas: la interfaz del túnel, el
  **endpoint UDP del relay** (necesario para el handshake de WireGuard, que sale por tu red física) y tu
  **red local**.
- El estado previo del firewall se guarda antes de activarlo y se **restaura siempre** al desactivar
  (parada normal, error, botón de emergencia o arranque tras un cierre inesperado).
- **Importante**: si el túnel cae y la restauración automática no llega a tiempo, te quedas sin internet
  hasta el failback automático (segundos) o el botón de emergencia. Por defecto está desactivado.

## Fallos frecuentes
- **«No se encontró WireGuard»**: instálalo o di a la app dónde está (`wireguard.exe`).
- **El túnel no levanta**: revisa endpoint (IP/puerto UDP), claves pública/privada y que el servidor
  permita UDP; mira **Registros**.
- **El túnel aparece activo pero no hay internet**: la app vigila el **handshake** de WireGuard: si deja
  de renovarse, el túnel se marca como caído y se activa el failback a la ruta directa.
- **Internet no funciona con túnel global**: MTU muy alta (bájala a 1280), DNS del relay caído, o el
  proveedor del relay bloquea reenvío (IP forwarding / NAT). Detén y revisa la config del servidor.
- Más casos en docs/solucion-problemas.md.
