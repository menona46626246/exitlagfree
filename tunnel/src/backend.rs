// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Configuracion de un tunel. `private_key` existe SOLO en memoria: nunca se
/// serializa a JSON, nunca se exporta y nunca se envia a la API de nodos.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TunnelConfig {
    pub node_id: String,
    pub endpoint: String,
    pub public_key: String,
    pub private_key: String,
    pub listen_port: u16,
    pub fwmark: Option<u32>,
    pub ipv6: bool,
}

/// Version segura para logs: sin clave privada y sin endpoint completo.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct TunnelConfigPublic {
    pub node_id: String,
    pub public_key: String,
    pub listen_port: u16,
}

impl TunnelConfig {
    pub fn public_view(&self) -> TunnelConfigPublic {
        TunnelConfigPublic {
            node_id: self.node_id.clone(),
            public_key: self.public_key.clone(),
            listen_port: self.listen_port,
        }
    }
}

#[derive(Debug)]
pub enum TunnelError {
    NotImplemented(&'static str),
    Permission(String),
    InvalidConfig(String),
    Io(String),
}

impl std::fmt::Display for TunnelError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::NotImplemented(s) => write!(f, "backend aun no implementado: {s}"),
            Self::Permission(s) => write!(f, "permiso insuficiente: {s}"),
            Self::InvalidConfig(s) => write!(f, "configuracion invalida: {s}"),
            Self::Io(s) => write!(f, "error de E/S: {s}"),
        }
    }
}

impl std::error::Error for TunnelError {}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum TunnelStatus {
    #[default]
    Down,
    Up,
    Degraded,
}

pub trait TunnelBackend {
    fn name(&self) -> &'static str;
    /// Debe verificar la ruta efectiva antes de devolver `Ok`.
    fn up(&mut self, cfg: &TunnelConfig) -> Result<(), TunnelError>;
    fn down(&mut self) -> Result<(), TunnelError>;
    fn status(&self) -> TunnelStatus;
    fn last_handshake_ms(&self) -> u64;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn public_view_never_contains_private_key() {
        let c = TunnelConfig {
            node_id: "n1".into(),
            endpoint: "198.51.100.1:51820".into(),
            public_key: "PUBLICA".into(),
            private_key: "SUPER_SECRETO".into(),
            listen_port: 51820,
            fwmark: Some(0xca6d),
            ipv6: false,
        };
        let v = c.public_view();
        let j = serde_json::to_string(&v).unwrap();
        assert!(!j.contains("SUPER_SECRETO"));
        assert!(!j.contains("198.51.100.1"));
        assert!(j.contains("PUBLICA"));
    }

    #[test]
    fn default_status_is_down() {
        assert_eq!(TunnelStatus::default(), TunnelStatus::Down);
    }
}
