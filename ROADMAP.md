# ROADMAP.md — Mejoras futuras

## Corto plazo (próximas iteraciones)

- [ ] **Split tunneling por proceso en Windows**: integración con `SetDefaultDllDirectories`/
  WFP (Windows Filtering Platform) para aplicaciones específicas, con la misma
  reversibilidad actual.
- [ ] **Agente opcional de relay**: pequeño endpoint HTTP en tu propio relay (opcional,
  desactivado por defecto) que mida relay→servidor con sondas autorizadas y cierre el
  dato "desconocido" (mejora D5).
- [ ] **Gráfica de comparación antes/después** superpuesta en la vista de Sesión.
- [x] **Wizard de primera ejecución** (modo diagnóstico sin VPS, configuración rápida de perfil y prueba en vivo).
- [ ] **Notificaciones toast nativas de Windows** para cambios de ruta y caídas.
- [ ] Pruebas de integración end-to-end del IPC con el Servicio en CI Windows runner.

## Medio plazo

- [ ] **MTR continuo** en segundo plano con heatmap por salto (persistencia SQLite).
- [ ] **Perfilado por hora del día**: estadísticas históricas por franja para recomendar
  rutas según la hora.
- [ ] **Auto-learn de `relayToGameMs`** a partir de sesiones con éxito verificado.
- [ ] **Exportar/informar automáticamente** tras cada sesión (carpeta vigilada).
- [ ] **Multi-idioma**: extracción de cadenas a recursos (es/en) — la UI ya está
  centralizada en español.
- [ ] **WinUI 3 opcional** como frontend alternativo (la lógica ya está en Core).

## Largo plazo

- [ ] Soporte macOS (Network Extension + wg-quick) con la misma Core.
- [ ] Integración con **WireGuard API nativa** (tunnel.dll oficial) en lugar de CLI,
  manteniendo el fallback a herramientas oficiales.
- [ ] Panel de salud de flota de relays (si el usuario gestiona varios servidores
  propios) con métricas agregadas locales.
- [ ] Firma de código Authenticode y MSIX installer firmado.
- [ ] Fuzzing del parser de configuración y del framing IPC.

## No incluido a propósito (siempre)

- Evasión de geo-bloqueos/bans, DPI, lectura de memoria, hooking, drivers propios,
  telemetría, cuentas, pagos, backend cloud, relays de terceros sin autorización.
