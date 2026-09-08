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
2. Acepta el aviso de UAC si se activa el túnel (la app se eleva solo para la operación de red).
3. El panel muestra el túnel activo, su modo, y las métricas en vivo directa vs túnel.

## Modos
| Modo | Qué encamina | Cuándo |
|---|---|---|
| Directo | nada (solo mide) | diagnóstico y monitorización |
| Solo destinos del juego | solo las IPs de los servidores del perfil | uso diario recomendado |
| Global | todo el tráfico del equipo | casos concretos (el juego usa muchos hosts/CDNs) |

## Detener y restaurar
- **Detener** (botón en Panel principal): desconecta el túnel, borra **solo** las rutas añadidas y
  restaura DNS. Las rutas originales del sistema nunca se tocan.
- **🛑 Detener y restaurar red** (barra inferior, siempre visible): restauración de emergencia aunque algo
  falle; desactiva también el kill switch. Úsalo si te quedas sin conexión.
- Auto-stop: si el perfil tiene *auto-stop al cerrar*, al cerrar el juego se restaura todo.

## Kill switch (opcional)
Actívalo en **Configuración → Túnel** (requiere administrador). Mientras el túnel esté activo, bloquea el
tráfico que no pase por él. **Importante**: si el túnel cae, te quedas sin internet hasta la restauración
automática (o el botón de emergencia). Por defecto está desactivado.

## Fallos frecuentes
- **«No se encontró WireGuard»**: instálalo o di a la app dónde está (`wireguard.exe`).
- **El túnel no levanta**: revisa endpoint (IP/puerto UDP), claves pública/privada y que el servidor
  permita UDP; mira **Registros**.
- **Internet no funciona con túnel global**: MTU muy alta (bájala a 1280), DNS del relay caído, o el
  proveedor del relay bloquea reenvío (IP forwarding / NAT). Detén y revisa la config del servidor.
- Más casos en docs/solucion-problemas.md.
