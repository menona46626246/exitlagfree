// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Garantia anti-"red rota": el helper mantiene un lease renovado por el motor.
/// Si el motor/UI muere, el helper revierte sus propias rutas y reglas al
/// expirar el lease (3 s por defecto). Nunca depende de un hook de cierre.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct Lease {
    pub id: String,
    pub last_heartbeat_ms: u64,
    pub ttl_ms: u64,
    pub active: bool,
}

impl Lease {
    pub fn new(id: impl Into<String>, now_ms: u64) -> Self {
        Self { id: id.into(), last_heartbeat_ms: now_ms, ttl_ms: 3_000, active: true }
    }

    pub fn heartbeat(&mut self, now_ms: u64) {
        self.last_heartbeat_ms = now_ms;
    }

    pub fn expired(&self, now_ms: u64) -> bool {
        self.active && now_ms.saturating_sub(self.last_heartbeat_ms) >= self.ttl_ms
    }

    pub fn release(&mut self) {
        self.active = false;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lease_expires_without_heartbeat() {
        let mut l = Lease::new("sesion-1", 1_000);
        assert!(!l.expired(2_000));
        assert!(l.expired(4_000));
        l.heartbeat(3_500);
        assert!(!l.expired(4_000));
    }

    #[test]
    fn released_lease_never_expires_and_clock_rollback_is_safe() {
        let mut l = Lease::new("s", 10_000);
        assert!(!l.expired(1_000)); // reloj hacia atras
        l.release();
        assert!(!l.expired(100_000));
    }
}
