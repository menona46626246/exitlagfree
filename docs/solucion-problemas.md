# Solución de problemas — GameRoute Optimizer

Antes: mira **Registros (logs)** (exportables) y el estado de la barra inferior; casi todo fallo queda
anotado ahí.

## La app no abre / falla al iniciar
1. Verifica los requisitos (Windows 10/11 x64, .NET 8).
2. Borra/renombra `%LOCALAPPDATA%\GameRouteOptimizer` (respalda antes) si hay una base de datos corrupta.
3. Revisa los logs en `%LOCALAPPDATA%\GameRouteOptimizer\logs`.

## «No se pudo medir» en Diagnóstico
- **Host no responde ICMP**: el servidor bloquea ICMP → usa TCP (o marca *Prueba por defecto: TCP* en el juego).
- **Pérdida 100 % con TCP** a un puerto específico: ese puerto puede estar filtrado; prueba 443 o el puerto real del juego.
- **Timeout de DNS**: revisa el dominio o usa la IP.

## El túnel no se activa
1. ¿WireGuard instalado? La app avisa y guarda el error en logs.
2. ¿El relay tiene clave privada cargada y *Endpoint* correcto (IP:pUDP)? Pruébalo en Relays.
3. ¿UAC aceptado? La activación necesita elevación puntual.
4. Revisa en el servidor: `systemctl status wg-quick@wg0`, `wg show`, firewall UDP, IP forwarding/NAT.

## Con túnel activo no hay internet (modo global)
1. Pulsa **🛑 Detener y restaurar red** (restaura siempre).
2. Prueba con **MTU 1280** en Configuración → Túnel.
3. Comprueba el **DNS del relay** (usa `1.1.1.1` o el DNS del proveedor del relay en *DNS interno*).
4. En el servidor: NAT (`iptables -t nat -A POSTROUTING -o eth0 -j MASQUERADE` + `net.ipv4.ip_forward=1`).

## El kill switch me dejó sin internet
Es su función si el túnel cayó: espera el failback automático (segundos) o pulsa el botón de emergencia.
Si quieres menos riesgo, desactívalo en Configuración (por defecto lo está).

## La recomendación dice «quédate en directa» y yo quería túnel
Es el comportamiento honesto: la directa era igual o mejor. Prueba con *modo profundo*, a otra hora, o con
un relay más cercano a tu zona y al servidor.

## La app dice que el juego está «ejecutándose» cuando no lo está
El watchdog compara nombres de proceso: revisa en el perfil la lista de ejecutables (quita launcher).
El watchdog solo vigila procesos con auto-inicio activo.

## Errores de permiso al restaurar rutas
La restauración también se eleva (UAC). Si rechazaste el aviso, acepta cuando reaparezca o pulsa
**Detener y restaurar red** y acepta UAC. Si sigue fallando, reinicia la app: al salir restaura la red.

## ¿Dónde están mis datos?
`%LOCALAPPDATA%\GameRouteOptimizer\`: `gro.db` (juegos, relays, sesiones, config), `logs\`, `exports\`.
Claves privadas: cifradas con DPAPI en esa carpeta (nunca en texto plano ni en exportaciones).

## Reportar un fallo
Incluye: versión (Acerca de), pasos, y el log exportado **sin** datos sensibles. No subas claves ni
configs de relays.
