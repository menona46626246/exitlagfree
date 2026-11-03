// SPDX-License-Identifier: Apache-2.0
//! Claves WireGuard: 32 bytes en base64 (44 caracteres, termina en '=').
//! La generacion la realiza `scripts/gen-keys.sh` con `wg genkey | wg pubkey`
//! de forma local; este modulo solo valida forma y redacta para logs.

pub fn key_shape_is_valid(key: &str) -> bool {
    let k = key.trim();
    k.len() == 44
        && k.ends_with('=')
        && k.chars().all(|c| c.is_ascii_alphanumeric() || c == '+' || c == '/' || c == '=')
}

pub fn redact_key(key: &str) -> String {
    if key.len() <= 8 {
        return "[redacted]".into();
    }
    format!("{}…[redacted]", &key[..4])
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shape_validation_and_redaction() {
        let k = "A".repeat(43) + "=";
        assert!(key_shape_is_valid(&k));
        assert!(!key_shape_is_valid("corto"));
        assert!(redact_key(&k).contains("redacted"));
        assert!(!redact_key(&k).contains(&k));
    }
}
