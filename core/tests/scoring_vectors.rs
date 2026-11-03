// SPDX-License-Identifier: Apache-2.0
//! Test de integracion: los vectores del oraculo Python (`tests/lab/fixtures`)
//! deben coincidir con la implementacion Rust de `core`. Asi se detecta drift
//! entre el laboratorio reproducible y el motor de produccion.

use serde_json::Value;
use tramo_core::{loss_equivalent_ms, score_route, FixedHistogram, ScoreWeights, WindowMetrics};

const FIXTURE: &str = include_str!("../../tests/lab/fixtures/scoring_cases.json");

#[test]
fn oracle_vectors_match_rust_core() {
    let v: Value = serde_json::from_str(FIXTURE).unwrap();
    for c in v["loss_equivalents"].as_array().unwrap() {
        let loss = c["loss"].as_f64().unwrap();
        let expected = c["expected"].as_f64().unwrap();
        assert!(
            (loss_equivalent_ms(loss) - expected).abs() < 1e-6,
            "loss_eq({loss})"
        );
    }
    let rtts: Vec<f64> = v["histogram"]["rtts"]
        .as_array()
        .unwrap()
        .iter()
        .map(|x| x.as_f64().unwrap())
        .collect();
    let mut h = FixedHistogram::new();
    for r in &rtts {
        h.record(*r);
    }
    let e = &v["histogram"]["expected"];
    assert_eq!(h.percentile(50.0), e["p50"].as_f64());
    assert_eq!(h.percentile(95.0), e["p95"].as_f64());
    assert_eq!(h.percentile(99.0), e["p99"].as_f64());

    let s = &v["score"];
    let m = WindowMetrics {
        samples: 100,
        received: 99,
        loss: s["metrics"]["loss"].as_f64().unwrap(),
        p50_ms: s["metrics"]["p50_ms"].as_f64().unwrap(),
        p95_ms: 30.0,
        p99_ms: 30.0,
        jitter_ms: s["metrics"]["jitter_ms"].as_f64().unwrap(),
        jitter_rfc3550_ms: 0.0,
        tail_ms: s["metrics"]["tail_ms"].as_f64().unwrap(),
        consecutive_timeouts: 0,
        window_ms: 60_000,
        instability: s["metrics"]["instability"].as_f64().unwrap(),
    };
    let got = score_route(
        &m,
        s["load"].as_f64().unwrap(),
        s["geo_cost_ms"].as_f64().unwrap(),
        &ScoreWeights::default(),
    );
    assert!(
        (got - s["expected"].as_f64().unwrap()).abs() < 1e-6,
        "score={got}"
    );
}
