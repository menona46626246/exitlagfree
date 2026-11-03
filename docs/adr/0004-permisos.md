# ADR-0004 — Helper privilegiado separado con lease

**Estado:** Aceptado

La UI y el motor corren sin privilegios. Un binario pequeño (`tramo-helper`) recibe
**solo 6 comandos** por UDS 0600 + `SO_PEERCRED` (Linux) o Named Pipe con SID
(Windows). En Linux se limita a `CAP_NET_ADMIN`/`CAP_NET_RAW` con
`NoNewPrivileges=yes`, `ProtectSystem=strict` y `RestrictAddressFamilies`; en Windows
a un servicio con UAC puntual. Tras crear el TUN, el fd pasa al proceso de datos y
el helper conserva solo capacidades de routing.

El helper mantiene un **lease con heartbeat de 1 s y TTL de 3 s**: si el motor o la
UI mueren, revierte sus propias reglas etiquetadas y restaura el snapshot. Las reglas
de firewall propias llevan una etiqueta única y jamás modifican las del usuario ni
las de una VPN corporativa. Kill switch desactivado por defecto; `revert_direct` es
la política ante caída.
