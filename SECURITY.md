# Seguridad y privacidad — GameRoute Optimizer

## Principios
- **Sin telemetría**: no hay llamadas a casa, analítica, crash-reporting ni cuentas.
- **Sin nube**: la base de datos, logs, configuraciones y sesiones viven en tu equipo
  (`%LOCALAPPDATA%\GameRouteOptimizer`).
- **Mínimo privilegio**: la UI no pide administrador para uso normal; la elevación (UAC) se pide solo al
  activar túnel/kill switch y dura una operación.
- **Autorización explícita**: el programa solo sondea hosts que tú añades (dominio/IP del juego o relay) y
  solo actúa sobre tu red tras confirmación en la UI.

## Claves privadas WireGuard
- Se guardan cifradas con **DPAPI (CurrentUser)** tras la confirmación del usuario.
- Nunca se serializan a JSON (import/export de relays **no incluye** claves privadas).
- Si DPAPI no está disponible (o el usuario no confirma el cifrado), la clave vive solo en memoria y se
  vuelve a pedir al reiniciar.

## Red
- No se inyecta nada en juegos: no lectura de memoria, no hooks, no drivers de terceros.
- El túnel usa **WireGuard oficial** instalado por el usuario y relays **propios** del usuario.
- Las rutas añadidas son reversibles: al detener (normal, error o botón de emergencia) se eliminan las
  rutas añadidas y se restaura DNS; las rutas originales nunca se borran.
- **Kill switch** (opcional): bloquea la salida fuera del túnel; se desactiva siempre al detener. Puede
  aislarte de internet si el túnel cae: está desactivado por defecto y se documenta en la UI.

## Operaciones privilegiadas
- Contrato JSON único `PrivilegedOp → PrivilegedOpResult` (named pipe `gro.privileged.v1` local o
  CLI elevado por UAC).
- El helper valida entradas, acota ejecución a comandos conocidos y devuelve salida/error estructurados.
- Diario de operaciones local para auditoría y restauración.

## Modelado de amenazas (resumen)
| Amenaza | Mitigación |
|---|---|
| Dejar la red rota | Restauración inversa registrada; botón de emergencia; rutas originales intactas |
| Claves privadas robadas en disco | DPAPI CurrentUser + exclusión de exportaciones |
| Fuga de hosts jugados | Todo local; exportación de sesión explícita por el usuario |
| Suplantación del helper | Pipe local con nombre fijo; el ejecutable se valida en disco antes de UAC |
| Dependencias | Mínimas: Serilog, SQLite (Microsoft.Data.Sqlite), ProtectedData. Sin código de terceros en runtime |

## Reportar problemas
Abre un issue en el repositorio (sin adjuntar claves privadas ni logs con datos personales).
