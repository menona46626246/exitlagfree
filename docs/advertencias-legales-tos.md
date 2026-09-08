# Advertencias legales y términos de servicio — GameRoute Optimizer

> No es asesoría legal. Si operas relays o juegas bajo contratos especiales, consulta a un profesional.

## Naturaleza de la herramienta
GameRoute Optimizer es una herramienta de **diagnóstico y optimización de rutas de red** que:
- mide la conexión hacia los servidores de juego que **tú** declaras;
- compara tu ruta directa con relays **WireGuard propios/tuyos** que **tú** configuras;
- aplica rutas/túneles solo con tu confirmación y siempre de forma **reversible**.

## Límites de uso aceptable
Queda **prohibido** usar la herramienta para:
1. **Evasión de restricciones geográficas o regionales** (geo-bloqueos, catálogos por región).
2. **Evasión de sanciones de servicio** (bans) en juegos o plataformas, o para eludir medidas anti-trampas.
3. **Ocultar actividad ilegal** o vulnerar leyes de telecomunicaciones/propiedad intelectual.
4. **Usar relays, VPS o credenciales de terceros sin autorización** del propietario.
5. **Dañar, saturar o sondear** redes, servidores o servicios ajenos (las probes se limitan a tus hosts,
   con volumen bajo y respetando los TTL de la red).

## Responsabilidad del usuario
- Eres responsable de los relays que configuras (servidores, claves, tráfico que pasa por ellos) y de
  revisar los **términos de servicio del juego, la plataforma y tu ISP/proveedor**.
- Algunos juegos prohíben VPN/proxies; usar un túnel hacia servidores de juego puede violar esos términos.
  El programa avisa, pero la decisión y la responsabilidad son tuyas.
- El modo **túnel global** envía todo el tráfico del equipo por tu relay: verifica que el operador del
  servidor lo permite y que no incumples tu contrato de servicios.

## Sin garantías
El software se distribuye «tal cual», sin garantías de disponibilidad, exactitud de métricas o idoneidad
para un fin concreto. Las condiciones de la red cambian; ninguna métrica es una promesa de rendimiento.
No nos hacemos responsables de daños derivados del uso (incluidos cortes de conexión durante la
activación/restauración de túneles) más allá de lo exigible por ley.

## Sin servicios de terceros
La aplicación no usa ni ofrece relays, VPNs ni infraestructura de terceros, no recopila datos personales,
no contacta servidores propios y no muestra publicidad. Todo el procesamiento es local.

## Propiedad intelectual
«GameRoute Optimizer» es un proyecto independiente; no está afiliado a ExitLag ni a otros servicios de
optimización. El código es original de este repositorio y no incluye marcas, activos ni código de terceros
salvo las dependencias declaradas (MIT/BSD/APACHE, ver dependencias).
