// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum KillSwitchPolicy {
    /// Por defecto: si el tunel cae, el trafico vuelve al ISP. Se documenta
    /// explicitamente para no sugerir una proteccion que no existe.
    #[default]
    Off,
    /// Opt-in: bloquea SOLO el trafico del perfil fuera del tunel.
    BlockGameTrafficOutsideTunnel,
}

/// Etiqueta unica para que el helper pueda revertir unicamente SUS reglas y
/// nunca tocar las del usuario ni las de una VPN corporativa.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct FirewallTag {
    pub id: String,
    pub comment: String,
}

/// Allowlist obligatoria en cualquier kill switch: sin ella se cortaria el
/// handshake WireGuard, DHCP o DNS y se "romperia la red del usuario".
pub fn allowlist_always_permitted(wg_endpoint: &str) -> Vec<String> {
    vec![
        "loopback".into(),
        "lan_configurada_por_usuario".into(),
        "dhcp_udp_67_68".into(),
        "dns_resolver_configurado".into(),
        format!("udp_endpoint_wireguard:{wg_endpoint}"),
    ]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn kill_switch_is_off_by_default() {
        assert_eq!(KillSwitchPolicy::default(), KillSwitchPolicy::Off);
    }

    #[test]
    fn wireguard_endpoint_is_always_allowed() {
        let a = allowlist_always_permitted("198.51.100.1:51820");
        assert!(a.iter().any(|x| x.contains("198.51.100.1:51820")));
        assert!(a.iter().any(|x| x.contains("dhcp")));
        assert!(a.iter().any(|x| x.contains("dns")));
    }
}
