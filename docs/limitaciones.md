# Riesgos y limitaciones (documento obligatorio)

> Principio rector: **no ocultar limitaciones técnicas**. Si algo no es legal,
> seguro o físicamente viable, se dice aquí, en el README y en la UI.

## 1. Limitaciones físicas de la red

- Un relay **añade** al menos propagación hasta el nodo, encapsulado y, a menudo, un
  salto extra hasta el servidor del juego. Solo mejora la ruta si evita un tramo
  congestionado o mal interconectado (peering deficiente, ruta ISP subóptima).
- Si la ruta directa ya es óptima, **ningún túnel puede mejorarla**. Tramo mantiene
  la directa y lo muestra. No se garantiza reducción de ping, jitter ni pérdida.
- La medición es una **estimación**: las sondas son tráfico propio, no paquetes del
  juego. RTT de sonda ≠ latencia percibida en el motor del juego (tick rate,
  interpolación, buffer, servidor).
- ICMP/UDP pueden estar bloqueados; TCP handshake mide solo el establecimiento.
- El MTU es una estimación por eco UDP, no PMTUD real con bit DF.

## 2. Casos límite y su tratamiento

| Caso | Comportamiento | Resuelto |
|---|---|---|
| Sin permisos admin/root | Modo solo-medir con UDP/TCP; aviso accionable | Sí |
| Red corporativa con VPN previa | Detección, bloqueo de auto hasta confirmar, reglas propias etiquetadas | Parcial: convivencia no garantizada con políticas estrictas |
| Firewall bloquea ICMP | Degrada a UDP/TCP y etiqueta la métrica | Sí |
| CG-NAT en el cliente | Sin impacto (WireGuard inicia desde el cliente) | Sí |
| CG-NAT en el nodo | Requiere IP pública o port-forward; si no, `unreachable` | No: limitación física |
| WiFi inestable | Histéresis + límite 3 cambios/10 min → congela y avisa | Sí |
| Cambio de red en la sesión | Invalida ventana, rehace preflight, reaplica o revierte | Sí |
| Suspensión/hibernación | Reloj monotónico; salto >5 s descarta ventana y rehace handshake | Sí |
| IPv6 deshabilitado | IPv4 obligatorio, sin `::/0`, aviso en UI | Sí |
| DNS filtrado | Catálogo local, IP literal, resolver configurable, DoH opt-in | Parcial |
| Nodo caído | 15 timeouts → failover; sin candidato → `revert_direct`/`block` | Sí |
| Pérdida alta | `loss_eq` saturado en 300 ms, descalificación >25 % sostenida | Sí |
| Antivirus vs TUN | Preflight con error; nunca se desactiva el AV | No: resolución manual |
| Anti-cheat sensible a VPN | Aviso de posibles ToS, perfil `requires_ack`, sin ocultar túnel ni hooks | No: decisión del usuario |

## 3. Limitaciones funcionales del MVP actual

- **Los backends de túnel devuelven `NotImplemented`**: no se crea ningún túnel ni se
  modifica ninguna ruta todavía (Fase 3). Nada simula un túnel activo.
- **Split tunneling por proceso en Windows está fuera de alcance**: requiere un
  callout WFP y un driver firmado. Alternativa: CIDR/puerto/protocolo; en Linux,
  network namespaces. Ver ADR-0003.
- El **relay del laboratorio es una simulación** con retardo fijo; TTD/TTF reales
  requieren `tests/lab/netem.sh` con root y un túnel real.
- La UI usa un adaptador de demostración claramente etiquetado hasta Fase 6;
  no muestra métricas inventadas como si fueran reales.
- No hay updater firmado activo todavía (`active: false`).
- macOS queda para una fase posterior; IPv6 es deseable, no obligatorio.
- No hay optimización de throughput/qdisc, ni malla P2P, ni panel multiusuario.

## 4. Riesgos legales y de cumplimiento

- Usar una VPN puede infringir los **términos de servicio** de un juego o disparar
  anti-cheat. Tramo no evade, no oculta ni modifica nada; informa y pide confirmación.
- El nombre actual del repositorio contiene una marca de terceros: **renombrado
  pendiente de autorización**. No se usa esa marca en artefactos nuevos ni se afirma
  compatibilidad oficial.
- Un operador de relay **ve el tráfico que lo atraviesa**. Solo se recomiendan nodos
  propios o de confianza; la privacidad depende de esa confianza.
- Los drivers Wintun/WireGuardNT son GPLv2: se usan por ABI como componentes del
  sistema, sin enlazarlos, con su aviso y ofrecimiento de fuente.

## 5. Riesgos operativos

| Riesgo | Mitigación | Residual |
|---|---|---|
| Rutas huérfanas tras un crash | Lease con heartbeat 3 s + snapshot + rollback | Bajo |
| Fuga de tráfico al caer el túnel | Kill switch opt-in y política explícita `revert_direct` | Medio (por defecto se vuelve al ISP) |
| Nodo malicioso que miente sobre carga | Peso bajo (0,5) y medición local dominante | Medio |
| Actualización comprometida | Firma Ed25519/minisign + cosign/SLSA (Fase 8) | Medio hasta Fase 8 |
| Configuración maliciosa importada | JSON Schema, rangos, rechazo de campos ejecutables | Bajo |
| Falsos positivos de cambio de ruta | Histéresis + K ventanas + cooldown + límite | Bajo (0 en laboratorio) |
