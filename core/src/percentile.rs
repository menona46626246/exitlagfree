// SPDX-License-Identifier: Apache-2.0
//! Histograma de bins fijos de 1 ms (equivalente a un HDR de rango acotado).
//! Se cuantiza con `round()` y se satura en `MAX_RTT_MS`; los percentiles usan
//! *nearest-rank* sobre el histograma acumulado, no interpolacion, para que el
//! resultado sea identico en Rust, en el oraculo Python del laboratorio y en la UI.

pub const MAX_RTT_MS: usize = 2_000;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FixedHistogram {
    bins: Vec<u32>,
}

impl Default for FixedHistogram {
    fn default() -> Self {
        Self::new()
    }
}

impl FixedHistogram {
    pub fn new() -> Self {
        Self { bins: vec![0; MAX_RTT_MS + 1] }
    }

    pub fn record(&mut self, rtt_ms: f64) {
        if !rtt_ms.is_finite() || rtt_ms < 0.0 {
            return;
        }
        let idx = (rtt_ms.round() as usize).min(MAX_RTT_MS);
        self.bins[idx] = self.bins[idx].saturating_add(1);
    }

    pub fn count(&self) -> u64 {
        self.bins.iter().map(|&n| n as u64).sum()
    }

    /// `p` en [0, 100]. Devuelve `None` si no hay muestras.
    /// Nearest-rank: se busca el primer bin cuyo acumulado alcanza `ceil(p*n/100)`.
    pub fn percentile(&self, p: f64) -> Option<f64> {
        let total = self.count();
        if total == 0 {
            return None;
        }
        let p = p.clamp(0.0, 100.0);
        let rank = ((p / 100.0) * total as f64).ceil().max(1.0) as u64;
        let mut acc: u64 = 0;
        for (idx, &n) in self.bins.iter().enumerate() {
            acc += n as u64;
            if acc >= rank {
                return Some(idx as f64);
            }
        }
        Some(MAX_RTT_MS as f64)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_histogram_has_no_percentile() {
        assert_eq!(FixedHistogram::new().percentile(50.0), None);
    }

    #[test]
    fn nearest_rank_percentiles() {
        let mut h = FixedHistogram::new();
        for rtt in [10.0, 10.0, 20.0, 30.0, 100.0] {
            h.record(rtt);
        }
        assert_eq!(h.count(), 5);
        assert_eq!(h.percentile(50.0), Some(20.0));
        assert_eq!(h.percentile(95.0), Some(100.0));
        assert_eq!(h.percentile(99.0), Some(100.0));
        assert_eq!(h.percentile(0.0), Some(10.0));
    }

    #[test]
    fn quantisation_and_saturation() {
        let mut h = FixedHistogram::new();
        h.record(10.4); // -> 10
        h.record(10.6); // -> 11
        h.record(-1.0);
        h.record(f64::NAN);
        h.record(9_999.0); // -> saturado en 2000
        assert_eq!(h.count(), 3);
        assert_eq!(h.percentile(50.0), Some(11.0));
        assert_eq!(h.percentile(100.0), Some(MAX_RTT_MS as f64));
    }
}
