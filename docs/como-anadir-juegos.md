# Cómo añadir juegos

## Opción A: desde la UI (recomendado)

1. Pestaña **Juegos** → **Añadir**.
2. Completa el diálogo:
   - **Nombre**: como lo verás en el Dashboard (p. ej. "Mi juego — Ranked").
   - **Ejecutables**: uno por línea, sin ruta (p. ej. `mi-juego.exe`). Sirven para
     detectar si el juego está corriendo y el auto-start.
   - **Ruta del ejecutable principal** (opcional): habilita detección de instalados.
   - **Servidores objetivo**: JSON con dominio o IP, puertos, región y protocolo.
     Ejemplo mínimo:
     ```json
     [
       {
         "name": "LATAM Sur",
         "domain": "sa-east.mi-juego.example",
         "ports": [7000, 7777],
         "region": "SA-East",
         "preferredProtocol": "Tcp"
       },
       { "name": "Por IP", "ip": "203.0.113.10", "ports": [7777] }
     ]
     ```
   - **Protocolo preferido**: `Auto` (recomendado: prueba ICMP y cae a TCP si está bloqueado),
     `Tcp` (handshake TCP, muy compatible), `Icmp` (el juego responde ping),
     `Udp` (solo si el servidor responde eco en ese puerto) o `Http` (con health URL).
   - **Modo de ruta**:
     - `Directo`: nunca aplica túnel (solo medición).
     - `Túnel solo juego` (recomendado): enruta únicamente las IPs de los servidores.
     - `Túnel global`: **todo tu tráfico** pasa por el relay (se advierte en la UI).
     - `Split tunneling`: se intenta por proceso; si el sistema no lo permite, la app
       lo indica y usa rutas por destino.
   - **Auto-inicio**: si el juego arranca y el perfil tiene `AutoStartOptimization`,
     la app puede iniciar la optimización sola (opcional, desactivado por defecto).
3. Guarda. El perfil aparece en la tabla y puedes **exportarlo a JSON** para respaldarlo.

## Opción B: importar JSON

- Pestaña *Juegos* → *Importar JSON* (o CLI: `gameroute profile import archivo.json`).
- Usa `examples/game-profile.example.json` como plantilla.

## Opción C: detectar juegos instalados

1. En **Configuración**, activa *"Autorizo escanear carpetas para detectar juegos"* y
   lista las carpetas (una por línea), p. ej.:
   ```
   C:\Program Files (x86)\Steam\steamapps\common
   C:\Riot Games
   ```
2. En *Juegos* → *Detectar instalados*: se buscan ejecutables cuyo nombre coincida con
   los perfiles existentes (solo se **leen nombres de archivo**; no se ejecuta ni se
   analiza nada del juego).

## ¿Qué es un "servidor objetivo"?

Es el endpoint del juego al que quieres medir/rutear. Puedes averiguarlo por:
- El dominio oficial que el propio juego publica o muestra en su configuración.
- Herramientas del sistema mientras juegas (`netstat -n` en Windows muestra las conexiones
  activas del proceso del juego — tú decides qué IP añadir; la app no intercepta nada).

> Regla de oro: solo se hacen pruebas activas a endpoints **que tú defines**. La app no
> escanea puertos masivamente ni adivina servidores.
