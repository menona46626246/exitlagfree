# Cómo optimizar

## Antes de empezar

1. Añade tu juego y sus servidores ([cómo añadir juegos](como-anadir-juegos.md)).
2. Añade al menos un relay ([cómo añadir relays](como-anadir-relays-wireguard.md)).
3. Instala el Servicio elevado **solo si vas a usar túneles**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File installer\install-service.ps1   # como admin
   ```

## Flujo recomendado

1. **Diagnóstico primero** (sin túnel): pestaña *Diagnóstico* → servidor → *Medir* +
   *Traceroute*. Fíjate en pérdida y estabilidad, no solo en la media.
2. **Comparar**: *Comparar directa vs relays*. Verás una tabla con score/confianza y una
   explicación ("Mejor latencia", "Menos pérdida", "Ruta más estable").
3. Si la recomendación es **mantener la directa**: no actives nada. Ese es un resultado
   válido y el objetivo del programa es decirte la verdad.
4. Para activar un relay:
   - **Manual**: Dashboard → *Optimizar* con relay elegido en el perfil
     (`preferredRelayId`) o selección manual.
   - **Automática**: sin relay preferido, la app elige por score y gestiona cambios.
5. **Observa el Dashboard** durante la sesión: ping directo vs optimizado, pérdida,
   jitter y gráficas en vivo. Cada cambio de ruta queda en *Sesión* → eventos.
6. **Al terminar**: botón *Detener* (restaura red) o el botón rojo *Detener y restaurar
   red* para una parada de emergencia (túnel + kill switch + rutas).

## Cómo decide la optimización automática

- Cada candidato (directa y cada relay) tiene un score 0-100 con pesos:
  latencia 50 %, pérdida 25 %, jitter 15 %, estabilidad 10 %, menos penalizaciones
  (tramo relay→juego desconocido, carga alta, saltos extra, costo declarado).
- Cambia de ruta **solo si**:
  - la mejora supera el umbral (score +8 o latencia −10 ms por defecto),
  - el candidato lleva ≥30 s siendo consistentemente mejor,
  - pasaron ≥300 s desde el último cambio (cooldown),
  - no se excedieron 4 cambios/hora,
  - y tiene suficientes muestras (≥5).
- **Failback automático** a la directa si el túnel cae o si el túnel degrada la conexión.
- Todo configurable en *Configuración* → *Auto-optimización*.

## Durante el juego (auto-start opcional)

Si activas *Iniciar optimización automáticamente al arrancar el juego* en el perfil, la
app vigila los procesos del perfil (solo nombres) y arranca/detiene la optimización
cuando el juego abre/cierra. Es opcional y está desactivado por defecto.

## Después: revisa tu historial

*Session* → historial de sesiones con antes/después, nº de cambios de ruta, caídas de
túnel y la mejora estimada. Exporta un informe Markdown/JSON para compartirlo o comparar.
