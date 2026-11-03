# Contribuir

1. Todo cambio entra por PR a `main` desde una rama de trabajo, con CI en verde.
2. `cargo fmt --all`, `cargo clippy --workspace --all-targets -- -D warnings`,
   `cargo test --workspace` y `bash scripts/check-no-branding.sh` antes de abrir el PR.
3. Commits con `Signed-off-by: Nombre <email>` (DCO, `git commit -s`).
4. `core/` es síncrono, puro y sin I/O: ninguna PR puede añadirle sockets, `tokio`,
   reloj de pared ni `unsafe` (`#![forbid(unsafe_code)]`).
5. Ningún cambio puede añadir telemetría por defecto, recolección de datos personales,
   ejecución remota de código, evasión de anti-cheat o nombres/marcas de terceros.
6. Toda limitación nueva se documenta en `docs/limitaciones.md` antes de mergear.
7. Cambios de arquitectura requieren un ADR nuevo en `docs/adr/`.
