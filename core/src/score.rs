// SPDX-License-Identifier: Apache-2.0
//! Scoring en "ms equivalentes". **Menor es mejor.**
//! Todos los pesos son configurables por el usuario (ver `tramo-config`).

use crate::metrics::WindowMetrics;
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct ScoreWeights {
    pub rtt: f64,
    pub jitter: f64,
    pub loss: f64,
    pub tail: f64,
    pub load: f64,
    pub stability: f64,
    pub geo: f64,
}

impl Default for ScoreWeights {
    fn default() -> Self {
        Self {
            rtt: 1.0,
            jitter: 2.0,
            loss: 1.0,
            tail: 1.0,
            load: 0.5,
            stability: 1.5,
            geo: 0.25,
        }
    }
}

impl ScoreWeights {
    /// Validacion de rangos al importar configuracion. No se permiten pesos
    /// negativos (invertirian el sentido del score) ni valores absurdos.
    pub fn validate(&self) -> Result<(), String> {
        for (name, value) in [
            ("rtt", self.rtt),
            ("jitter", self.jitter),
            ("loss", self.loss),
            ("tail", self.tail),
            ("load", self.load),
            ("stability", self.stability),
            ("geo", self.geo),
        ] {
            if !value.is_finite() || !(0.0..=10.0).contains(&value) {
                return Err(format!("peso '{name}' fuera de rango [0, 10]: {value}"));
            }
        }
        Ok(())
    }
}

/// Coste de la perdida de paquetes expresado en ms. Un paquete UDP perdido
/// cuesta del orden de un tick/retransmision (~300 ms), con saturacion para que
/// una ruta caida no produzca infinitos. 1% -> 3,03 ms; 5% -> 15,79 ms;
/// 20% -> 75 ms; >=50% -> 300 ms (tope).
pub fn loss_equivalent_ms(loss: f64) -> f64 {
    let loss = loss.clamp(0.0, 1.0);
    (300.0 * loss / (1.0 - loss).max(0.05)).min(300.0)
}

/// `load` (0..1) es **autodeclarado por el nodo y no confiable**, por eso su peso
/// por defecto es bajo. `geo_cost_ms` es definido por el usuario, 0 por defecto,
/// y nunca se infiere de su ubicacion.
pub fn score_route(
    m: &WindowMetrics,
    load: f64,
    geo_cost_ms: f64,
    w: &ScoreWeights,
) -> f64 {
    let load = load.clamp(0.0, 1.0);
    let geo = geo_cost_ms.max(0.0);
    w.rtt * m.p50_ms
        + w.jitter * m.jitter_ms
        + w.loss * loss_equivalent_ms(m.loss)
        + w.tail * m.tail_ms
        + w.load * (100.0 * load)
        + w.stability * (10.0 * m.instability)
        + w.geo * geo
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::model::ProbeSample;

    fn metrics(rtts: &[f64], lost: usize, flaps: usize) -> WindowMetrics {
        let mut s: Vec<ProbeSample> = rtts
            .iter()
            .enumerate()
            .map(|(i, &r)| ProbeSample::ok(i as u64, r))
            .collect();
        for i in 0..lost {
            s.push(ProbeSample::lost((rtts.len() + i) as u64));
        }
        WindowMetrics::from_samples(&s, 60_000, flaps).unwrap()
    }

    #[test]
    fn loss_equivalence_reference_values() {
        assert_eq!(loss_equivalent_ms(0.0), 0.0);
        assert!((loss_equivalent_ms(0.01) - 3.0303030303).abs() < 1e-6);
        assert!((loss_equivalent_ms(0.05) - 15.7894736842).abs() < 1e-6);
        assert!((loss_equivalent_ms(0.20) - 75.0).abs() < 1e-9);
        assert_eq!(loss_equivalent_ms(0.50), 300.0);
        assert_eq!(loss_equivalent_ms(0.99), 300.0);
        assert_eq!(loss_equivalent_ms(-1.0), 0.0);
    }

    #[test]
    fn default_weights_are_valid() {
        ScoreWeights::default().validate().unwrap();
    }

    #[test]
    fn invalid_weights_are_rejected() {
        let mut w = ScoreWeights::default();
        w.jitter = -1.0;
        assert!(w.validate().is_err());
        w.jitter = 2.0;
        w.loss = f64::NAN;
        assert!(w.validate().is_err());
        w.loss = 11.0;
        assert!(w.validate().is_err());
    }

    #[test]
    fn score_reference_value_case_a() {
        // p50=20, jitter=3, loss=1%, tail=10, load=0.2, instability=0.5
        let mut s = vec![
            ProbeSample::ok(0, 20.0),
            ProbeSample::ok(1, 23.0),
            ProbeSample::ok(2, 20.0),
            ProbeSample::ok(3, 30.0),
            ProbeSample::ok(4, 20.0),
            ProbeSample::lost(5),
        ];
        // 100 muestras para que loss sea ~1% y el histograma tenga p95=30
        for i in 6..100 {
            s.push(ProbeSample::ok(i as u64, 20.0));
        }
        let m = WindowMetrics::from_samples(&s, 60_000, 0).unwrap();
        // Forzamos los valores de referencia para aislar la formula del scoring.
        let m = WindowMetrics { jitter_ms: 3.0, tail_ms: 10.0, instability: 0.5, ..m };
        let got = score_route(&m, 0.2, 0.0, &ScoreWeights::default());
        // 20 + 2*3 + 3.030303 + 10 + 0.5*20 + 1.5*5 + 0 = 56.530303
        assert!((got - 56.5303030303).abs() < 1e-6, "score={got}");
    }

    #[test]
    fn direct_beats_tunnel_when_measurements_are_equal() {
        let m = metrics(&[20.0; 30], 0, 0);
        let direct = score_route(&m, 0.0, 0.0, &ScoreWeights::default());
        let relay = score_route(&m, 0.6, 0.0, &ScoreWeights::default());
        assert!(direct < relay, "direct={direct} relay={relay}");
        assert!((relay - direct - 30.0).abs() < 1e-9);
    }

    #[test]
    fn user_weights_change_ranking() {
        let m = metrics(&[20.0; 30], 0, 0);
        let base = ScoreWeights::default();
        let ignore_load = ScoreWeights { load: 0.0, ..base.clone() };
        assert_eq!(score_route(&m, 0.9, 0.0, &ignore_load), score_route(&m, 0.0, 0.0, &ignore_load));
        assert!(score_route(&m, 0.9, 0.0, &base) > score_route(&m, 0.0, 0.0, &base));
    }

    #[test]
    fn geo_cost_is_optional_and_nonnegative() {
        let m = metrics(&[15.0; 30], 0, 0);
        let w = ScoreWeights::default();
        assert_eq!(score_route(&m, 0.0, 0.0, &w), score_route(&m, 0.0, -50.0, &w));
        assert!(score_route(&m, 0.0, 40.0, &w) > score_route(&m, 0.0, 0.0, &w));
    }

    #[test]
    fn loss_dominates_a_small_latency_advantage() {
        let clean = metrics(&[25.0; 100], 0, 0);
        let lossy = metrics(&[20.0; 95], 5, 0);
        assert!(
            score_route(&clean, 0.0, 0.0, &ScoreWeights::default())
                < score_route(&lossy, 0.0, 0.0, &ScoreWeights::default()),
            "una ruta con perdida no debe ganar por 5 ms de p50"
        );
    }
}
