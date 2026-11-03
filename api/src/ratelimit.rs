// SPDX-License-Identifier: Apache-2.0
use std::collections::HashMap;

/// Ventana deslizante por IP. El tiempo es monotonico inyectado (`Instant`),
/// no hora de pared. Por defecto 60 peticiones / minuto / IP.
#[derive(Clone, Debug)]
pub struct RateLimiter {
    max: usize,
    window_ms: u64,
    hits: HashMap<String, Vec<u64>>,
}

impl Default for RateLimiter {
    fn default() -> Self {
        Self { max: 60, window_ms: 60_000, hits: HashMap::new() }
    }
}

impl RateLimiter {
    pub fn new(max: usize, window_ms: u64) -> Self {
        Self { max, window_ms, hits: HashMap::new() }
    }

    pub fn allow(&mut self, key: &str, now_ms: u64) -> bool {
        let entry = self.hits.entry(key.to_string()).or_default();
        entry.retain(|t| now_ms.saturating_sub(*t) < self.window_ms);
        if entry.len() >= self.max {
            return false;
        }
        entry.push(now_ms);
        true
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn blocks_after_limit_and_recovers_after_window() {
        let mut r = RateLimiter::new(2, 1_000);
        assert!(r.allow("1.2.3.4", 100));
        assert!(r.allow("1.2.3.4", 200));
        assert!(!r.allow("1.2.3.4", 300));
        assert!(r.allow("5.6.7.8", 300), "el limite es por IP");
        assert!(r.allow("1.2.3.4", 1_300));
    }
}
