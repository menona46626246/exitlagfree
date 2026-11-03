# Build reproducible (Windows y Linux)

## Requisitos comunes

| Herramienta | Versión | Nota |
|---|---|---|
| Rust | stable (MSRV 1.82) | `curl https://sh.rustup.rs -sSf \| sh` · `rustup component add rustfmt clippy` |
| Node | 22 | solo para la UI |
| pnpm | 9+ | `corepack enable` |
| Python | 3.11+ | solo laboratorio, sin dependencias externas |
| Git | cualquiera | DCO: `git commit -s` |

El `Cargo.lock` se genera con `cargo generate-lockfile` en el primer build y debe
commitearse; los jobs de release usan `--locked`. No se usan fuentes CDN ni
assets remotos en la UI.

```bash
cd "$(git rev-parse --show-toplevel)"
cargo generate-lockfile
cargo fmt --all
cargo clippy --workspace --all-targets
cargo test --workspace
bash scripts/test.sh
```

## Linux (Debian/Ubuntu/Fedora)

```bash
sudo apt install -y build-essential pkg-config libwebkit2gtk-4.1-dev libayatana-appindicator3-dev librsvg2-dev
# Fedora: sudo dnf install webkit2gtk4.1-devel libayatana-appindicator-gtk3-devel
cargo build --release -p tramoctl -p tramo-api -p tramo-node-agent
pnpm --dir app/ui install --frozen-lockfile
pnpm --dir app/ui build
bash scripts/build-linux.sh          # genera .deb / AppImage (Fase 8)
```

El cliente **no necesita root para medir**. Para el túnel (Fase 3): módulo
`wireguard` del kernel y `CAP_NET_ADMIN` en el helper (ver `permisos.md`).

## Windows 10/11 x64

```powershell
# MSVC Build Tools + WebView2 (evergreen; el bootstrapper se incluye en el instalador)
rustup target add x86_64-pc-windows-msvc
cargo build --release -p tramoctl -p tramo-api -p tramo-node-agent
pnpm --dir app/ui install --frozen-lockfile
pnpm --dir app/ui build
.\scripts\build-windows.ps1          # MSI/NSIS (Fase 8)
```

Alternativa sin MSVC: `rustup target add x86_64-pc-windows-gnu` + mingw-w64, o
`cargo xwin build --release` (configurado en `Cross.toml`). El helper usa UAC solo
al instalar el servicio / activar el túnel por primera vez.

## Cross-compilación y CI

`.github/workflows/ci.yml` ejecuta la matriz `ubuntu-latest` + `windows-latest`:
formato, clippy, `cargo test --workspace`, build de CLI/API/agente, laboratorio
Python, validación de JSON y comprobación anti-marca. `release.yml` añade
`SHA256SUMS` firmados con cosign (OIDC keyless) y provenance SLSA.

## Verificación de artefactos

```bash
bash scripts/verify-release.sh SHA256SUMS tramoctl-x86_64-linux.tar.gz
```

> Estado honesto: este bloque se entrega sin toolchain Rust en el sandbox de
> desarrollo, por lo que la compilación se valida en CI. Los jobs de `fmt --check`
> y clippy estricto son informativos hasta ejecutar un `cargo fmt` inicial.
