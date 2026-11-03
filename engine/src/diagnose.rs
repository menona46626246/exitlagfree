// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};
use std::net::{TcpStream, ToSocketAddrs};
use std::time::Duration;

/// `ok = None` significa "no determinable sin privilegios": se muestra como
/// aviso, nunca como error que bloquee la aplicacion.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct DiagnosticResult {
    pub name: String,
    pub ok: Option<bool>,
    pub detail: String,
    pub requires_privilege: bool,
}

impl DiagnosticResult {
    fn new(name: &str, ok: Option<bool>, detail: impl Into<String>, priv_: bool) -> Self {
        Self { name: name.into(), ok, detail: detail.into(), requires_privilege: priv_ }
    }
}

/// Diagnostico basico, sin elevar privilegios y sin enviar datos a terceros.
pub fn run_diagnostics(target: Option<&str>) -> Vec<DiagnosticResult> {
    let mut out = Vec::new();

    let icmp = tramo_probe::icmp_available();
    out.push(DiagnosticResult::new(
        "icmp_raw",
        Some(icmp),
        if icmp {
            "socket RAW ICMP disponible"
        } else {
            "sin CAP_NET_RAW / ping_group_range; se degradara a UDP y TCP"
        },
        !icmp,
    ));

    match target {
        None => out.push(DiagnosticResult::new(
            "destino",
            None,
            "sin destino configurado; configura host:puerto en el perfil",
            false,
        )),
        Some(dest) => {
            let resolved = dest.to_socket_addrs();
            match resolved {
                Ok(mut addrs) => {
                    let addr = addrs.next();
                    out.push(DiagnosticResult::new(
                        "dns",
                        Some(addr.is_some()),
                        format!("resolucion de {dest}: {:?}", addr),
                        false,
                    ));
                    if let Some(a) = addr {
                        let ok = TcpStream::connect_timeout(&a, Duration::from_millis(1_500)).is_ok();
                        out.push(DiagnosticResult::new(
                            "conectividad_tcp",
                            Some(ok),
                            format!("handshake TCP con {a}: {}", if ok { "ok" } else { "sin respuesta" }),
                            false,
                        ));
                    }
                }
                Err(e) => out.push(DiagnosticResult::new(
                    "dns",
                    Some(false),
                    format!("DNS filtrado o destino invalido: {e}. Usa una IP literal."),
                    false,
                )),
            }
        }
    }

    out.push(DiagnosticResult::new(
        "tun",
        None,
        "creacion de TUN requiere el helper privilegiado (UAC en Windows, CAP_NET_ADMIN/polkit en Linux); se comprueba al activar el tunel",
        true,
    ));
    out.push(DiagnosticResult::new(
        "firewall",
        None,
        "no se modifica el firewall en modo solo-medir; las reglas propias se etiquetan y se revierten al cerrar",
        true,
    ));
    out.push(DiagnosticResult::new(
        "mtu",
        None,
        "estimacion basica por eco UDP disponible con tramoctl diagnose --mtu; no sustituye a PMTUD con DF",
        false,
    ));
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn diagnostics_never_crash_without_target() {
        let d = run_diagnostics(None);
        assert!(d.iter().any(|x| x.name == "tun" && x.requires_privilege));
        assert!(d.iter().any(|x| x.name == "destino" && x.ok.is_none()));
    }

    #[test]
    fn invalid_destination_is_reported_not_panicked() {
        let d = run_diagnostics(Some("esto-no-es-un-destino"));
        assert!(d.iter().any(|x| x.name == "dns" && x.ok == Some(false)));
    }
}
