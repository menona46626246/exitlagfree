# Seguridad y threat model

## Activos

1. Clave privada WireGuard del cliente.
2. Configuración y perfiles (pueden revelar destinos de juego).
3. Canal IPC con el helper privilegiado.
4. Integridad del binario y de las actualizaciones.
5. Disponibilidad de la red del equipo (no romper rutas).
6. Metadatos de tráfico del usuario.

## Actores y superficie

| Actor | Capacidad | Control |
|---|---|---|
| Proceso local no privilegiado | Intentar hablar con el socket del helper | UDS 0600 + `SO_PEERCRED` / Named Pipe con SID; enum cerrado de 6 comandos; framing ≤64 KiB |
| Atacante en la red | Ver/modificar probes o handshake | WireGuard autentica el túnel; probes no contienen secretos; TLS en la API mediante proxy inverso |
| Operador de nodo malicioso | Mentir sobre carga/estado, observar tráfico | `load` con peso 0,5 y neutralizado si metadata obsoleta; documentado en `limitaciones.md` |
| Servidor de catálogo/actualizaciones comprometido | Servir nodos o binarios falsos | Sin endpoints de escritura; validación de certificados; firma Ed25519/minisign + cosign (Fase 8) |
| JSON de configuración importado | Intentar ejecutar comandos | `deny`-list de campos ejecutables, rangos validados, sin rutas a archivos ni argumentos |

## Controles implementados en este bloque

- `#![forbid(unsafe_code)]` en `core`, `config`, `ipc`, `netctl`, `engine`,
  `tunnel`, `api`, `app/src-tauri`. Excepción documentada y acotada: `probe/src/icmp.rs`
  (API de `socket2` con `MaybeUninit`).
- Claves generadas localmente; `TunnelConfig::public_view()` y `export_config()` sin
  secretos; `assert_private_file` exige modo `0600`.
- `command_from_slice` rechaza claves desconocidas además del enum cerrado.
- API de solo lectura (GET/HEAD), sin lectura de cuerpo, rate limit 60/min/IP,
  `X-Tramo-Key` comparada en tiempo constante, cabeceras `no-store`/`nosniff`.
- Lease con heartbeat, snapshot de rutas, allowlist de firewall, reglas etiquetadas.
- Sin ejecución remota de código: la API no acepta configuración, scripts ni
  binarios; el helper no acepta cadenas libres.

## STRIDE (resumen)

| Amenaza | Mitigación principal |
|---|---|
| Spoofing | Autenticación de par IPC; claves públicas WireGuard; API key opcional |
| Tampering | Firma de releases; validación de config; verificación de ruta efectiva |
| Repudio | `reason` en cada decisión; logs locales opcionales con redacción |
| Information disclosure | Sin telemetría por defecto; logs off; export sin secretos; health sin PII |
| Denial of service | Rate limit; framing acotado; límites de ventana; fail-safe a solo-medir |
| Elevation of privilege | Helper con capacidades mínimas; `NoNewPrivileges`; sin `unsafe` en el plano de control |

## Pendiente (Fase 8)

`SO_PEERCRED`/SID reales, keyring del SO, updater firmado activo, `cargo audit`/
`cargo deny` en CI, auditoría externa y divulgación coordinada (`SECURITY.md`).
