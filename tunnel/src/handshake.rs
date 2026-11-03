// SPDX-License-Identifier: Apache-2.0
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HandshakeState {
    pub last_handshake_ms: u64,
    pub stale_after_ms: u64,
}

impl Default for HandshakeState {
    fn default() -> Self {
        Self { last_handshake_ms: 0, stale_after_ms: 30_000 }
    }
}

impl HandshakeState {
    pub fn observe(&mut self, now_ms: u64) {
        self.last_handshake_ms = now_ms;
    }
    pub fn is_stale(&self, now_ms: u64) -> bool {
        self.last_handshake_ms == 0 || now_ms.saturating_sub(self.last_handshake_ms) > self.stale_after_ms
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn stale_handshake_is_detected() {
        let mut h = HandshakeState::default();
        assert!(h.is_stale(1_000));
        h.observe(1_000);
        assert!(!h.is_stale(20_000));
        assert!(h.is_stale(31_001));
    }
}
