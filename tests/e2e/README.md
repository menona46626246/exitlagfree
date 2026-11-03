# Tests E2E (Fase 6)

Se ejecutarán con `tauri-driver` + WebDriver sobre los flujos: dashboard,
toggle (bloqueado en «solo medir»), cambio de modo, selección de nodo
recomendado, import/export de configuración, gráficas, diagnóstico y tema.
Hasta que exista el runtime Tauri no hay especificaciones ejecutables; la lógica
de esos flujos ya está cubierta por `app/src-tauri/src/commands.rs`.
