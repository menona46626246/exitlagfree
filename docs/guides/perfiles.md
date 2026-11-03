# Perfiles y reglas

Cada perfil (`config::GameProfile`) define `destination` (`host:puerto`), reglas,
prioridad de nodos y `requires_ack`. Esquema completo: `config/schemas/config.v1.json`.

Tipos de regla:

| `kind` | `value` | Soporte |
|---|---|---|
| `cidr` | `203.0.113.0/24` | Windows y Linux |
| `port` | `27015` | Windows y Linux |
| `protocol` | `udp` / `tcp` / `both` | Windows y Linux |
| `executable` | `game.exe` (solo nombre, sin rutas ni argumentos) | Linux vía netns; **no** en Windows |

Validación al importar: IDs `[A-Za-z0-9_-]`, puertos 1..65535, CIDR IPv4 bien
formado, pesos en [0,10], umbrales en rangos seguros. Se rechazan campos
`hook`, `script`, `command`, `on_up`, `on_down` y `exec`: **la configuración nunca es
un mecanismo de ejecución de código**.

```bash
tramoctl config validate --file mi-config.json
tramoctl config export  --file mi-config.json          # sin claves privadas
tramoctl config export  --file mi-config.json --include-secrets  # con advertencia
```

`requires_ack: true` obliga a confirmar que el usuario conoce las políticas anti-VPN
del juego. El producto no oculta el túnel ni intercepta el proceso del juego.
