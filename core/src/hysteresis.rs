// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Guardarrailes anti-flapping. Todos los valores son configurables por perfil.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct HysteresisConfig {
    /// Margen absoluto minimo que un retador debe mejorar.
    pub margin_ms: f64,
    /// Margen proporcional al score del incumbente.
    pub margin_ratio: f64,
    /// Ventanas consecutivas (de 2 s por defecto en el scheduler) que debe sostenerse la mejora.
    pub consecutive_windows: usize,
    pub cooldown_ms: u64,
    pub max_switches_10m: usize,
    pub min_samples: usize,
    pub min_window_ms: u64,
    /// Pérdida sostenida que dispara failover duro.
    pub hard_loss: f64,
    /// Timeouts consecutivos que disparan failover duro (~3 s a 200 ms).
    pub hard_timeouts: usize,
    pub max_handshake_age_ms: u64,
    /// Coste en ms por cada nivel de prioridad del nodo (0 = preferido).
    pub priority_step_ms: f64,
}

impl Default for HysteresisConfig {
    fn default() -> Self {
        Self {
            margin_ms: 8.0,
            margin_ratio: 0.12,
            consecutive_windows: 3,
            cooldown_ms: 60_000,
            max_switches_10m: 3,
            min_samples: 20,
            min_window_ms: 10_000,
            hard_loss: 0.10,
            hard_timeouts: 15,
            max_handshake_age_ms: 30_000,
            priority_step_ms: 2.0,
        }
    }
}

impl HysteresisConfig {
    pub fn validate(&self) -> Result<(), String> {
        if !(0.0..=100.0).contains(&self.margin_ms) || !self.margin_ms.is_finite() {
            return Err("margin_ms debe estar en [0, 100]".into());
        }
        if !(0.0..=1.0).contains(&self.margin_ratio) {
            return Err("margin_ratio debe estar en [0, 1]".into());
        }
        if self.consecutive_windows == 0 || self.consecutive_windows > 60 {
            return Err("consecutive_windows debe estar en [1, 60]".into());
        }
        if self.min_samples < 5 {
            return Err("min_samples debe ser >= 5".into());
        }
        if !(0.0..=1.0).contains(&self.hard_loss) {
            return Err("hard_loss debe estar en [0, 1]".into());
        }
        if self.hard_timeouts == 0 {
            return Err("hard_timeouts debe ser >= 1".into());
        }
        if !(0.0..=50.0).contains(&self.priority_step_ms) {
            return Err("priority_step_ms debe estar en [0, 50]".into());
        }
        Ok(())
    }

    /// Un retador solo gana si `score(retador) + margin < score(actual)`.
    pub fn margin(&self, incumbent_score: f64) -> f64 {
        if !incumbent_score.is_finite() {
            return f64::INFINITY;
        }
        self.margin_ms.max(self.margin_ratio * incumbent_score.abs())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_are_valid() {
        HysteresisConfig::default().validate().unwrap();
    }

    #[test]
    fn margin_uses_whichever_bound_is_larger() {
        let c = HysteresisConfig::default();
        assert_eq!(c.margin(50.0), 8.0); // 12% de 50 = 6 -> gana el minimo
        assert!((c.margin(200.0) - 24.0).abs() < 1e-9);
        assert!(c.margin(f64::INFINITY).is_infinite());
    }

    #[test]
    fn invalid_config_is_rejected() {
        let mut c = HysteresisConfig::default();
        c.consecutive_windows = 0;
        assert!(c.validate().is_err());
        c.consecutive_windows = 3;
        c.hard_loss = 1.5;
        assert!(c.validate().is_err());
    }
}
