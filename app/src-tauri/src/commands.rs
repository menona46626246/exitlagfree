// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};
use tramo_config::Config;
use tramo_core::{
    decide, Decision, DecisionInput, HysteresisConfig, Mode, RouteId, ScoreWeights, WindowMetrics,
};
use tramo_engine::{EngineEvent, EngineState};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct NodeView {
    pub id: String,
    pub name: String,
    pub region_label: Option<String>,
    pub priority: u32,
    pub enabled: bool,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct StatusView {
    pub mode: String,
    pub optimization_enabled: bool,
    pub active_route: String,
    pub kill_switch: bool,
    pub telemetry_enabled: bool,
    pub logging_enabled: bool,
    pub nodes: Vec<NodeView>,
    /// Aviso permanente: nunca se promete reduccion de latencia.
    pub disclaimer: String,
}

pub struct UiState {
    pub engine: EngineState,
    pub config: Config,
    pub weights: ScoreWeights,
    pub hysteresis: HysteresisConfig,
}

impl Default for UiState {
    fn default() -> Self {
        Self::new(Config::default())
    }
}

impl UiState {
    pub fn new(config: Config) -> Self {
        let weights = config.weights.clone();
        let hysteresis = config.hysteresis.clone();
        Self {
            engine: EngineState::new(),
            config,
            weights,
            hysteresis,
        }
    }

    pub fn status(&self) -> StatusView {
        StatusView {
            mode: format!("{:?}", self.engine.mode),
            optimization_enabled: self.engine.optimization_enabled,
            active_route: self.engine.decider.incumbent.to_string(),
            kill_switch: self.config.kill_switch,
            telemetry_enabled: self.config.telemetry.enabled,
            logging_enabled: self.config.logging.enabled,
            nodes: self
                .config
                .nodes
                .iter()
                .map(|n| NodeView {
                    id: n.id.clone(),
                    name: n.name.clone(),
                    region_label: n.region_label.clone(),
                    priority: n.priority,
                    enabled: n.enabled,
                })
                .collect(),
            disclaimer: "Un relay siempre anade propagacion y encapsulado. Tramo solo cambia de ruta si la medicion lo justifica; no garantiza menor ping.".into(),
        }
    }

    pub fn set_mode(&mut self, mode: Mode) {
        self.engine.set_mode(mode);
        self.config.mode = mode;
    }

    pub fn toggle(&mut self, enabled: bool) -> Result<(), String> {
        self.engine.toggle(enabled)
    }

    /// Recomendacion sin aplicar cambios. La UI muestra la tabla de scores.
    pub fn recommend(&self, input: &DecisionInput) -> Decision {
        let (_, d) = decide(
            &self.engine.decider,
            input,
            &self.hysteresis,
            &self.weights,
        );
        d
    }

    pub fn metrics_event(&self, route: &RouteId, m: &WindowMetrics, at_ms: u64) -> EngineEvent {
        EngineEvent::Metrics {
            route: route.to_string(),
            p50_ms: m.p50_ms,
            p95_ms: m.p95_ms,
            jitter_ms: m.jitter_ms,
            loss: m.loss,
            at_ms,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_status_is_private_and_measure_only() {
        let s = UiState::default().status();
        assert_eq!(s.mode, "MeasureOnly");
        assert!(!s.optimization_enabled);
        assert!(!s.telemetry_enabled);
        assert!(!s.logging_enabled);
        assert!(!s.kill_switch);
        assert!(s.disclaimer.contains("no garantiza"));
    }

    #[test]
    fn status_never_exposes_private_keys() {
        let mut cfg = Config::default();
        cfg.nodes.push(tramo_config::NodeEntry {
            id: "n1".into(),
            name: "N".into(),
            endpoint: "198.51.100.1:51820".into(),
            public_key: "PUBLICA".into(),
            region_label: None,
            priority: 0,
            geo_cost_ms: 0.0,
            enabled: true,
            private_key: Some("SECRETO".into()),
        });
        let j = serde_json::to_string(&UiState::new(cfg).status()).unwrap();
        assert!(!j.contains("SECRETO"));
        assert!(j.contains("n1"));
    }

    #[test]
    fn toggle_requires_non_measure_mode() {
        let mut ui = UiState::default();
        assert!(ui.toggle(true).is_err());
        ui.set_mode(Mode::Auto);
        assert!(ui.toggle(true).is_ok());
        assert!(ui.status().optimization_enabled);
    }
}
