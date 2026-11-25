# Modo diagnóstico (sin túnel, sin administrador)

El diagnóstico **no requiere** el Servicio, ni WireGuard, ni privilegios elevados. Es la
manera correcta de empezar y la base de toda decisión de optimización.

## Qué incluye

- **Probes** hacia el servidor que definas: ICMP → TCP connect → UDP → HTTP/S con
  fallback automático, rate limit respetuoso (5 probes/s por defecto) y timeouts
  configurables.
- **Traceroute** por TTL con 2 muestras por salto, detección de cuellos de botella y
  fallback a `tracert`/`traceroute` del sistema.
- **Resumen estadístico**: media, p50/p95/p99, min/max, pérdida, jitter, estabilidad
  0-100 y avisos de fiabilidad (ICMP bloqueado, etc.).
- **Comparación directa vs relays** con score, confianza y explicación — **sin conectar
  nada**: es solo medición.
- **Recomendación honesta**: si la directa gana, te lo dice.

## Cómo usarlo

**Desde la UI:** pestaña *Diagnóstico* → escribe dominio/IP → *Medir*, *Traceroute*,
*Comparar directa vs relays*.

**Desde la CLI (multiplataforma):**

```bash
gameroute probe --host 1.1.1.1 --protocol auto --mode deep
gameroute probe --host sa-east.mi-juego.example --protocol tcp --port 7000
gameroute trace --host sa-east.mi-juego.example --max-hops 15
gameroute score --host sa-east.mi-juego.example --mode quick   # compara con tus relays
gameroute tunnel-check --conf mi-relay.conf                    # solo valida, no aplica
```

## Cuándo usar diagnóstico y no más

- Cuando quieras saber **dónde está el problema** (tu red, el ISP, el peering o el
  servidor) antes de tocar nada.
- Para vigilar periódicamente la calidad de tu ruta.
- En entornos donde no puedes usar VPN (trabajo, escuela) — solo medición pasiva de
  latencia a endpoints autorizados.
- Para validar configuraciones WireGuard **sin aplicarlas** (`tunnel-check`).

## Seguridad del modo diagnóstico

- Solo se envían paquetes de medición a los endpoints que **tú** definiste.
- Rate limit bajo por defecto (máx. 5 probes/s, intervalo configurable) para no molestar.
- No se instala nada en el sistema, no se cambian rutas ni DNS, no se toca el firewall.
- Sin elevación: la app corre con tu usuario normal.
