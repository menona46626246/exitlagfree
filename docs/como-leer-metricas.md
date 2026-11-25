# Cómo leer las métricas

## Latencia (ping)

- **Media** (`MeanMs`): promedio de las probes exitosas. Útil, pero engaña sola.
- **p95 / p99**: el 95 %/99 % de las probes fueron más rápidas que esto. Un p95 muy
  superior a la media = picos (stutters en el juego aunque la media parezca buena).
- **Min/Max**: rango del trayecto. El min suele aproximar el RTT físico real.

**Referencias orientativas**: <30 ms excelente · 30-60 muy bueno · 60-100 jugable ·
100-150 notable · >150 difícil para juegos competitivos. Depende del juego y la región.

## Pérdida de paquetes (Loss)

- Porcentaje de probes sin respuesta dentro de su timeout.
- 0-1 % normal en internet. **2-5 % ya se siente** (teleports, hit registration errática).
- **≥20 % se considera "pérdida extrema"** y la ruta se excluye del scoring por defecto.
- Cuidado: si un servidor **limita o bloquea ICMP**, la "pérdida ICMP" es falsa. La app
  detecta alta pérdida ICMP y muestra el aviso *"ICMP con alta pérdida: puede estar
  limitado por el servidor"*; en ese caso confía más en TCP connect o define el
  protocolo del servidor como `Tcp`.

## Jitter

- Variación entre probes consecutivas (media de diferencias absolutas).
- <5 ms imperceptible · 5-15 leve · 15-30 molesto · >30 grave (a veces peor que la
  latencia absoluta para el gameplay).

## Estabilidad (0-100)

- Score compuesto propio: base 100, resta por pérdida (fuerte), jitter (moderado) y
  tendencia creciente de latencia (pequeña). 90+ = estable, <60 = ruta inestable.

## Score de ruta (0-100) y confianza

- Combina (normalizadas y ponderadas): latencia contra el mejor candidato (50 %),
  pérdida absoluta con 10 % = 0 puntos (25 %), jitter con 30 ms = 0 puntos (15 %) y
  estabilidad (10 %). Penalizaciones: tramo relay→juego desconocido (−10), carga de
  relay sobre 60 % (−0,2/punto), saltos extra (−1/salto), costo declarado.
- **Confianza** (0-100 %): cuánto te creemos la medición — sube con el nº de muestras,
  baja si ICMP no es fiable o si falta el dato relay→juego.
- **EXCLUIDO**: ruta inalcanzable o con pérdida extrema; no puede ser recomendada.

## Qué esperar del "antes/después"

- `Mejora estimada` = media directa − media optimizada (positiva = mejora).
- Si el túnel no mejora nada, la app no lo activa (o hace failback). Un relay solo
  gana cuando existe una **ruta directa mala** y el relay ofrece un **camino mejor**
  (p. ej., peering más directo a la región del servidor).
