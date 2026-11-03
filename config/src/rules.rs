// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};
use std::net::Ipv4Addr;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Protocol {
    Udp,
    Tcp,
    Both,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum RuleKind {
    /// CIDR IPv4 destino. Base del split tunneling portable.
    Cidr,
    /// Puerto destino (1..=65535).
    Port,
    /// Regla por protocolo.
    Protocol,
    /// Solo Linux, mediante network namespace; en Windows se marca como no soportada.
    Executable,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct Rule {
    pub kind: RuleKind,
    /// CIDR, numero de puerto, "udp"/"tcp"/"both" o nombre de ejecutable.
    pub value: String,
    #[serde(default)]
    pub protocol: Option<Protocol>,
    #[serde(default)]
    pub comment: Option<String>,
}

impl Rule {
    pub fn validate(&self) -> Result<(), String> {
        match self.kind {
            RuleKind::Cidr => parse_ipv4_cidr(&self.value)
                .map(|_| ())
                .map_err(|e| format!("CIDR invalido '{}': {e}", self.value)),
            RuleKind::Port => self
                .value
                .parse::<u16>()
                .map(|_| ())
                .map_err(|_| format!("puerto invalido '{}' (rango 1..=65535)", self.value)),
            RuleKind::Protocol => match self.value.to_ascii_lowercase().as_str() {
                "udp" | "tcp" | "both" => Ok(()),
                _ => Err(format!("protocolo invalido '{}'", self.value)),
            },
            RuleKind::Executable => {
                if self.value.is_empty() || self.value.len() > 260 {
                    return Err("nombre de ejecutable vacio o demasiado largo".into());
                }
                // Nunca se aceptan rutas arbitrarias ni argumentos: el JSON de
                // configuracion no es un mecanismo de ejecucion de comandos.
                if self.value.contains('/') || self.value.contains('\\') || self.value.contains(' ')
                {
                    return Err(
                        "executable debe ser solo un nombre de archivo, sin rutas ni argumentos".into(),
                    );
                }
                Ok(())
            }
        }
    }
}

/// Parser minimo de CIDR IPv4 sin dependencias externas.
/// Devuelve (direccion de red, prefijo).
pub fn parse_ipv4_cidr(s: &str) -> Result<(Ipv4Addr, u8), String> {
    let (addr, prefix) = s
        .split_once('/')
        .ok_or_else(|| "se espera formato A.B.C.D/N".to_string())?;
    let ip: Ipv4Addr = addr.parse().map_err(|_| "direccion IPv4 invalida".to_string())?;
    let bits: u8 = prefix.parse().map_err(|_| "prefijo invalido".to_string())?;
    if bits > 32 {
        return Err("prefijo fuera de rango 0..=32".into());
    }
    let mask = if bits == 0 { 0 } else { u32::MAX << (32 - bits) };
    let net = u32::from(ip) & mask;
    Ok((Ipv4Addr::from(net), bits))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cidr_parser_normalises_host_bits() {
        assert_eq!(parse_ipv4_cidr("192.168.1.7/24").unwrap(), (Ipv4Addr::new(192, 168, 1, 0), 24));
        assert_eq!(parse_ipv4_cidr("0.0.0.0/0").unwrap(), (Ipv4Addr::new(0, 0, 0, 0), 0));
    }

    #[test]
    fn cidr_parser_rejects_bad_input() {
        assert!(parse_ipv4_cidr("192.168.1.1").is_err());
        assert!(parse_ipv4_cidr("999.1.1.1/24").is_err());
        assert!(parse_ipv4_cidr("10.0.0.0/33").is_err());
    }

    #[test]
    fn rule_validation_rejects_paths_and_bad_ports() {
        assert!(Rule { kind: RuleKind::Port, value: "70000".into(), protocol: None, comment: None }.validate().is_err());
        assert!(Rule { kind: RuleKind::Port, value: "27015".into(), protocol: None, comment: None }.validate().is_ok());
        assert!(Rule { kind: RuleKind::Executable, value: "../../bin/sh -c evil".into(), protocol: None, comment: None }.validate().is_err());
        assert!(Rule { kind: RuleKind::Executable, value: "game.exe".into(), protocol: None, comment: None }.validate().is_ok());
    }
}
