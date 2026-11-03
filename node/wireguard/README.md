# Claves y configuracion del relay

1. `bash scripts/gen-keys.sh` genera un par local con `wg genkey` / `wg pubkey`.
2. Copia `wg0.conf.tpl` a `wg0.conf` (ignorado por Git) y sustituye los marcadores.
3. La **clave privada del cliente nunca se envia al nodo**: solo se intercambia
   la clave publica por un canal autenticado o de forma manual.
4. `wg0.conf` se monta en modo **solo lectura** en el contenedor.
5. Rotacion: anadir el peer nuevo, verificar handshake (`wg show`) y retirar el
   viejo. Nunca editar la config en caliente con `SaveConfig = true`.
6. El nodo necesita IP publica o port-forward UDP 51820. Detras de CG-NAT no es
   alcanzable (limitacion fisica documentada en `docs/limitaciones.md`).
