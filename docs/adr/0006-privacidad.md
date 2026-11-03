# ADR-0006 — Privacidad por defecto

**Estado:** Aceptado

Sin cuentas, sin tracking, sin telemetría, sin logs por defecto, sin CDN externos.
Cualquier envío opt-in requiere `consent_given_at` y `preview_before_send = true`
(validado en `config::validate_config`). La configuración exportada elimina
`private_key`. El health del nodo publica solo etiqueta estática, carga, versión y
número de peers: nunca IPs de clientes ni logs de tráfico. El modo offline funciona
con `nodes.json` local y sin comprobación de actualizaciones.
