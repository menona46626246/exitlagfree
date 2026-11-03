// SPDX-License-Identifier: Apache-2.0
/// Vigila la frescura del handshake WireGuard y los saltos de reloj monotónico
/// propios de suspension/hibernacion o cambio de red.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HandshakeWatchdog {
    pub last_handshake_ms: u64,
    pub stale_after_ms: u64,
    pub suspend_gap_ms: u64,
}

impl Default for HandshakeWatchdog {
    fn default() -> Self {
        Self { last_handshake_ms: 0, stale_after_ms: 30_000, suspend_gap_ms: 5_000 }
    }
}

impl HandshakeWatchdog {
    pub fn observe(&mut self, now_ms: u64) {
        self.last_handshake_ms = now_ms;
    }

    pub fn age_ms(&self, now_ms: u64) -> u64 {
        now_ms.saturating_sub(self.last_handshake_ms)
    }

    pub fn is_stale(&self, now_ms: u64) -> bool {
        self.last_handshake_ms == 0 || self.age_ms(now_ms) > self.stale_after_ms
    }

    /// Un salto de tiempo mayor al umbral indica suspension/cambio de red:
    /// el engine debe descartar la ventana y rehacer preflight, en lugar de
    /// contabilizar decenas de timeouts falsos.
    pub fn is_suspension_gap(&self, previous_ms: u64, now_ms: u64) -> bool {
        now_ms.saturating_sub(previous_ms) > self.suspend_gap_ms
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn never_observed_handshake_is_stale() {
        assert!(HandshakeWatchdog::default().is_stale(1_000));
    }

    #[test]
    fn stale_after_threshold() {
        let mut w = HandshakeWatchdog::default();
        w.observe(10_000);
        assert!(!w.is_stale(20_000));
        assert!(w.is_stale(41_000));
        assert_eq!(w.age_ms(20_000), 10_000);
    }

    #[test]
    fn suspension_gap_detection() {
        let w = HandshakeWatchdog::default();
        assert!(w.is_suspension_gap(1_000, 100_000));
        assert!(!w.is_suspension_gap(1_000, 1_200));
        assert!(!w.is_suspension_gap(10_000, 1_000)); // reloj hacia atras
    }
}
