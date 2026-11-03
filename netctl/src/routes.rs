// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Plan declarativo: el helper lo traduce a `ip rule`/`ip route` (Linux) o a
/// reglas WFP (Windows). Ningun elemento es un comando de shell ejecutable.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct RoutePlan {
    pub table: u32,
    pub fwmark: u32,
    pub tunnel_interface: String,
    /// Prefijos destino que se enrutan por el tunel.
    pub cidrs: Vec<String>,
    /// Puertos destino (1..=65535) que se enrutan por el tunel.
    pub ports: Vec<u16>,
    /// Nunca secuestrar la ruta por defecto salvo peticion explicita.
    pub override_default_route: bool,
    /// Permite que la sonda directa esquive el tunel (Linux).
    pub direct_probe_fwmark: u32,
}

pub fn build_route_plan(cidrs: &[String], ports: &[u16], table: u32, fwmark: u32) -> RoutePlan {
    let mut clean_cidrs: Vec<String> = cidrs
        .iter()
        .filter(|c| c.parse::<std::net::Ipv4Addr>().is_ok() || c.contains('/'))
        .cloned()
        .collect();
    clean_cidrs.sort();
    clean_cidrs.dedup();
    let mut clean_ports: Vec<u16> = ports.iter().copied().filter(|p| *p > 0).collect();
    clean_ports.sort_unstable();
    clean_ports.dedup();
    RoutePlan {
        table,
        fwmark,
        tunnel_interface: "tramo0".into(),
        cidrs: clean_cidrs,
        ports: clean_ports,
        override_default_route: false,
        direct_probe_fwmark: fwmark ^ 0x1,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn plan_is_deterministic_and_never_takes_default_route() {
        let cidrs = vec!["203.0.113.0/24".into(), "203.0.113.0/24".into(), "basura".into()];
        let a = build_route_plan(&cidrs, &[27015, 27015, 0], 51820, 0xca6d);
        let b = build_route_plan(&cidrs, &[0, 27015], 51820, 0xca6d);
        assert_eq!(a, b);
        assert_eq!(a.cidrs, vec!["203.0.113.0/24".to_string()]);
        assert_eq!(a.ports, vec![27015]);
        assert!(!a.override_default_route);
        assert_ne!(a.direct_probe_fwmark, a.fwmark);
    }
}
