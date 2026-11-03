// SPDX-License-Identifier: Apache-2.0
//! Sondas de red. Reglas:
//! * Las sondas son **sockets propios**: nunca se intercepta, modifica ni
//!   inyecta trafico del juego.
//! * Se usa `Instant` (monotonico) para todos los RTT.
//! * Cada protocolo degrada al siguiente si faltan permisos o el firewall
//!   bloquea: ICMP -> UDP con numero de secuencia -> TCP handshake timing.
//! * Un resultado perdido se representa como `ProbeSample::lost`, no como 0 ms.

pub mod channel;
pub mod icmp;
pub mod mtu;
pub mod tcp;
pub mod udp;

pub use channel::{BypassHint, ProbeChannel};
pub use icmp::{icmp_available, run_icmp_probe};
pub use mtu::estimate_path_mtu;
pub use tcp::run_tcp_probe;
pub use udp::run_udp_probe;

use serde::{Deserialize, Serialize};
use std::net::{SocketAddr, ToSocketAddrs};
use std::time::Duration;
use tramo_core::ProbeSample;

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct ProbeConfig {
    pub interval_ms: u64,
    pub timeout_ms: u64,
    pub count: usize,
}

impl Default for ProbeConfig {
    fn default() -> Self {
        Self { interval_ms: 200, timeout_ms: 1_000, count: 30 }
    }
}

impl ProbeConfig {
    pub fn timeout(&self) -> Duration {
        Duration::from_millis(self.timeout_ms)
    }

    pub fn interval(&self) -> Duration {
        Duration::from_millis(self.interval_ms)
    }

    pub fn validate(&self) -> Result<(), String> {
        if !(50..=10_000).contains(&self.interval_ms) {
            return Err("interval_ms fuera de [50, 10000]".into());
        }
        if self.timeout_ms < self.interval_ms {
            return Err("timeout_ms debe ser >= interval_ms".into());
        }
        if self.count == 0 || self.count > 10_000 {
            return Err("count fuera de [1, 10000]".into());
        }
        Ok(())
    }
}

#[derive(Debug)]
pub enum ProbeError {
    InvalidTarget(String),
    Io(std::io::Error),
    Permission(String),
}

impl std::fmt::Display for ProbeError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::InvalidTarget(s) => write!(f, "destino invalido: {s}"),
            Self::Io(e) => write!(f, "error de red: {e}"),
            Self::Permission(s) => write!(f, "permiso insuficiente: {s}"),
        }
    }
}

impl std::error::Error for ProbeError {}

impl From<std::io::Error> for ProbeError {
    fn from(e: std::io::Error) -> Self {
        Self::Io(e)
    }
}

/// Resuelve "host:puerto" con el resolver del sistema. Si el DNS esta filtrado,
/// el llamador puede pasar una IP literal y esta funcion no hace consultas.
pub fn resolve_target(dest: &str) -> Result<SocketAddr, ProbeError> {
    let mut addrs = dest
        .to_socket_addrs()
        .map_err(|e| ProbeError::InvalidTarget(format!("{dest}: {e}")))?;
    addrs.next().ok_or_else(|| ProbeError::InvalidTarget(format!("sin resultados para {dest}")))
}

/// Seleccion de protocolo segun disponibilidad. Devuelve el motivo para la UI.
pub fn select_protocol(icmp_ok: bool, udp_ok: bool) -> (&'static str, String) {
    if icmp_ok {
        ("icmp", "ICMP disponible".into())
    } else if udp_ok {
        ("udp", "ICMP bloqueado o sin CAP_NET_RAW: se usan sondas UDP con secuencia".into())
    } else {
        ("tcp", "UDP e ICMP no disponibles: se mide el RTT del handshake TCP".into())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn config_validation() {
        assert!(ProbeConfig::default().validate().is_ok());
        assert!(ProbeConfig { count: 0, ..Default::default() }.validate().is_err());
        assert!(ProbeConfig { timeout_ms: 10, ..Default::default() }.validate().is_err());
    }

    #[test]
    fn resolves_ip_literal_without_dns() {
        let a = resolve_target("127.0.0.1:27015").unwrap();
        assert_eq!(a.port(), 27015);
        assert!(resolve_target("no:puerto").is_err());
    }

    #[test]
    fn protocol_degradation_order() {
        assert_eq!(select_protocol(true, true).0, "icmp");
        assert_eq!(select_protocol(false, true).0, "udp");
        assert_eq!(select_protocol(false, false).0, "tcp");
    }
}
