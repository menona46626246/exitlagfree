// SPDX-License-Identifier: Apache-2.0
/// Detecta cambio de interfaz/red para invalidar ventanas y rehacer preflight.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct NetworkChangeDetector {
    previous: Option<String>,
}

impl NetworkChangeDetector {
    pub fn observe(&mut self, current: &str) -> bool {
        let changed = self.previous.as_deref().is_some_and(|p| p != current);
        self.previous = Some(current.to_string());
        changed
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_interface_change() {
        let mut d = NetworkChangeDetector::default();
        assert!(!d.observe("eth0"));
        assert!(!d.observe("eth0"));
        assert!(d.observe("wlan0"));
    }
}
