// SPDX-License-Identifier: Apache-2.0
use std::collections::VecDeque;
use tramo_core::{
    decide, DeciderState, Decision, DecisionInput, HysteresisConfig, Mode, RouteId, ScoreWeights,
};

pub const MAX_HISTORY: usize = 500;

#[derive(Clone, Debug, PartialEq)]
pub struct DecisionRecord {
    pub now_ms: u64,
    pub route: RouteId,
    pub reason: String,
    pub apply: bool,
}

#[derive(Clone, Debug)]
pub struct EngineState {
    pub mode: Mode,
    pub optimization_enabled: bool,
    pub decider: DeciderState,
    pub active_profile: Option<String>,
    pub history: VecDeque<DecisionRecord>,
}

impl Default for EngineState {
    fn default() -> Self {
        Self::new()
    }
}

impl EngineState {
    pub fn new() -> Self {
        Self {
            mode: Mode::MeasureOnly,
            optimization_enabled: false,
            decider: DeciderState::new(RouteId::direct()),
            active_profile: None,
            history: VecDeque::new(),
        }
    }

    /// El toggle de "optimizacion" NO puede activarse en modo solo-medir:
    /// evita que un usuario crea que esta optimizando cuando solo esta midiendo.
    pub fn toggle(&mut self, enabled: bool) -> Result<(), String> {
        if enabled && self.mode == Mode::MeasureOnly {
            return Err(
                "modo 'solo medir' activo: selecciona 'sugerir' o 'auto cambiar' antes de activar".into(),
            );
        }
        self.optimization_enabled = enabled;
        if !enabled {
            self.decider.streaks.clear();
        }
        Ok(())
    }

    pub fn set_mode(&mut self, mode: Mode) {
        self.mode = mode;
        if mode == Mode::MeasureOnly {
            self.optimization_enabled = false;
        }
    }

    /// Evalua y registra. Devuelve la decision con `apply` ya resuelto; el
    /// llamador SOLO debe tocar rutas si `apply == true`.
    pub fn evaluate(
        &mut self,
        input: &DecisionInput,
        cfg: &HysteresisConfig,
        w: &ScoreWeights,
    ) -> Decision {
        let mut effective = input.clone();
        if !self.optimization_enabled || self.mode == Mode::MeasureOnly {
            effective.mode = Mode::MeasureOnly;
        } else {
            effective.mode = self.mode;
        }
        let (next, decision) = decide(&self.decider, &effective, cfg, w);
        self.decider = next;
        self.history.push_back(DecisionRecord {
            now_ms: input.now_ms,
            route: decision.route.clone(),
            reason: decision.reason.clone(),
            apply: decision.apply,
        });
        while self.history.len() > MAX_HISTORY {
            self.history.pop_front();
        }
        decision
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn toggle_is_refused_in_measure_only() {
        let mut s = EngineState::new();
        assert!(s.toggle(true).is_err());
        assert!(!s.optimization_enabled);
        s.set_mode(Mode::Auto);
        assert!(s.toggle(true).is_ok());
        assert!(s.optimization_enabled);
    }

    #[test]
    fn switching_back_to_measure_only_disables_optimization() {
        let mut s = EngineState::new();
        s.set_mode(Mode::Auto);
        s.toggle(true).unwrap();
        s.set_mode(Mode::MeasureOnly);
        assert!(!s.optimization_enabled);
        assert_eq!(s.mode, Mode::MeasureOnly);
    }

    #[test]
    fn history_is_bounded() {
        let mut s = EngineState::new();
        for i in 0..(MAX_HISTORY + 20) {
            s.evaluate(
                &DecisionInput { now_ms: i as u64, mode: Mode::Auto, candidates: vec![] },
                &HysteresisConfig::default(),
                &ScoreWeights::default(),
            );
        }
        assert_eq!(s.history.len(), MAX_HISTORY);
        // Sin candidatos hay fallo duro: se revierte a directa pero nunca se
        // aplica en modo solo-medir (optimization_enabled = false).
        assert!(s.history.iter().all(|h| !h.apply));
    }
}
