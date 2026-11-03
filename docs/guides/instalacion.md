# Instalación para usuarios

1. Descarga el instalador firmado de la pestaña Releases (Fase 8) o compila desde
   código con `docs/guides/build.md`.
2. Verifica la firma: `bash scripts/verify-release.sh SHA256SUMS <artefacto>`.
3. Primer arranque: **modo solo medir**, telemetría y logs desactivados, sin nodos
   configurados. No se toca ninguna ruta del sistema.
4. Configura un destino en el perfil (`host:puerto`) y pulsa «Medir».
5. (Opcional) Añade un nodo propio en `config/examples/nodes.json` o apunta a tu
   API autohospedada. Sin nodos, el producto funciona igual en modo offline.
6. Solo cuando quieras cambiar rutas, selecciona «sugerir» o «auto cambiar» y
   activa el toggle; se te pedirán permisos elevados (ver `permisos.md`).

No hay cuenta, no hay registro, no hay wizard de telemetría. La configuración se
guarda localmente y se puede exportar/importar como JSON sin claves privadas.
