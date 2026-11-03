// SPDX-License-Identifier: Apache-2.0
use std::collections::VecDeque;
use tramo_engine::EngineEvent;

/// Agregador de eventos hacia la UI: como maximo 4 eventos/s (250 ms).
/// Evita saturar el WebView y mantener bajo el uso de CPU (metrica de calidad).
#[derive(Clone, Debug)]
pub struct ThrottledEvents {
    min_interval_ms: u64,
    last_emit_ms: Option<u64>,
    pending: VecDeque<EngineEvent>,
}

impl Default for ThrottledEvents {
    fn default() -> Self {
        Self::new(250)
    }
}

impl ThrottledEvents {
    pub fn new(min_interval_ms: u64) -> Self {
        Self { min_interval_ms, last_emit_ms: None, pending: VecDeque::new() }
    }

    pub fn push(&mut self, event: EngineEvent, now_ms: u64) -> Option<EngineEvent> {
        self.pending.push_back(event);
        if self.last_emit_ms.is_some_and(|t| now_ms.saturating_sub(t) < self.min_interval_ms) {
            return None;
        }
        self.last_emit_ms = Some(now_ms);
        self.pending.pop_front()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ev(at: u64) -> EngineEvent {
        EngineEvent::Decision { route: "direct".into(), reason: "ok".into(), apply: false }
    }

    #[test]
    fn emits_at_most_one_event_per_interval() {
        let mut bus = ThrottledEvents::default();
        assert!(bus.push(ev(1), 100).is_some());
        assert!(bus.push(ev(2), 200).is_none());
        assert!(bus.push(ev(3), 349).is_none());
        assert_eq!(bus.pending.len(), 2);
        assert!(bus.push(ev(4), 350).is_some());
    }
}
