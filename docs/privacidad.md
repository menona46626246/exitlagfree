# Privacidad por defecto

## Compromisos

- **Sin cuentas**, sin login, sin identificadores de dispositivo.
- **Sin telemetría por defecto.** Si el usuario la activa: consentimiento explícito
  con marca de tiempo (`consent_given_at`), vista previa obligatoria del payload
  (`preview_before_send`) y solo métricas agregadas/anónimas (contadores y
  percentiles), sin IP, sin destinos de juego, sin nombres de perfil.
- **Logs locales desactivados.** Si se activan: redacción de IP configurable
  (`none` / `truncate` / `hash`) y retención máxima (`max_days`, 7 por defecto).
- **Modo offline:** catálogo `nodes.json` local, sin comprobación de actualizaciones,
  sin DNS salvo el configurado, sin fuentes ni CDN externos en la UI.
- **Exportación/importación** de configuración: el export elimina `private_key`;
  `--include-secrets` solo bajo advertencia explícita.
- **Sin datos personales sin consentimiento:** no se geolocaliza al usuario.
  `geo_cost_ms` es un coste definido por él; `region_label` es una etiqueta estática
  que define el operador del nodo.
- **Health del nodo:** solo `status`, `load`, `active_peers`, `version`,
  `region_label` y número de nodos. Sin IPs de clientes ni logs de tráfico.

## Qué sale del equipo (y cuándo)

| Flujo | Por defecto | Requiere |
|---|---|---|
| Sondas al destino configurado | Solo si el usuario configura un destino | Destino explícito |
| Consulta a la API de nodos | No (catálogo local) | URL configurada por el usuario |
| Health de nodos | No | Nodos configurados |
| Comprobación de actualizaciones | No | Opt-in (Fase 8) |
| Telemetría agregada | No | Consentimiento + vista previa |

## Datos que nunca se recogen

Nombre de usuario, correo, IP pública del cliente, lista de juegos instalados,
contenido de paquetes, identificadores de hardware, ubicación inferida.

## Nota de confianza

Un relay autohospedado **ve el tráfico que lo atraviesa**. Tramo no puede impedir
que un operador de nodo registre metadatos: por eso el producto recomienda nodos
propios o de confianza y lo documenta en `docs/limitaciones.md`.
