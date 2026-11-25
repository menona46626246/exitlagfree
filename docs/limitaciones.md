# Limitaciones (léelo completo)

Este programa fue diseñado para ser **honesto sobre lo que puede y no puede hacer**.

## Física y topología

1. **No puede bajar el ping por debajo del límite físico.** La luz por fibra (~200 000 km/s)
   impone un mínimo: si estás a 3 000 km del servidor, el RTT de ida y vuelta no será menor
   que ~30 ms + retrasos de equipos. Ningún software cambia eso.
2. **No arregla tu red local ni tu ISP.** WiFi saturado,router antiguo, congestión de tu
   proveedor o un peering malo del ISP hacia la región del servidor son problemas que un
   relay solo puede paliar si el trayecto *desde el relay* es mejor que el tuyo.
3. **Solo mejora si existe una ruta alternativa mejor.** Si tu ruta directa es óptima,
   la recomendación correcta es "mantén la directa" y el programa lo dirá.

## Medición

4. **No puede medir relay→servidor sin un agente en el relay.** El componente
   usuario→relay se mide de verdad; el tramo relay→juego es una **estimación** (tuya,
   histórica) o queda "desconocido" (se penaliza en el score y baja la confianza).
5. **Muchos servidores de juego bloquean o limitan ICMP.** Las métricas ICMP pueden
   mostrar pérdida falsa. La app lo detecta, avisa y prefiere TCP connect; el RTT de un
   handshake TCP no es idéntico al RTT de los paquetes de juego (suele ser cercano).
6. **UDP opcional solo sirve con endpoints que responden.** Un probe UDP sin respuesta
   no significa nada malo: muchos servidores ignoran paquetes no autenticados (y está
   bien que lo hagan).
7. Las métricas son **estimaciones estadísticas con rate limit bajo** (por diseño: no
   queremos molestar a los servidores). Más muestras = más confianza, no certeza.

## Enrutamiento y túnel

8. **Split tunneling por proceso** tiene soporte limitado del sistema. Donde no es
   posible, la app usa **rutas por destino (IP del servidor)** y te lo dice; o bien el
   túnel global, que mueve **todo** tu tráfico (con advertencia).
9. **MTU**: un túnel añade sobrecarga. La app usa 1380 por defecto y es configurable
   (1200-1500); un MTU mal puesto puede causar problemas de carga de páginas o
   desconexiones (ver solución de problemas).
10. **El kill switch por firewall es grueso**: bloquea por programa (todo excepto
    WireGuard y el servicio), no por proceso de juego individual. Y por definición te
    deja sin internet si el túnel cae.
11. **La restauración de red** cubre lo que la app cambió (túnel, rutas añadidas, reglas
    `GameRouteKS_*`, DNS). Si otros programas u otros cambios del sistema afectan la
    red, la app no puede detectarlo todo.

## Alcance del producto

12. Sin cuentas, sin backend, sin relays incluidos: **tú** aportas los servidores
    WireGuard que vas a usar.
13. No hay detección automática "mágica" de servidores de juego: los defines tú, o los
    importas; no hay escaneo masivo de puertos ni DPI.
14. La app de escritorio es Windows 10/11 (WPF). La CLI funciona en Linux/macOS para
    diagnóstico y validación.
15. No debe usarse para evadir bans, bloqueos geográficos o políticas de servicio
    ([advertencias legales](advertencias-legales.md)).
