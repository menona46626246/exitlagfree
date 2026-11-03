# Diagnóstico

```bash
tramoctl diagnose --target 198.51.100.10:51820
tramoctl measure  --target 198.51.100.10:51820 --count 40 --protocol udp
tramoctl measure  --target 198.51.100.10:443   --protocol tcp
python3 tests/lab/run_lab.py
```

Comprobaciones: DNS (con degradación a IP literal si está filtrado), conectividad
TCP, disponibilidad de RAW ICMP, permisos de TUN, firewall y MTU. Cada resultado
lleva `ok: true|false|null`; `null` significa «no determinable sin privilegios» y
se muestra como aviso, no como error.

Degradación de protocolo: ICMP → UDP con número de secuencia → TCP handshake timing.
Si no hay eco UDP, `tramoctl measure` reintenta con TCP y lo indica. MTU es una
estimación por eco UDP de tamaño creciente: **no sustituye a PMTUD con DF**.
