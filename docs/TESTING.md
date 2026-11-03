# Guía de testing

```bash
cargo test --workspace                  # unit + integración Rust
python3 tests/lab/score_oracle.py       # 10 vectores analíticos
python3 tests/lab/run_lab.py            # medición UDP real + reporte JSON
bash scripts/check-no-branding.sh       # sin marcas de terceros
python3 -m json.tool config/schemas/config.v1.json >/dev/null
```

Con root (Fase 3):

```bash
sudo bash tests/lab/netem.sh            # namespaces + tc netem
# inyectar caida: tc qdisc change dev veth0 root netem loss 100%
```

CI (`.github/workflows/ci.yml`): matriz Linux/Windows con fmt, clippy, tests y
build de CLI/API/agente; job de laboratorio Python; job de UI con `pnpm build`;
job de seguridad con `cargo audit` y `cargo deny`.

**Estado honesto:** este sandbox no dispone de toolchain Rust, así que la
compilación y los tests de Rust se validan en CI. Sí se han ejecutado y verificado
aquí el oráculo de scoring, el laboratorio UDP, la validación de JSON y el checker
anti-marca. Ver `tests/README.md`.
