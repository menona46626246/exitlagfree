// SPDX-License-Identifier: Apache-2.0
//! Decisor puro. No aplica rutas: solo emite una `Decision` con un motivo legible.
//! El plano de control (`tramo-engine` -> `tramo-ipc` -> `tramo-helper`) es el
//! unico que ejecuta cambios, y solo cuando `Decision.apply == true`.

use crate::hysteresis::HysteresisConfig;
use crate::metrics::WindowMetrics;
use crate::model::{Mode, RouteId};
use crate::score::{score_route, ScoreWeights};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

const TEN_MIN_MS: u64 = 600_000;

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Candidate {
    pub route: RouteId,
    /// `None` = sin muestras validas (ruta caida).
    pub metrics: Option<WindowMetrics>,
    /// Carga autodeclarada por el nodo [0,1]. Se trata como NO confiable.
    pub load: f64,
    /// Coste definido por el usuario, en ms-equivalentes. 0 por defecto.
    pub geo_cost_ms: f64,
    pub healthy: bool,
    pub handshake_age_ms: u64,
    /// Si el health del nodo esta ausente/antiguo, `load` se neutraliza a 0.5.
    pub metadata_stale: bool,
    /// 0 = preferido; cada nivel suma `priority_step_ms`.
    pub priority: u32,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum Action {
    Keep,
    Suggest(RouteId),
    Apply(RouteId),
    Failover(RouteId),
    RevertDirect,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct RouteScore {
    pub route: RouteId,
    /// Score efectivo (incluye prioridad). `INFINITY` si no es elegible.
    pub score: f64,
    pub eligible: bool,
    /// Motivo de no elegibilidad, vacio si es elegible.
    pub reason: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Decision {
    pub action: Action,
    pub route: RouteId,
    pub reason: String,
    pub scores: Vec<RouteScore>,
    /// Solo `true` en modo Auto: autoriza al plano de control a tocar rutas.
    pub apply: bool,
}

impl Decision {
    pub fn keep(route: RouteId, reason: impl Into<String>, scores: Vec<RouteScore>) -> Self {
        Self { action: Action::Keep, route, reason: reason.into(), scores, apply: false }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct DecisionInput {
    /// Milisegundos desde un reloj monotonico inyectado (NO hora de pared).
    pub now_ms: u64,
    pub mode: Mode,
    pub candidates: Vec<Candidate>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct DeciderState {
    pub incumbent: RouteId,
    /// Ventanas consecutivas en que cada retador supero la histeresis.
    pub streaks: BTreeMap<String, usize>,
    pub last_switch_ms: Option<u64>,
    pub recent_switches_ms: Vec<u64>,
    /// Congelado por demasiados cambios: se avisa al usuario de red inestable.
    pub frozen: bool,
}

impl DeciderState {
    pub fn new(incumbent: RouteId) -> Self {
        Self {
            incumbent,
            streaks: BTreeMap::new(),
            last_switch_ms: None,
            recent_switches_ms: Vec::new(),
            frozen: false,
        }
    }
}

fn ineligibility_reason(c: &Candidate, cfg: &HysteresisConfig) -> String {
    if !c.healthy {
        return "nodo marcado como no saludable".into();
    }
    match &c.metrics {
        None => "sin muestras validas (ruta caida)".into(),
        Some(m) if m.samples < cfg.min_samples => {
            format!("muestras insuficientes ({} < {})", m.samples, cfg.min_samples)
        }
        Some(m) if m.window_ms < cfg.min_window_ms => {
            format!("ventana demasiado corta ({} ms)", m.window_ms)
        }
        Some(_) => String::new(),
    }
}

fn is_eligible(c: &Candidate, cfg: &HysteresisConfig) -> bool {
    if !c.healthy || c.handshake_age_ms > cfg.max_handshake_age_ms {
        return false;
    }
    match &c.metrics {
        Some(m) => {
            m.samples >= cfg.min_samples
                && m.window_ms >= cfg.min_window_ms
                && m.loss < 0.25 // descalificacion temporal ante perdida extrema
        }
        None => false,
    }
}

fn is_hard_failure(incumbent: Option<&Candidate>, cfg: &HysteresisConfig) -> bool {
    match incumbent {
        None => true,
        Some(c) if !c.healthy => true,
        Some(c) => match &c.metrics {
            None => true,
            Some(m) => {
                (m.loss > cfg.hard_loss && m.window_ms >= 2_000)
                    || m.consecutive_timeouts >= cfg.hard_timeouts
            }
        },
    }
}

/// Funcion pura: dado el estado, la entrada y la configuracion, devuelve el
/// nuevo estado y la decision. El llamador conserva `state` si quiere simular
/// escenarios alternativos (util en benchmarks y tests).
pub fn decide(
    state: &DeciderState,
    input: &DecisionInput,
    cfg: &HysteresisConfig,
    w: &ScoreWeights,
) -> (DeciderState, Decision) {
    let mut next = state.clone();
    let now = input.now_ms;
    next.recent_switches_ms
        .retain(|t| now.saturating_sub(*t) < TEN_MIN_MS);

    let mut scores = Vec::with_capacity(input.candidates.len());
    let mut best: Option<(RouteId, f64)> = None;

    for c in &input.candidates {
        let eligible = is_eligible(c, cfg);
        let load = if c.metadata_stale { 0.5 } else { c.load };
        let eff = c
            .metrics
            .as_ref()
            .map(|m| score_route(m, load, c.geo_cost_ms, w) + (c.priority as f64) * cfg.priority_step_ms);
        let value = eff.unwrap_or(f64::INFINITY);
        if eligible && eff.is_some() {
            let better = match &best {
                None => true,
                Some((_, s)) => value < *s,
            };
            if better {
                best = Some((c.route.clone(), value));
            }
        }
        scores.push(RouteScore {
            route: c.route.clone(),
            score: value,
            eligible,
            reason: if eligible { String::new() } else { ineligibility_reason(c, cfg) },
        });
    }

    let incumbent_candidate = input.candidates.iter().find(|c| c.route == next.incumbent);
    let incumbent_score = scores
        .iter()
        .find(|s| s.route == next.incumbent)
        .map(|s| s.score)
        .unwrap_or(f64::INFINITY);

    // --- Fallo duro: prioriza disponibilidad, salta cooldown e histeresis ---
    if is_hard_failure(incumbent_candidate, cfg) {
        let target = best
            .as_ref()
            .filter(|(r, _)| *r != next.incumbent)
            .map(|(r, _)| r.clone());
        return match target {
            Some(route) => {
                let reason = format!(
                    "Fallo duro en {} (perdida/timeouts o nodo caido); {} es el mejor candidato sano. Se omiten cooldown e histeresis.",
                    next.incumbent, route
                );
                match input.mode {
                    Mode::Auto => {
                        next.incumbent = route.clone();
                        next.last_switch_ms = Some(now);
                        next.recent_switches_ms.push(now);
                        next.streaks.clear();
                        next.frozen = false;
                        (
                            next,
                            Decision {
                                action: Action::Failover(route.clone()),
                                route,
                                reason,
                                scores,
                                apply: true,
                            },
                        )
                    }
                    Mode::Suggest => (
                        next,
                        Decision {
                            action: Action::Suggest(route.clone()),
                            route,
                            reason,
                            scores,
                            apply: false,
                        },
                    ),
                    Mode::MeasureOnly => (
                        next,
                        Decision::keep(
                            next.incumbent.clone(),
                            format!("Solo medir: se sugeriria {route}, sin aplicar cambios. {reason}"),
                            scores,
                        ),
                    ),
                }
            }
            None => {
                let reason = format!(
                    "Fallo duro en {} y ningun candidato sano: vuelta a la ruta directa.",
                    next.incumbent
                );
                match input.mode {
                    Mode::Auto => {
                        next.incumbent = RouteId::direct();
                        next.last_switch_ms = Some(now);
                        next.recent_switches_ms.push(now);
                        next.streaks.clear();
                        (
                            next,
                            Decision {
                                action: Action::RevertDirect,
                                route: RouteId::direct(),
                                reason,
                                scores,
                                apply: true,
                            },
                        )
                    }
                    Mode::Suggest => (
                        next,
                        Decision {
                            action: Action::Suggest(RouteId::direct()),
                            route: RouteId::direct(),
                            reason,
                            scores,
                            apply: false,
                        },
                    ),
                    Mode::MeasureOnly => (
                        next,
                        Decision::keep(
                            next.incumbent.clone(),
                            format!("Solo medir: se revertiria a directa, sin aplicar cambios. {reason}"),
                            scores,
                        ),
                    ),
                }
            }
        };
    }

    // --- Camino normal: histeresis + K ventanas + cooldown + limite de cambios ---
    let challenger = best.filter(|(r, _)| *r != next.incumbent);
    let Some((route, challenger_score)) = challenger else {
        next.streaks.clear();
        return (
            next,
            Decision::keep(
                next.incumbent.clone(),
                "La ruta actual es la mejor medida; ningun tunel la supera. No se promete mejora de ping.".into(),
                scores,
            ),
        );
    };

    let margin = cfg.margin(incumbent_score);
    if challenger_score + margin >= incumbent_score {
        next.streaks.clear();
        return (
            next,
            Decision::keep(
                next.incumbent.clone(),
                format!(
                    "{} no supera la histeresis: necesita mejorar >{:.1} ms (mejora actual {:.1} ms).",
                    route,
                    margin,
                    incumbent_score - challenger_score
                ),
                scores,
            ),
        );
    }

    let key = route.as_str().to_string();
    let streak = {
        let entry = next.streaks.entry(key.clone()).or_insert(0);
        *entry += 1;
        *entry
    };
    next.streaks.retain(|k, _| k.as_str() == key.as_str());

    if streak < cfg.consecutive_windows {
        return (
            next,
            Decision::keep(
                next.incumbent.clone(),
                format!(
                    "{} es mejor, pero solo por {}/{} ventanas consecutivas.",
                    route, streak, cfg.consecutive_windows
                ),
                scores,
            ),
        );
    }

    if let Some(last) = next.last_switch_ms {
        if now.saturating_sub(last) < cfg.cooldown_ms {
            return (
                next,
                Decision::keep(
                    next.incumbent.clone(),
                    format!(
                        "Cooldown activo ({} ms restantes); no se cambia a {}.",
                        cfg.cooldown_ms - now.saturating_sub(last),
                        route
                    ),
                    scores,
                ),
            );
        }
    }

    if next.recent_switches_ms.len() >= cfg.max_switches_10m {
        next.frozen = true;
        return (
            next,
            Decision::keep(
                next.incumbent.clone(),
                "Red inestable: limite de 3 cambios / 10 min alcanzado. Ruta congelada; revisa WiFi/ISP.".into(),
                scores,
            ),
        );
    }

    let delta = incumbent_score - challenger_score;
    match input.mode {
        Mode::MeasureOnly => (
            next,
            Decision::keep(
                next.incumbent.clone(),
                format!(
                    "Solo medir: se recomendaria {} ({:.1} ms mejor), sin aplicar cambios.",
                    route, delta
                ),
                scores,
            ),
        ),
        Mode::Suggest => (
            next,
            Decision {
                action: Action::Suggest(route.clone()),
                route: route.clone(),
                reason: format!("Sugerencia: cambiar a {} ({:.1} ms mejor). Requiere confirmacion.", route, delta),
                scores,
                apply: false,
            },
        ),
        Mode::Auto => {
            next.incumbent = route.clone();
            next.last_switch_ms = Some(now);
            next.recent_switches_ms.push(now);
            next.streaks.clear();
            next.frozen = false;
            (
                next,
                Decision {
                    action: Action::Apply(route.clone()),
                    route: route.clone(),
                    reason: format!(
                        "Cambio automatico a {}: {:.1} ms mejor tras {} ventanas, cooldown respetado.",
                        route, delta, streak
                    ),
                    scores,
                    apply: true,
                },
            )
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::model::ProbeSample;

    fn candidate(id: &str, rtt: f64, load: f64) -> Candidate {
        let samples: Vec<ProbeSample> = (0..30).map(|i| ProbeSample::ok(i, rtt)).collect();
        Candidate {
            route: RouteId::new(id),
            metrics: WindowMetrics::from_samples(&samples, 60_000, 0),
            load,
            geo_cost_ms: 0.0,
            healthy: true,
            handshake_age_ms: 1_000,
            metadata_stale: false,
            priority: 0,
        }
    }

    fn candidates(direct_rtt: f64, relay_rtt: f64) -> Vec<Candidate> {
        vec![
            candidate("direct", direct_rtt, 0.0),
            candidate("wg-fra-1", relay_rtt, 0.0),
        ]
    }

    /// Ejecuta K ventanas consecutivas con la misma entrada.
    fn run_k(state: &DeciderState, input: &DecisionInput, k: usize) -> (DeciderState, Decision) {
        let cfg = HysteresisConfig::default();
        let w = ScoreWeights::default();
        let mut s = state.clone();
        let mut d = Decision::keep(RouteId::direct(), "", vec![]);
        for i in 0..k {
            let mut step = input.clone();
            step.now_ms = input.now_ms + (i as u64) * 2_000;
            let (next, decision) = decide(&s, &step, &cfg, &w);
            s = next;
            d = decision;
        }
        (s, d)
    }

    #[test]
    fn hysteresis_rejects_a_small_improvement() {
        let input = DecisionInput { now_ms: 1_000, mode: Mode::Auto, candidates: candidates(40.0, 35.0) };
        let (_, d) = run_k(&DeciderState::new(RouteId::direct()), &input, 6);
        assert_eq!(d.action, Action::Keep);
        assert!(!d.apply);
        assert!(d.reason.contains("histeresis"));
    }

    #[test]
    fn auto_applies_only_after_k_consecutive_windows() {
        let input = DecisionInput { now_ms: 1_000, mode: Mode::Auto, candidates: candidates(40.0, 20.0) };
        let (_, first) = run_k(&DeciderState::new(RouteId::direct()), &input, 2);
        assert_eq!(first.action, Action::Keep);
        let (state, third) = run_k(&DeciderState::new(RouteId::direct()), &input, 3);
        assert_eq!(third.action, Action::Apply(RouteId::new("wg-fra-1")));
        assert!(third.apply);
        assert_eq!(state.incumbent, RouteId::new("wg-fra-1"));
    }

    #[test]
    fn measure_only_never_modifies_routes() {
        let input = DecisionInput { now_ms: 1_000, mode: Mode::MeasureOnly, candidates: candidates(80.0, 10.0) };
        let (state, d) = run_k(&DeciderState::new(RouteId::direct()), &input, 10);
        assert_eq!(d.action, Action::Keep);
        assert!(!d.apply);
        assert_eq!(state.incumbent, RouteId::direct());
        assert!(d.reason.contains("Solo medir"));
    }

    #[test]
    fn suggest_mode_only_recommends() {
        let input = DecisionInput { now_ms: 1_000, mode: Mode::Suggest, candidates: candidates(80.0, 10.0) };
        let (state, d) = run_k(&DeciderState::new(RouteId::direct()), &input, 3);
        assert_eq!(d.action, Action::Suggest(RouteId::new("wg-fra-1")));
        assert!(!d.apply);
        assert_eq!(state.incumbent, RouteId::direct());
    }

    #[test]
    fn cooldown_blocks_a_second_switch() {
        // El relay es el incumbente (40 ms), la directa mejora (10 ms), pero
        // hubo un cambio hace 1 s: el cooldown de 60 s debe bloquear la vuelta.
        let mut state = DeciderState::new(RouteId::new("wg-fra-1"));
        state.last_switch_ms = Some(1_000);
        let input = DecisionInput {
            now_ms: 2_000,
            mode: Mode::Auto,
            candidates: candidates(10.0, 40.0),
        };
        let (_, last) = run_k(&state, &input, 3);
        assert_eq!(last.action, Action::Keep);
        assert!(!last.apply);
        assert!(last.reason.contains("Cooldown"), "reason={}", last.reason);
    }

    #[test]
    fn hard_failover_bypasses_cooldown_and_hysteresis() {
        let mut relay = candidate("wg-fra-1", 20.0, 0.0);
        let samples: Vec<ProbeSample> = (0..15)
            .map(|i| ProbeSample::ok(i, 20.0))
            .chain((15..30).map(|i| ProbeSample::lost(i)))
            .collect();
        relay.metrics = WindowMetrics::from_samples(&samples, 2_000, 0);
        let mut state = DeciderState::new(RouteId::new("wg-fra-1"));
        state.last_switch_ms = Some(1_000); // cooldown activo
        let input = DecisionInput {
            now_ms: 2_000,
            mode: Mode::Auto,
            candidates: vec![candidate("direct", 45.0, 0.0), relay],
        };
        let (next, d) = decide(&state, &input, &HysteresisConfig::default(), &ScoreWeights::default());
        assert_eq!(d.action, Action::Failover(RouteId::new("direct")));
        assert!(d.apply);
        assert_eq!(next.incumbent, RouteId::direct());
    }

    #[test]
    fn without_any_healthy_candidate_it_reverts_to_direct() {
        let mut dead = candidate("wg-fra-1", 20.0, 0.0);
        dead.healthy = false;
        dead.metrics = None;
        let state = DeciderState::new(RouteId::new("wg-fra-1"));
        let input = DecisionInput { now_ms: 1_000, mode: Mode::Auto, candidates: vec![dead] };
        let (next, d) = decide(&state, &input, &HysteresisConfig::default(), &ScoreWeights::default());
        assert_eq!(d.action, Action::RevertDirect);
        assert!(d.apply);
        assert_eq!(next.incumbent, RouteId::direct());
    }

    #[test]
    fn too_many_switches_freezes_the_route() {
        let mut state = DeciderState::new(RouteId::direct());
        state.recent_switches_ms = vec![100, 200, 300];
        let input = DecisionInput { now_ms: 1_000, mode: Mode::Auto, candidates: candidates(80.0, 10.0) };
        let (next, d) = run_k(&state, &input, 3);
        assert_eq!(d.action, Action::Keep);
        assert!(next.frozen);
        assert!(d.reason.contains("Red inestable"));
    }

    #[test]
    fn stale_node_load_is_neutral_not_rewarded() {
        let mut stale = candidate("wg-fra-1", 20.0, 0.9);
        stale.metadata_stale = true;
        let input = DecisionInput {
            now_ms: 1_000,
            mode: Mode::Suggest,
            candidates: vec![candidate("direct", 90.0, 0.0), stale],
        };
        let (_, d) = decide(&DeciderState::new(RouteId::direct()), &input, &HysteresisConfig::default(), &ScoreWeights::default());
        let relay = d.scores.iter().find(|s| s.route.as_str() == "wg-fra-1").unwrap();
        // 20 ms de p50 + 0.5*(100*0.5)=25 -> 45, no 20+0.5*90=65 ni 20.
        assert!((relay.score - 45.0).abs() < 1e-9, "score={}", relay.score);
    }

    #[test]
    fn candidate_with_too_few_samples_is_not_eligible() {
        let mut c = candidate("wg-fra-1", 1.0, 0.0);
        let samples: Vec<ProbeSample> = (0..3).map(|i| ProbeSample::ok(i, 1.0)).collect();
        c.metrics = WindowMetrics::from_samples(&samples, 60_000, 0);
        let input = DecisionInput {
            now_ms: 1_000,
            mode: Mode::Auto,
            candidates: vec![candidate("direct", 50.0, 0.0), c],
        };
        let (_, d) = decide(&DeciderState::new(RouteId::direct()), &input, &HysteresisConfig::default(), &ScoreWeights::default());
        assert_eq!(d.action, Action::Keep);
        assert!(!d.scores.iter().find(|s| s.route.as_str() == "wg-fra-1").unwrap().eligible);
    }

    #[test]
    fn priority_penalises_less_preferred_nodes() {
        let mut c = candidate("wg-fra-1", 20.0, 0.0);
        c.priority = 5;
        let input = DecisionInput {
            now_ms: 1_000,
            mode: Mode::Suggest,
            candidates: vec![candidate("direct", 90.0, 0.0), c],
        };
        let (_, d) = decide(&DeciderState::new(RouteId::direct()), &input, &HysteresisConfig::default(), &ScoreWeights::default());
        let relay = d.scores.iter().find(|s| s.route.as_str() == "wg-fra-1").unwrap();
        assert!((relay.score - 30.0).abs() < 1e-9); // 20 + 5*2
    }
}
