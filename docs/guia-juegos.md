# Guía — Juegos

En la sección **🎮 Juegos** gestionas los perfiles de juego. Un perfil define: nombre, ejecutable
(opcional), **servidores objetivo** (los únicos hosts que la app sondeará) y preferencias de ruta.

## Crear un juego
1. Pulsa **+ Nuevo** y pon un nombre reconocible (p. ej. «Counter-Strike 2»).
2. **Detectar instalado**: busca el ejecutable en rutas comunes (Program Files, Steam, Epic, Riot…).
   Si no aparece, escribe la ruta a mano (opcional; solo se usa para detectar el proceso y lanzar el juego).
3. Añade **al menos un servidor objetivo**:
   - **Dominio**: el hostname público de tu región (p. ej. `server-na-01.mijuego.com`).
   - **IP fija** (alternativa si el juego no usa dominio).
   - **Puertos** separados por coma (los que uses para TCP/UDP; informativo y usado por las probes UDP/TCP).
   - **Región** (p. ej. `US-Este`): usada por el historial de aprendizaje relay→región.
   - **Prueba por defecto**: ICMP normalmente; si el servidor bloquea ICMP elige TCP.
4. Pulsa **Guardar juego**.

## Preferencias del perfil
| Opción | Efecto |
|---|---|
| **Ruta directa (sin túnel)** | Solo diagnóstico y monitorización; nunca activa túnel. |
| **Túnel global** | Todo el tráfico del equipo pasa por el relay al optimizar. ⚠ |
| **Túnel solo destinos del juego** | Solo las IPs de los servidores objetivo pasan por el túnel. |
| **Relay preferido** | Si está habilitado y es medible, se prioriza en la recomendación. |
| **Auto-inicio al lanzar el juego** | Al detectar el proceso, arranca la optimización sola (si auto-switch está activo, decide la mejor ruta automáticamente; si no, espera tu confirmación). |
| **Auto-stop al cerrar** | Al cerrarse el proceso, restaura tu red (túnel + rutas). |

## Importar / exportar
- **Importar**: archivo JSON exportado antes (`juegos.json`). No contiene secretos.
- **Exportar**: guarda todos tus perfiles en un JSON portable.

> Los servidores objetivo son la frontera de la herramienta: **nunca** sondea nada que no esté en esta
> lista (o en una prueba libre explícita que escribas tú en Diagnóstico).
