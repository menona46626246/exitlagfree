# Limitaciones — GameRoute Optimizer

Versión: 0.9.0. Léelo completo antes de confiar en las métricas o en el modo túnel.

## Físicas y de medición
1. **Un túnel no teletransporta.** La latencia directa hacia un servidor lejano incluye la distancia
   física (≈1 ms por cada 100 km de fibra, más saltos). Si tu relay está más lejos que tu servidor, el
   túnel no puede ganar.
2. **La ruta de ida y vuelta puede diferir** (asimetría de BGP/ISP). Medimos el round-trip efectivo que ve
   la aplicación, que es lo que importa para jugar.
3. **ICMP no siempre refleja el tráfico de juego.** Muchos servidores priorizan o bloquean ICMP. Cuando el
   programa detecta ICMP no fiable usa TCP connect y **etiqueta** la métrica; ninguna métrica etiquetada
   como no fiable se usa para decidir un cambio de ruta sin avisarte.
4. **Muestras cortas ≠ certeza.** El modo rápido usa pocas muestras; el modo profundo, más. El jitter y la
   pérdida estimada tienen varianza; por eso existe histéresis (mejora mínima y ventana de estabilidad)
   antes de recomendar o cambiar nada.
5. **WiFi y red local contaminan las métricas.** Interferencia, otros dispositivos y el propio adaptador
   afectan al ping/jitter. Si sospechas, repite la prueba con cable.
6. **Hora del día y congestión:** las mediciones reflejan el momento; un resultado bueno/malo no es
   permanente.

## Del túnel WireGuard
7. **Requiere WireGuard oficial instalado** (https://www.wireguard.com/install/) y un relay **tuyo** con
   WireGuard en el servidor. La app no aporta ni vende relays.
8. **Modo global** encamina *todo* el tráfico del equipo por el relay (no solo el juego): consumo del plan
   de datos del VPS, posible bloqueo de servicios que detectan VPN, y mayor latencia para servicios
   locales. Úsalo solo si el proveedor del relay lo permite.
9. **MTU**: valores muy altos fragmentan; el valor por defecto (1420) es seguro para la mayoría de
   túneles sobre UDP. Si un servicio no carga con túnel activo, baja la MTU a 1280.
10. **El primer byte tras activar el túnel puede perder paquetes** (handshake WireGuard ~1-2 s). El
    programa espera a que la interfaz esté operativa antes de dar el túnel por activo.
11. **UDP puede estar bloqueado** en redes de hotel/empresa/campus; WireGuard no funcionará ahí (aunque el
    diagnóstico por TCP sí sirva).
12. **Kill switch** (bloqueo de salida salvo túnel, endpoint del relay y red local): si está activo y el
    túnel cae, puedes quedarte sin internet hasta el failback automático o el botón de emergencia
    («Detener y restaurar red»). La app restaura el firewall al detener, ante errores y al arrancar si
    quedó activo de una sesión anterior; aun así, úsalo con criterio. Está desactivado por defecto.
13. **Detección de caída del túnel por handshake**: la app consulta `wg show` (sin elevar cuando el
    sistema lo permite) y vigila que el handshake se renueve. Un túnel sin handshake reciente
    (~3 minutos) se considera caído y dispara el failback, aunque la interfaz siga existiendo. Durante
    los primeros segundos tras activar (primer handshake) no se declara una caída.
14. **DNS del túnel, v1**: solo se aplican a la interfaz servidores **IPv4 literales**. Los nombres o
    IPv6 se dejan en el `.conf` (el DNS interno los resuelve) pero no se configuran en Windows, y se
    avisa. En modo «solo destinos del juego», el DNS solo se encamina por el túnel si el `AllowedIPs`
    del relay lo cubre; si no, se advierte del posible **fuga DNS** (la consulta saldría por la ruta
    directa). El modo global cubre el DNS con `0.0.0.0/0`.
15. **Clave privada y archivos temporales**: la clave se guarda cifrada (DPAPI) y jamás en texto plano en
    la base de datos ni en exportaciones. Al activar el túnel, el `.conf` (que contiene la clave) se
    escribe unos instantes en la carpeta temporal del usuario para entregársela a `wireguard.exe`
    (que la copia a su almacén cifrado) y se elimina **siempre**, con éxito o fallo.

## De la detección y los juegos
16. **Detección de juegos** busca solo en rutas comunes (Program Files, Steam, Epic…) por nombre de
    ejecutable; nunca escanea el disco entero. Si no encuentra el juego, escribe la ruta a mano.
17. **El proceso del juego puede no llamarse igual** que el ejecutable (launchers): añade el nombre real
    del proceso en el perfil.
18. **Algunos juegos/plataformas prohíben VPN/proxies en sus términos.** Es tu responsabilidad revisarlos;
    el modo túnel está pensado para servidores y relays que tú controlas o tienes autorizados.

## Técnicas (v1)
19. Traceroute puede no completarse si los routers intermedios no responden ICMP (saltos `*`).
20. El routing «solo destinos del juego» trabaja con **IPv4** en v1; los destinos IPv6 siguen la ruta
    normal del sistema (el modo global sí encamina `::/0` si el relay lo permite).
21. La elevación se pide por operación (UAC): activar un túnel usa una sola elevación (secuencia
    instalar → esperar → DNS); detener, otra. Si se rechaza la elevación, el túnel no se activa y se
    muestra un error claro; nada queda a medias: los fallos revierten la instalación parcial y el estado
    persistido permite recuperar en el siguiente arranque.
22. Sin SDK local de .NET no se puede compilar en esta máquina de desarrollo; se usa CI (ver STATE.md).
23. Tests automatizados: la suite xUnit (44 tests) cubre almacenamiento, juegos/relays, claves cifradas,
    parser .conf, métricas/scoring, plan de rutas (incluido DNS), failback, kill switch y gestor de
    túnel con operaciones **simuladas** (secuencia única, rollback, recuperación tras cierre, modo
    global). La validación fina contra el sistema (WireGuard real, UAC, firewall, rutas) exige una
    máquina Windows real con WireGuard y relay propio (guía en docs/).

## Éticas y legales (resumen)
- **Prohibido**: evadir regiones/bans, ocultar tu identidad para infringir términos, usar relays de
  terceros sin permiso, o cualquier uso que viole la ley.
- La herramienta es para **mejorar la ruta legítima hacia servidores que juegas legalmente**. Detalle en
  `docs/advertencias-legales-tos.md`.
