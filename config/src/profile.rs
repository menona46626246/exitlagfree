// SPDX-License-Identifier: Apache-2.0
use crate::rules::Rule;
use serde::{Deserialize, Serialize};

/// Perfil por juego. `destination` es configurable por el usuario; el producto
/// no mantiene ni distribuye listas de servidores de terceros.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct GameProfile {
    pub id: String,
    pub name: String,
    #[serde(default)]
    pub enabled: bool,
    /// "host:port" o IP literal. Vacio => solo medicion general.
    #[serde(default)]
    pub destination: Option<String>,
    #[serde(default)]
    pub rules: Vec<Rule>,
    /// Orden de preferencia de nodos (ids). 0 = preferido en el scoring.
    #[serde(default)]
    pub node_priority: Vec<String>,
    /// Pide confirmacion explicita por si el juego tiene politicas anti-VPN.
    #[serde(default)]
    pub requires_ack: bool,
}

impl GameProfile {
    pub fn validate(&self) -> Result<(), String> {
        if self.id.trim().is_empty() || self.id.len() > 64 {
            return Err("id de perfil vacio o demasiado largo".into());
        }
        if !self.id.chars().all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_') {
            return Err("id de perfil solo admite [A-Za-z0-9_-]".into());
        }
        if let Some(dest) = &self.destination {
            validate_destination(dest)?;
        }
        for (i, rule) in self.rules.iter().enumerate() {
            rule.validate().map_err(|e| format!("regla {i}: {e}"))?;
        }
        Ok(())
    }
}

/// Valida "host:port" sin resolver DNS (la resolucion se hace en `probe`, con
/// reintentos y sin filtrar informacion a terceros).
pub fn validate_destination(dest: &str) -> Result<(), String> {
    if dest.is_empty() || dest.len() > 255 {
        return Err("destino vacio o demasiado largo".into());
    }
    let (host, port) = dest
        .rsplit_once(':')
        .ok_or_else(|| "se espera formato host:puerto".to_string())?;
    if host.is_empty() || host.contains(' ') {
        return Err("host invalido".into());
    }
    let port: u16 = port
        .parse()
        .map_err(|_| "puerto invalido (1..=65535)".to_string())?;
    if port == 0 {
        return Err("el puerto 0 no esta permitido".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::rules::{Rule, RuleKind};

    #[test]
    fn destination_validation() {
        assert!(validate_destination("203.0.113.9:27015").is_ok());
        assert!(validate_destination("game.example.net:27015").is_ok());
        assert!(validate_destination("203.0.113.9").is_err());
        assert!(validate_destination("host:0").is_err());
        assert!(validate_destination("host:99999").is_err());
    }

    #[test]
    fn profile_validates_nested_rules() {
        let p = GameProfile {
            id: "mi-juego".into(),
            name: "Mi juego".into(),
            enabled: true,
            destination: Some("203.0.113.9:27015".into()),
            rules: vec![Rule {
                kind: RuleKind::Cidr,
                value: "203.0.113.0/24".into(),
                protocol: None,
                comment: None,
            }],
            node_priority: vec!["wg-fra-1".into()],
            requires_ack: false,
        };
        assert!(p.validate().is_ok());
        let bad = GameProfile { id: "../evil".into(), ..p };
        assert!(bad.validate().is_err());
    }
}
