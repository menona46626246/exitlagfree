# FAQ

**¿Necesito ser administrador/root?** Para **medir**, no. Para crear el túnel y
cambiar rutas, sí: UAC + servicio en Windows, polkit + `CAP_NET_ADMIN` en Linux,
siempre en un helper separado con privilegio mínimo. Ver `guides/permisos.md`.

**¿Tramo promete bajar el ping?** No. Un relay añade distancia y encapsulado.
Solo ayuda si evita un tramo malo. Si la directa gana, Tramo se queda en directa.

**¿Por qué no hay split tunneling por proceso en Windows?** Requiere un callout WFP
y un driver firmado, con implicaciones de anti-cheat y seguridad. Se ofrecen reglas
por CIDR/puerto/protocolo y, en Linux, network namespaces. Ver ADR-0003.

**¿El firewall/antivirus bloquea el TUN?** Puede. El preflight lo detecta y degrada
a «solo medir»; Tramo nunca desactiva tu antivirus ni cambia reglas ajenas.

**¿Funciona detrás de CG-NAT?** Como **cliente**, sí. Para **alojar un nodo**, no:
necesitas IP pública o port-forward UDP.

**¿Qué pasa si el túnel cae?** Por defecto `revert_direct`: el tráfico vuelve al ISP
y se avisa. El kill switch (`block`) es opt-in y bloquea solo el tráfico del perfil.
Las rutas propias se retiran solas en ≤3 s por expiración del lease.

**¿Se envían mis datos a algún servidor?** No por defecto. Sin cuentas, sin
telemetría, sin logs. Cualquier flujo de red (sondas, catálogo, health,
actualizaciones) requiere configuración explícita. Ver `privacidad.md`.

**¿Es compatible con algún producto comercial?** No se afirma ninguna compatibilidad
oficial y no se usa ninguna marca de terceros. Es un proyecto original.

**¿Puede un juego con anti-cheat banearme por usar VPN?** Es posible según sus
términos de servicio. Tramo no evade ni oculta el túnel; los perfiles con
`requires_ack` piden confirmación y siempre puedes usar «solo medir».

**¿Dónde están mis claves?** Solo en tu equipo: keyring del SO (DPAPI/Credential
Manager, Secret Service) o archivo `0600`. Nunca se envían a la API de nodos ni se
exportan sin `--include-secrets`.

**¿Por qué el relay del laboratorio dice «simulado»?** Porque en este bloque aún no
existe el túnel real (Fase 3). Simularlo como si fuera real sería engañoso.
