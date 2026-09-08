# Guía — Relays WireGuard

Un **relay** es un servidor tuyo (VPS, casa, etc.) con WireGuard que puede mejorar la ruta hacia el
servidor del juego. La app **no** aporta relays: tú los añades y los mides.

## Requisitos
- Servidor con WireGuard instalado y un peer configurado para este equipo (clave privada cliente).
- Apertura del puerto UDP elegido (por defecto 51820) hacia el relay.

## Añadir un relay
Opción A — **Importar .conf**: exporta del relay un archivo `peer.conf` de WireGuard con
`[Interface]` (Address, DNS opcional), `[Peer]` (PublicKey, Endpoint, AllowedIPs) y pégalo/abrélo en
**Importar .conf**. La app rellena los campos y guarda la clave privada cifrada con DPAPI (Windows).

Opción B — **A mano**:
1. **+ Nuevo** y rellena:
   - **Nombre** (p. ej. «VPS México»), **País/Ciudad** (informativo).
   - **Endpoint**: IP o dominio del relay + puerto UDP.
   - **Clave pública** del relay (del lado servidor).
   - **AllowedIPs del peer**: en el servidor define el peer con las IPs que le asignarás al cliente
     (p. ej. `10.66.0.2/32`). En la app, en modo *global* se usan `0.0.0.0/0, ::/0` para encaminar todo;
     en modo *solo destinos del juego* la app genera automáticamente las rutas selectivas hacia las IPs
     de tus servidores (sin encaminar todo tu tráfico).
   - **Clave privada** del cliente (puedes generarla con **Generar**; luego añade su pública al `[Peer]`
     del servidor). Se guarda cifrada con DPAPI tras confirmar.
2. **Guardar relay**.

## Probar un relay
Pulsa **Probar relay**: la app hace una sonda ICMP/TCP al endpoint y guarda latencia/pérdida/jitter.
Esa medición alimenta el scoring (la medición *final* hacia el servidor del juego se hace por el túnel
cuando está activo).

## Estado y salud
- Punto verde: habilitado y con salud reciente. Amarillo: sin medición. Rojo: última medición con fallo.
- **Relay deshabilitado**: no entra en recomendaciones ni en diagnóstico.

## Exportar / importar JSON
- **Exportar JSON (sin claves)**: portátil entre equipos; **nunca** incluye la clave privada.
- Al importar en otro equipo deberás volver a cargar la clave privada de ese relay.

> Reglas: relays de terceros solo con permiso expreso del propietario; revisa las condiciones del juego
> antes de usarlos (docs/advertencias-legales-tos.md).
