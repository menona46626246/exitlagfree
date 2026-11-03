// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Identificador de ruta. `direct` esta reservado para la ruta sin tunel.
#[derive(Clone, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
pub struct RouteId(pub String);

impl RouteId {
    pub fn new(id: impl Into<String>) -> Self {
        Self(id.into())
    }

    pub fn direct() -> Self {
        Self("direct".to_string())
    }

    pub fn as_str(&self) -> &str {
        &self.0
    }

    pub fn is_direct(&self) -> bool {
        self.0 == "direct"
    }
}

impl std::fmt::Display for RouteId {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.0)
    }
}

/// Una sonda. `rtt_ms = None` significa perdida/timeout.
/// La ventana se entrega **ordenada por `seq`**.
#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
pub struct ProbeSample {
    pub seq: u64,
    pub rtt_ms: Option<f64>,
}

impl ProbeSample {
    pub fn ok(seq: u64, rtt_ms: f64) -> Self {
        Self { seq, rtt_ms: Some(rtt_ms) }
    }

    pub fn lost(seq: u64) -> Self {
        Self { seq, rtt_ms: None }
    }
}

/// Modo de operacion. `MeasureOnly` es el valor por defecto y jamas modifica rutas.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Mode {
    #[default]
    MeasureOnly,
    Suggest,
    Auto,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_mode_is_measure_only() {
        assert_eq!(Mode::default(), Mode::MeasureOnly);
    }

    #[test]
    fn serde_mode_is_snake_case() {
        let json = serde_json::to_string(&Mode::MeasureOnly).unwrap();
        assert_eq!(json, "\"measure_only\"");
    }

    #[test]
    fn route_id_direct_is_recognised() {
        assert!(RouteId::direct().is_direct());
        assert!(!RouteId::new("wg-fra-1").is_direct());
    }
}
