// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct Check {
    pub name: String,
    pub ok: Option<bool>,
    pub detail: String,
    pub requires_privilege: bool,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct PreflightResult {
    pub can_measure: bool,
    pub can_tunnel: bool,
    pub checks: Vec<Check>,
}

/// Preflight sin efectos secundarios. Si faltan permisos, el resultado es
/// `can_tunnel = false` pero `can_measure = true`: la aplicacion degrada a
/// modo solo-medir en lugar de fallar o pedir root innecesariamente.
pub fn run_preflight(icmp_ok: bool, tun_backend_available: bool, privileged: bool) -> PreflightResult {
    let checks = vec![
        Check {
            name: "udp_probe".into(),
            ok: Some(true),
            detail: "sondas UDP/TCP no requieren privilegios".into(),
            requires_privilege: false,
        },
        Check {
            name: "icmp_raw".into(),
            ok: Some(icmp_ok),
            detail: if icmp_ok { "RAW ICMP disponible" } else { "se degrada a UDP/TCP" }.into(),
            requires_privilege: !icmp_ok,
        },
        Check {
            name: "tun_create".into(),
            ok: if privileged { Some(tun_backend_available) } else { None },
            detail: if privileged {
                "helper privilegiado disponible".into()
            } else {
                "se requiere UAC / polkit + CAP_NET_ADMIN para crear el TUN".into()
            },
            requires_privilege: true,
        },
    ];
    let can_tunnel = privileged && tun_backend_available;
    PreflightResult { can_measure: true, can_tunnel, checks }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn missing_privileges_degrade_to_measure_only() {
        let r = run_preflight(false, false, false);
        assert!(r.can_measure);
        assert!(!r.can_tunnel);
        assert!(r.checks.iter().any(|c| c.ok.is_none() && c.requires_privilege));
    }

    #[test]
    fn even_with_root_a_missing_backend_fails_closed() {
        let r = run_preflight(true, false, true);
        assert!(!r.can_tunnel, "nunca se debe fingir un tunel activo");
    }
}
