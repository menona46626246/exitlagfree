# API de nodos

Especificación OpenAPI 3.0.3: `api/openapi.yaml`, servida también en
`/openapi.yaml` por `tramo-api`.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/v1/nodes` | Lista de nodos configurados |
| GET | `/v1/nodes/{id}` | Detalle de un nodo |
| GET | `/v1/health` | Estado, carga agregada, versión, número de nodos |
| GET | `/openapi.yaml` | Esta especificación |

- **Solo lectura**: cualquier otro método devuelve 400 y no se lee el cuerpo.
- `X-Tramo-Key` opcional si el operador define `TRAMO_API_KEY`.
- Rate limit: 60 peticiones/minuto/IP → 429.
- Sin PII: health no incluye IPs de clientes ni logs de tráfico.
- TLS: mediante proxy inverso (Caddy/Traefik); el proceso Rust escucha HTTP plano.
- Sin endpoints de escritura ⇒ sin ejecución remota de código.

```bash
cargo run -p tramo-api -- --addr 0.0.0.0:8080 --nodes config/examples/nodes.json
curl -s http://127.0.0.1:8080/v1/health | jq
```
