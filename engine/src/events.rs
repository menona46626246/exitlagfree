// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Eventos hacia la UI (Tauri). Se emiten agregados a ~4 Hz, nunca por paquete.
/// No incluyen IP del usuario, nombres de perfil ni identificadores de dispositivo.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum EngineEvent {
    Metrics {
        route: String,
        p50_ms: f64,
        p95_ms: f64,
        jitter_ms: f64,
        loss: f64,
        at_ms: u64,
    },
    Decision {
        route: String,
        reason: String,
        apply: bool,
    },
    RouteChanged {
        from: String,
        to: String,
        reason: String,
    },
    PermissionMissing {
        capability: String,
        hint: String,
    },
    NodeUnreachable {
        node: String,
        reason: String,
    },
    ModeChanged {
        mode: String,
    },
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn events_are_tagged_and_contain_no_pii_fields() {
        let e = EngineEvent::Metrics {
            route: "direct".into(),
            p50_ms: 12.0,
            p95_ms: 18.0,
            jitter_ms: 2.0,
            loss: 0.0,
            at_ms: 1_000,
        };
        let j = serde_json::to_string(&e).unwrap();
        assert!(j.contains("\"kind\":\"metrics\""));
        assert!(!j.contains("ip"));
    }
}
