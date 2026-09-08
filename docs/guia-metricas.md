# Guía — Métricas y cómo interpretarlas

## Qué mide cada prueba
| Métrica | Definición | Bueno | Malo |
|---|---|---|---|
| **Ping (latencia)** | Tiempo de ida y vuelta de una sonda (ms) | estable y cercano a la base física de tu zona | alto o inestable |
| **Jitter** | Variación entre muestras consecutivas (ms) | < 10-15 ms | > 25-30 ms (afecta a disparos/hit-reg) |
| **Pérdida** | % de sondas sin respuesta | 0 % | > 2-5 % en juego (teleportes/rollbacks) |
| **P95 / P99** | Percentiles de latencia | cercanos a la media | muy por encima de la media = ráfagas |

## Cómo se calcula la recomendación
1. Se mide la **ruta directa** actual.
2. Se miden los relays habilitados/no bloqueados del perfil (endpoint + tramo final por el túnel cuando
   está activo).
3. El **scoring** convierte pérdida y jitter a «ms equivalentes» para comparar en una sola escala.
4. Solo se recomienda un relay si la mejora supera la **histéresis** (mejora mínima configurada) durante
   una ventana de estabilidad; si no, la recomendación honesta es *«quédate en la ruta directa»*.

### Lectura de la confianza
- **Confianza alta** = muestras consistentes y suficientes; **baja** = varianza alta o pocas muestras
  (activa el **modo profundo** en Diagnóstico para confirmar).

## Fiabilidad
- Si el servidor **bloquea ICMP**, la app lo detecta, lo indica y pasa a TCP connect; esas métricas van
  etiquetadas y el scoring las trata con cautela.
- Los números son de **tu conexión actual**: repetir la prueba en otro momento/hora puede cambiar todo.
- En WiFi los números empeoran por causas locales que un túnel **no** corrige.
- Un túnel puede ganar porque evita un tramo congestionado de tu ISP, no porque «comprima» nada.

## Dónde verlo
- **Panel principal**: tarjetas en vivo de directa vs túnel y gráfica de latencia/pérdida.
- **Diagnóstico**: prueba rápida/profunda y traceroute hacia el host que escribas; resultado por relay.
- **Sesión**: resumen de la sesión con eventos y exportación Markdown.
