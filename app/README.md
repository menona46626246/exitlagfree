# Capa desktop (Fase 6)

`src-tauri/` contiene ya la **logica de comandos** (`UiState`, `StatusView`,
`ThrottledEvents`) y la configuracion de Tauri 2, pero aun no enlaza el runtime
`tauri` ni el WebView. Eso se hace en la Fase 6, junto con:

- `#[tauri::command]` sobre `UiState::{status, set_mode, toggle, recommend}`.
- Icono de bandeja, notificaciones de cambio de ruta, autoinicio por usuario.
- Updater firmado Ed25519/minisign (hoy `active: false` a proposito).
- Eventos `EngineEvent` emitidos a 4 Hz hacia `app/ui`.

Razon del desacople: la logica es testeable en CI sin WebView ni permisos, y el
frontend permanece como una capa tonta que no puede tocar el sistema.

La UI en `app/ui` es funcional pero usa un **adaptador local de demostracion**
claramente etiquetado hasta que exista el puente IPC; no simula metricas reales.
