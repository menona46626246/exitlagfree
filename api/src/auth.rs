// SPDX-License-Identifier: Apache-2.0
//! API key opcional. Fase 7: almacenar hash Argon2id en vez del valor en claro.
//! Ya se compara en tiempo constante para no filtrar por timing.

pub fn constant_time_eq(a: &str, b: &str) -> bool {
    let a = a.as_bytes();
    let b = b.as_bytes();
    let max = a.len().max(b.len());
    let mut diff = (a.len() ^ b.len()) as u8;
    for i in 0..max {
        let x = a.get(i).copied().unwrap_or(0);
        let y = b.get(i).copied().unwrap_or(0);
        diff |= x ^ y;
    }
    diff == 0
}

#[derive(Clone, Debug)]
pub struct ApiKey {
    expected: Option<String>,
}

impl ApiKey {
    pub fn from_env() -> Self {
        Self { expected: std::env::var("TRAMO_API_KEY").ok().filter(|s| !s.is_empty()) }
    }

    pub fn is_required(&self) -> bool {
        self.expected.is_some()
    }

    pub fn verify(&self, provided: Option<&str>) -> bool {
        match (&self.expected, provided) {
            (None, _) => true,
            (Some(_), None) => false,
            (Some(expected), Some(got)) => constant_time_eq(expected, got),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn constant_time_compare() {
        assert!(constant_time_eq("secreto", "secreto"));
        assert!(!constant_time_eq("secreto", "secretO"));
        assert!(!constant_time_eq("secreto", "secreto_extra"));
    }

    #[test]
    fn missing_key_is_rejected_only_when_required() {
        let open = ApiKey { expected: None };
        assert!(open.verify(None));
        let closed = ApiKey { expected: Some("k".into()) };
        assert!(!closed.verify(None));
        assert!(!closed.verify(Some("mal")));
        assert!(closed.verify(Some("k")));
    }
}
