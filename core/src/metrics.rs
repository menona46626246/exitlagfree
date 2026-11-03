// SPDX-License-Identifier: Apache-2.0
use crate::model::ProbeSample;
use crate::percentile::FixedHistogram;
use serde::{Deserialize, Serialize};

/// Metricas de una ventana de medicion de una unica ruta.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct WindowMetrics {
    pub samples: usize,
    pub received: usize,
    pub loss: f64,
    pub p50_ms: f64,
    pub p95_ms: f64,
    pub p99_ms: f64,
    /// Media de |RTT[i] - RTT[i-1]| sobre muestras recibidas consecutivas.
    pub jitter_ms: f64,
    /// Variante RFC 3550: J += (|D| - J)/16.
    pub jitter_rfc3550_ms: f64,
    pub tail_ms: f64,
    /// Timeouts consecutivos al final de la ventana (para failover duro).
    pub consecutive_timeouts: usize,
    pub window_ms: u64,
    /// [0,1]: mezcla de jitter normalizado y flaps propios en 10 min.
    pub instability: f64,
}

impl WindowMetrics {
    /// Devuelve `None` si la ventana esta vacia o si no llego ninguna sonda:
    /// una ruta sin respuestas no tiene p50 y se trata como caida dura, nunca
    /// como "latencia cero" (evita que un nodo muerto gane el scoring).
    pub fn from_samples(
        samples: &[ProbeSample],
        window_ms: u64,
        flaps_last_10m: usize,
    ) -> Option<Self> {
        if samples.is_empty() {
            return None;
        }
        let mut hist = FixedHistogram::new();
        let mut received = 0usize;
        let mut last: Option<f64> = None;
        let mut delta_sum = 0.0;
        let mut delta_count = 0usize;
        let mut rfc_j = 0.0;
        let mut rfc_started = false;

        for s in samples {
            match s.rtt_ms {
                Some(rtt) => {
                    hist.record(rtt);
                    received += 1;
                    if let Some(prev) = last {
                        let d = rtt - prev;
                        delta_sum += d.abs();
                        delta_count += 1;
                        if !rfc_started {
                            rfc_j = d.abs() / 16.0;
                            rfc_started = true;
                        } else {
                            rfc_j += (d.abs() - rfc_j) / 16.0;
                        }
                    }
                    last = Some(rtt);
                }
                None => {
                    // Una perdida no rompe la serie de deltas: se ignora para jitter
                    // (el siguiente RTT se compara con el ultimo recibido).
                }
            }
        }
        if received == 0 {
            return None;
        }

        let p50 = hist.percentile(50.0).unwrap();
        let p95 = hist.percentile(95.0).unwrap();
        let p99 = hist.percentile(99.0).unwrap();
        let jitter = if delta_count == 0 { 0.0 } else { delta_sum / delta_count as f64 };
        let instability = (0.6 * (jitter / 20.0) + 0.4 * (flaps_last_10m as f64 / 5.0))
            .clamp(0.0, 1.0);

        let mut trailing_timeouts = 0usize;
        for s in samples.iter().rev() {
            if s.rtt_ms.is_none() {
                trailing_timeouts += 1;
            } else {
                break;
            }
        }

        Some(Self {
            samples: samples.len(),
            received,
            loss: 1.0 - (received as f64 / samples.len() as f64),
            p50_ms: p50,
            p95_ms: p95,
            p99_ms: p99,
            jitter_ms: jitter,
            jitter_rfc3550_ms: rfc_j,
            tail_ms: p95 - p50,
            consecutive_timeouts: trailing_timeouts,
            window_ms,
            instability,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ok(seq: u64, rtt: f64) -> ProbeSample {
        ProbeSample::ok(seq, rtt)
    }

    #[test]
    fn empty_and_all_lost_windows_have_no_metrics() {
        assert!(WindowMetrics::from_samples(&[], 60_000, 0).is_none());
        let lost = vec![ProbeSample::lost(1), ProbeSample::lost(2)];
        assert!(WindowMetrics::from_samples(&lost, 60_000, 0).is_none());
    }

    #[test]
    fn percentiles_jitter_and_tail() {
        let s = vec![ok(1, 10.0), ok(2, 10.0), ok(3, 20.0), ok(4, 30.0), ok(5, 100.0)];
        let m = WindowMetrics::from_samples(&s, 60_000, 0).unwrap();
        assert_eq!(m.p50_ms, 20.0);
        assert_eq!(m.p95_ms, 100.0);
        assert_eq!(m.tail_ms, 80.0);
        assert_eq!(m.jitter_ms, 22.5); // (0+10+10+70)/4
        assert_eq!(m.loss, 0.0);
    }

    #[test]
    fn loss_is_computed_with_sequence_gaps() {
        let s = vec![
            ok(1, 12.0),
            ProbeSample::lost(2),
            ok(3, 14.0),
            ProbeSample::lost(4),
            ok(5, 13.0),
        ];
        let m = WindowMetrics::from_samples(&s, 60_000, 0).unwrap();
        assert!((m.loss - 0.4).abs() < 1e-9);
        assert_eq!(m.samples, 5);
        assert_eq!(m.received, 3);
        assert_eq!(m.consecutive_timeouts, 0);
    }

    #[test]
    fn trailing_timeouts_drive_hard_failover() {
        let s = vec![ok(1, 10.0), ok(2, 11.0), ProbeSample::lost(3), ProbeSample::lost(4)];
        let m = WindowMetrics::from_samples(&s, 2_000, 0).unwrap();
        assert_eq!(m.consecutive_timeouts, 2);
        assert!((m.loss - 0.5).abs() < 1e-9);
    }

    #[test]
    fn jitter_rfc3550_is_smoothed() {
        let s = vec![ok(1, 10.0), ok(2, 14.0), ok(3, 12.0)];
        let m = WindowMetrics::from_samples(&s, 60_000, 0).unwrap();
        // J = 4/16 = 0.25 ; luego 0.25 + (2 - 0.25)/16 = 0.359375
        assert!((m.jitter_rfc3550_ms - 0.359375).abs() < 1e-9);
    }

    #[test]
    fn instability_is_clamped_and_depends_on_flaps() {
        let s = vec![ok(1, 10.0), ok(2, 30.0), ok(3, 10.0)];
        let stable = WindowMetrics::from_samples(&s, 60_000, 0).unwrap();
        let flappy = WindowMetrics::from_samples(&s, 60_000, 9).unwrap();
        assert!(flappy.instability > stable.instability);
        assert_eq!(flappy.instability, 1.0);
    }
}
