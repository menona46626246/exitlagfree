# Plantilla de relay. SUSTITUIR los marcadores; no commitear claves reales.
# Generar claves: bash scripts/gen-keys.sh
[Interface]
Address = 10.44.0.1/24
ListenPort = 51820
PrivateKey = <CLAVE_PRIVADA_DEL_RELAY>
# Sin PostUp/PostDown con comandos arbitrarios: el enrutado lo declara el
# helper de Tramo, no un script opaco en el nodo.
SaveConfig = false

[Peer]
# Cliente de prueba (clave PUBLICa del usuario)
PublicKey = <CLAVE_PUBLICA_DEL_CLIENTE>
AllowedIPs = 10.44.0.2/32
