// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};
use std::collections::VecDeque;
use tramo_core::{ProbeSample, RouteId, WindowMetrics};

/// Ventana rodante por ruta. Mantiene marcas de tiempo monotonicas inyectadas
/// por el llamador; al reanudar tras suspension el engine descarta la ventana
/// si detecta un salto > 5 s (ver `watchdog`).
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct RollingWindow {
    pub route: RouteId,
    pub window_ms: u64,
    entries: VecDeque<(u64, ProbeSample)>,
    next_seq: u64,
}

impl RollingWindow {
    pub fn new(route: RouteId, window_ms: u64) -> Self {
        Self { route, window_ms, entries: VecDeque::new(), next_seq: 0 }
    }

    pub fn push(&mut self, at_ms: u64, rtt_ms: Option<f64>) {
        let seq = self.next_seq;
        self.next_seq += 1;
        self.entries.push_back((at_ms, ProbeSample { seq, rtt_ms }));
        self.prune(at_ms);
    }

    pub fn prune(&mut self, now_ms: u64) {
        let cutoff = now_ms.saturating_sub(self.window_ms);
        while self.entries.front().is_some_and(|(t, _)| *t < cutoff) {
            self.entries.pop_front();
        }
    }

    /// Descarta la ventana completa tras un cambio de red o suspension.
    pub fn invalidate(&mut self) {
        self.entries.clear();
    }

    pub fn len(&self) -> usize {
        self.entries.len()
    }

    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    pub fn metrics(&self, flaps_last_10m: usize) -> Option<WindowMetrics> {
        let samples: Vec<ProbeSample> = self.entries.iter().map(|(_, s)| *s).collect();
        WindowMetrics::from_samples(&samples, self.window_ms, flaps_last_10m)
    }
}

/// Un `tick` del scheduler: recibe los resultados de las sondas de cada ruta
/// y devuelve las metricas de la ventana. Es determinista y testeable sin red;
/// el bucle real (`thread::sleep(interval_ms)`) vive en `tramoctl`/el daemon.
#[derive(Clone, Debug)]
pub struct TickResult {
    pub route: RouteId,
    pub metrics: Option<WindowMetrics>,
    pub samples_in_window: usize,
}

pub fn tick(
    windows: &mut [RollingWindow],
    now_ms: u64,
    results: &[(RouteId, Option<f64>)],
    flaps_last_10m: usize,
) -> Vec<TickResult> {
    for (route, rtt) in results {
        if let Some(w) = windows.iter_mut().find(|w| w.route == *route) {
            w.push(now_ms, *rtt);
        }
    }
    windows
        .iter()
        .map(|w| TickResult {
            route: w.route.clone(),
            metrics: w.metrics(flaps_last_10m),
            samples_in_window: w.len(),
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn window_prunes_old_samples() {
        let mut w = RollingWindow::new(RouteId::direct(), 1_000);
        w.push(100, Some(10.0));
        w.push(500, Some(12.0));
        w.push(1_500, Some(14.0)); // expulsa la muestra de t=100
        assert_eq!(w.len(), 2);
        let m = w.metrics(0).unwrap();
        assert_eq!(m.samples, 2);
        assert_eq!(m.p50_ms, 14.0); // nearest-rank sobre [12,14]
    }

    #[test]
    fn invalidate_discards_window_after_network_change() {
        let mut w = RollingWindow::new(RouteId::direct(), 60_000);
        w.push(1, Some(10.0));
        w.invalidate();
        assert!(w.is_empty());
        assert!(w.metrics(0).is_none());
    }

    #[test]
    fn tick_updates_only_known_routes() {
        let mut windows = vec![
            RollingWindow::new(RouteId::direct(), 60_000),
            RollingWindow::new(RouteId::new("wg-1"), 60_000),
        ];
        let results = vec![
            (RouteId::direct(), Some(20.0)),
            (RouteId::new("wg-1"), None),
            (RouteId::new("desconocida"), Some(1.0)),
        ];
        let out = tick(&mut windows, 100, &results, 0);
        assert_eq!(out.len(), 2);
        assert!(out[0].metrics.is_some());
        assert!(out[1].metrics.is_none()); // unica sonda perdida => sin metricas
    }

    #[test]
    fn monotonic_clock_rollover_is_safe() {
        let mut w = RollingWindow::new(RouteId::direct(), 1_000);
        w.push(u64::MAX - 10, Some(10.0));
        w.push(u64::MAX, Some(12.0));
        assert_eq!(w.len(), 2);
    }
}
