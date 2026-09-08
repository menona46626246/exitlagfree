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
    programa espera y verifica antes de dar el túnel por activo.
11. **UDP puede estar bloqueado** en redes de hotel/empresa/campus; WireGuard no funcionará ahí (aunque el
    diagnóstico por TCP sí sirva).
12. **Kill switch**: si está activo y el túnel cae, quedas sin internet hasta la restauración automática o
    manual. Está desactivado por defecto.

## De la detección y los juegos
13. **Detección de juegos** busca solo en rutas comunes (Program Files, Steam, Epic…) por nombre de
    ejecutable; nunca escanea el disco entero. Si no encuentra el juego, escribe la ruta a mano.
14. **El proceso del juego puede no llamarse igual** que el ejecutable (launchers): añade el nombre real
    del proceso en el perfil.
15. **Algunos juegos/plataformas prohíben VPN/proxies en sus términos.** Es tu responsabilidad revisarlos;
    el modo túnel está pensado para servidores y relays que tú controlas o tienes autorizados.

## Técnicas (v1)
16. Traceroute puede no completarse si los routers intermedios no responden ICMP (saltos `*`).
17. El routing «solo destinos del juego» trabaja con **IPv4** en v1; los destinos IPv6 siguen la ruta
    normal del sistema.
18. Sin SDK local de .NET no se puede compilar en esta máquina de desarrollo; se usa CI (ver STATE.md).
19. Tests automatizados: la suite xUnit cubre hoy pruebas básicas de humo; la validación fina de túnel/
    rutas/kill switch exige una máquina Windows real con WireGuard y relay propio (guía en docs/).

## Éticas y legales (resumen)
- **Prohibido**: evadir regiones/bans, ocultar tu identidad para infringir términos, usar relays de
  terceros sin permiso, o cualquier uso que viole la ley.
- La herramienta es para **mejorar la ruta legítima hacia servidores que juegas legalmente**. Detalle en
  `docs/advertencias-legales-tos.md`.
