# SECURITY.md — Modelo de seguridad y privacidad

## Filosofía

- **Mínima elevación**: la UI corre `asInvoker`; solo el Servicio elevado toca rutas,
  firewall y túneles. El diagnóstico funciona sin privilegios.
- **Reversibilidad**: cada cambio de red lleva snapshot + restore; hay botón de
  emergencia y desinstalación limpia.
- **Sin superficie de ataque de terceros**: sin backend, sin telemetría, sin cuentas,
  sin APIs externas obligatorias.

## Manejo de secretos

| Secreto | Almacenamiento | Notas |
|---------|----------------|-------|
| Clave privada WireGuard | Cifrada con **DPAPI** (`CryptProtectData`, CurrentUser) en Windows; AES-GCM con keyfile 0600 en Linux | Solo referencia cifrada en `relays.json`; nunca en claro en disco, logs ni exports |
| Clave privada en tránsito | Named pipe local (SO-protected) hacia el Servicio, solo al conectar el túnel | Nunca por red |
| `.conf` temporal del túnel | Directorio con permisos restrictivos (0600 en Linux) | Escrito por el Servicio para las herramientas oficiales |
| Logs | Enmascaran secretos (`***`); `TunnelProfile.ToConfString` nunca incluye la clave | Verificado con tests |

**Garantía probada**: los tests verifican que tras importar un `.conf`, la clave privada
no aparece en **ningún archivo** del directorio de datos, y que los exports JSON la
excluyen salvo consentimiento explícito (y aun así exportan la *referencia* cifrada,
no el secreto).

## Superficie de ataque y mitigaciones

| Riesgo | Mitigación |
|--------|-----------|
| Inyección en procesos de shell (command injection) | `CommandRunner` usa `ArgumentList` (sin shell), timeout y kill |
| Servicio comprometido por pipe | Pipe local con nombre fijo; handlers con validación; sin ejecución dinámica de payload (solo conf validada por `WireGuardConfigParser` antes de aplicar) |
| Config WG maliciosa | Validación dura previa (claves base64 32B, CIDR válidos, endpoint, MTU 1200-1500); rechazo **antes** de tocar el sistema |
| Rutas huérfanas si el servicio muere | Snapshot persistente de rutas con restauración automática al arrancar |
| Kill switch "trampa" | Reglas con nombre conocido `GameRouteKS_*`; desactivación por UI, emergencia, script y comando manual documentado |
| Escaneo/abuso de red | Rate limit (5 probes/s por defecto); solo endpoints definidos por el usuario; sin escaneo masivo por diseño |
| Exfiltración de datos | No hay canal: sin telemetría ni endpoints remotos del propio programa |
| Lectura de memoria del juego / hooking / DPI | No existe en el código; la interacción con juegos se limita a listar procesos por nombre y leer nombres de `.exe` en carpetas autorizadas |

## Privacidad

- Datos locales: `%APPDATA%\GameRouteOptimizer` (o `GAMEROUTE_DATA`).
- Los exports de perfiles/relays/informes no contienen secretos.
- No hay crash reporting externo; los errores solo van al log local.

## Amenazas fuera de alcance (explícito)

- No defendemos contra malware ya presente en la máquina ni contra administradores del
  sistema que quieran leer la memoria de la app.
- El kill switch protege la IP real ante caída del túnel, pero no es anonimato frente a
  un atacante activo global (no es su propósito).

## Reporte de vulnerabilidades

Proyecto de código abierto: abre un issue/PR describiendo el problema y la reprodución.
No incluyas secretos reales en los reportes.
