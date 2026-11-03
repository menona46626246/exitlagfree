# ADR-0003 — Split tunneling por CIDR/puerto, no por proceso en Windows

**Estado:** Aceptado

## Contexto
El requisito original pide split tunneling «por proceso, puerto o CIDR». Con
WireGuard + Wintun **no existe** enrutado por proceso sin un callout WFP: hace falta
un driver firmado. Desarrollarlo implicaría firma de controladores, revisiones de
anti-cheat y una superficie de ataque en kernel, incompatible con un MVP legal y
auditable.

## Decisión
- Windows y Linux: reglas por **CIDR, puerto y protocolo** (declarativas, en `RoutePlan`).
- Linux: además, lanzamiento opt-in del juego dentro de un **network namespace** con
  el túnel como ruta por defecto.
- Windows: **por proceso queda fuera del MVP**, documentado en UI y FAQ.

## Consecuencias
Se cumple el criterio de aceptación «si el SO lo permite». Nunca se intercepta
memoria ni paquetes del juego. Un driver WFP propio solo se reconsideraría con
firma, auditoría y consentimiento explícito del usuario.
