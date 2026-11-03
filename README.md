# Tramo

**Medición y optimización de red auditable para juegos.** Open source, autohospedable,
sin cuentas y sin telemetría por defecto.

Tramo mide latencia, jitter y pérdida hacia un destino configurable, compara la ruta
directa con túneles WireGuard **propios**, puntúa las alternativas con un algoritmo
transparente y —solo si el usuario lo autoriza y las métricas lo justifican— enruta
selectivamente el tráfico de un perfil por la mejor ruta.

> **Aviso legal y ético:** este proyecto es original. No usa nombres, marcas, logos,
> protocolos propietarios ni código de ningún producto de terceros, y no afirma
> compatibilidad oficial con ninguno. **No promete reducción de ping**: un relay
> siempre añade propagación y encapsulado; solo puede ayudar si evita un tramo
> congestionado. No implementa evasión de anti-cheat, modificación de juegos,
> inyección de paquetes ni ejecución remota de código. El uso de VPN puede estar
> sujeto a los términos de servicio de cada juego: es responsabilidad del usuario.
>
> **Pendiente de autorización:** el repositorio conserva actualmente un nombre que
> contiene una marca de terceros. Se propone renombrarlo a `tramo`; hasta entonces
> ningún artefacto nuevo reproduce ese nombre. Ver `docs/00-propuesta-tecnica.md`.

## Estado del desarrollo

| Fase | Estado |
|---|---|
| Fase 0 — investigación y decisiones | ✅ ADRs, verificación de licencias, limitaciones |
| Fase 1 — diseño y repositorio base | ✅ Workspace Rust, licencia, CI, esquemas, OpenAPI |
| Fase 2 — MVP de medición | ✅ `core`, `probe`, `config`, `tramoctl`, laboratorio ejecutable |
| Fase 3 — túnel WireGuard + helper | 🔜 Contratos listos, backends `NotImplemented` (fallan cerrado) |
| Fase 4 — split tunneling / perfiles | 🔜 Esquemas y planes declarativos listos |
| Fase 5 — selección automática | ✅ Decisor con histéresis, cooldown y failover (core) |
| Fase 6 — UI completa | 🔜 Lógica de comandos + UI React/Vite listas; puente IPC pendiente |
| Fase 7 — nodo relay | ✅ API, Compose, Ansible y agente de salud (sin PII) |
| Fase 8 — seguridad, tests, release | 🔜 CI, threat model y documentación; firma pendiente |

**Lo que NO hace todavía:** crear un túnel real ni cambiar rutas del sistema.
Los backends devuelven un error explícito en vez de simular un túnel activo.

## Inicio rápido

Requisitos: Rust stable (`rustup`), Node 22 + pnpm (solo UI), Python 3 (solo laboratorio).
No se necesitan permisos de administrador para **medir**.

```bash
# Workspace y dependencias (el lockfile se genera y se commitea en el primer build)
cargo generate-lockfile

# Tests del motor, oráculo y laboratorio reproducible
bash scripts/test.sh

# Medir contra un destino configurable (usa eco UDP; degrada a TCP si no responde)
cargo run -p tramoctl -- measure --target 127.0.0.1:51820 --count 40

# Scoring determinista sin red
cargo run -p tramoctl -- score --input tests/lab/fixtures/score_input.json

# Diagnóstico DNS / conectividad / permisos
cargo run -p tramoctl -- diagnose --target 198.51.100.10:51820

# Validar / exportar configuración (sin claves privadas)
cargo run -p tramoctl -- config validate --file config/examples/config.json
cargo run -p tramoctl -- config export  --file config/examples/config.json

# API de nodos (solo lectura, HTTP; TLS detrás de proxy inverso)
cargo run -p tramo-api -- --addr 0.0.0.0:8080 --nodes config/examples/nodes.json
curl -s http://127.0.0.1:8080/v1/health
```

Ejemplo de salida real del laboratorio (`python3 tests/lab/run_lab.py`):

```
p50 3.0 ms · p95 5.0 ms · p99 5.0 ms · jitter 1.287 ms · pérdida 1.67 %
score directa 13.239 · score relay simulado 32.739 · recomendado: direct · falsos positivos: 0
```

## Privacidad por defecto

Sin cuentas, sin identificadores de dispositivo, sin telemetría activada, logs locales
desactivados, sin fuentes ni CDN externos en la UI, modo offline con `nodes.json` local.
La telemetría opt-in exige consentimiento explícito y vista previa del payload antes
de cualquier envío. El exportador de configuración **nunca incluye claves privadas**
salvo `--include-secrets` con advertencia. Ver `docs/privacidad.md`.

## Permisos

Medir: ninguno (UDP/TCP; ICMP requiere `CAP_NET_ADMIN`/`CAP_NET_RAW` o `IcmpSendEcho`).
Crear el TUN y aplicar rutas: **UAC + servicio** en Windows, **polkit + `CAP_NET_ADMIN`**
en Linux, siempre en un helper separado con privilegio mínimo, lease con heartbeat y
restauración automática. Ver `docs/guides/permisos.md` y `docs/seguridad.md`.

## Documentación

- `docs/00-propuesta-tecnica.md` — resumen ejecutivo, stacks, arquitectura, roadmap, scoring.
- `docs/arquitectura.md` — módulos y flujo de datos.
- `docs/adr/` — decisiones arquitectónicas numeradas.
- `docs/guides/build.md` — **build reproducible para Windows y Linux**.
- `docs/guides/nodo-relay.md` — **Docker Compose y Ansible** para un relay de prueba.
- `docs/limitaciones.md` — **riesgos y limitaciones** (física de red, anti-cheat, CG-NAT…).
- `docs/seguridad.md` / `docs/privacidad.md` — **threat model y política de privacidad**.
- `docs/FAQ.md`, `docs/metricas.md`, `docs/TESTING.md`, `api/openapi.yaml`.

## Licencia

Apache-2.0 (ver `LICENSE` y `NOTICE`). Se eligió frente a MIT por la concesión
explícita de patentes, la cláusula de marca y la atribución de archivos modificados.
Los drivers Wintun/WireGuardNT (GPLv2) se usan por su ABI pública como binarios del
sistema, sin enlazarlos al código de Tramo. Ver ADR-0002.

Las contribuciones requieren DCO (`git commit -s`) y CI en verde. Ver `CONTRIBUTING.md`.
