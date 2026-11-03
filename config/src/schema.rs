// SPDX-License-Identifier: Apache-2.0
use crate::profile::GameProfile;
use serde::{Deserialize, Serialize};
use tramo_core::{HysteresisConfig, Mode, ScoreWeights};

pub const CONFIG_VERSION: u32 = 1;

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ProbeProtocol {
    Udp,
    Tcp,
    Icmp,
    Auto,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct ProbeSettings {
    /// Intervalo entre sondas (ms). 200 por defecto.
    pub interval_ms: u64,
    pub timeout_ms: u64,
    /// Ventana de calculo (ms). 60_000 por defecto.
    pub window_ms: u64,
    pub protocol: ProbeProtocol,
    #[serde(default)]
    pub target: Option<String>,
}

impl Default for ProbeSettings {
    fn default() -> Self {
        Self {
            interval_ms: 200,
            timeout_ms: 1_000,
            window_ms: 60_000,
            protocol: ProbeProtocol::Auto,
            target: None,
        }
    }
}

impl ProbeSettings {
    pub fn validate(&self) -> Result<(), String> {
        if !(50..=10_000).contains(&self.interval_ms) {
            return Err("interval_ms debe estar en [50, 10000]".into());
        }
        if self.timeout_ms < self.interval_ms || self.timeout_ms > 30_000 {
            return Err("timeout_ms debe ser >= interval_ms y <= 30000".into());
        }
        if !(10_000..=600_000).contains(&self.window_ms) {
            return Err("window_ms debe estar en [10000, 600000]".into());
        }
        if let Some(t) = &self.target {
            crate::profile::validate_destination(t)?;
        }
        Ok(())
    }
}

/// Metadatos de un nodo. `private_key` NUNCA se exporta por defecto ni se envia
/// a la API: solo se usa localmente para configurar el tunel.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct NodeEntry {
    pub id: String,
    pub name: String,
    /// "host:port" del endpoint WireGuard (UDP).
    pub endpoint: String,
    pub public_key: String,
    /// Etiqueta estatica definida por el operador. No se geolocaliza al usuario.
    #[serde(default)]
    pub region_label: Option<String>,
    #[serde(default)]
    pub priority: u32,
    #[serde(default)]
    pub geo_cost_ms: f64,
    #[serde(default = "default_true")]
    pub enabled: bool,
    /// Solo local, omitida en exportaciones normales.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub private_key: Option<String>,
}

fn default_true() -> bool {
    true
}

/// Valida la forma de una clave publica WireGuard (base64 de 32 bytes).
/// No verifica criptografia: solo evita errores de configuracion evidentes.
pub fn node_key_is_well_formed(key: &str) -> bool {
    crate::secrets::key_encoding_is_well_formed(key)
}

/// Telemetria: DESACTIVADA por defecto. Si se activa, exige consentimiento
/// explicito con marca de tiempo y solo metricas agregadas sin identificadores.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct TelemetryConfig {
    #[serde(default)]
    pub enabled: bool,
    #[serde(default)]
    pub consent_given_at: Option<String>,
    /// Vista previa obligatoria del payload antes de cualquier envio.
    #[serde(default = "default_true")]
    pub preview_before_send: bool,
}

impl Default for TelemetryConfig {
    fn default() -> Self {
        Self { enabled: false, consent_given_at: None, preview_before_send: true }
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct LoggingConfig {
    /// Logs locales DESACTIVADOS por defecto.
    #[serde(default)]
    pub enabled: bool,
    /// Redaccion de IP: none | truncate | hash
    #[serde(default = "default_redaction")]
    pub ip_redaction: String,
    #[serde(default)]
    pub max_days: u32,
}

fn default_redaction() -> String {
    "truncate".into()
}

impl Default for LoggingConfig {
    fn default() -> Self {
        Self { enabled: false, ip_redaction: default_redaction(), max_days: 7 }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Config {
    pub version: u32,
    #[serde(default)]
    pub mode: Mode,
    #[serde(default)]
    pub weights: ScoreWeights,
    #[serde(default)]
    pub hysteresis: HysteresisConfig,
    #[serde(default)]
    pub probe: ProbeSettings,
    #[serde(default)]
    pub profiles: Vec<GameProfile>,
    #[serde(default)]
    pub nodes: Vec<NodeEntry>,
    #[serde(default)]
    pub logging: LoggingConfig,
    #[serde(default)]
    pub telemetry: TelemetryConfig,
    /// Politica ante caida del tunel: revert_direct (por defecto) | block.
    #[serde(default = "default_failure_policy")]
    pub on_failure: String,
    /// Kill switch opcional, DESACTIVADO por defecto.
    #[serde(default)]
    pub kill_switch: bool,
}

fn default_failure_policy() -> String {
    "revert_direct".into()
}

impl Default for Config {
    fn default() -> Self {
        Self {
            version: CONFIG_VERSION,
            mode: Mode::default(),
            weights: ScoreWeights::default(),
            hysteresis: HysteresisConfig::default(),
            probe: ProbeSettings::default(),
            profiles: Vec::new(),
            nodes: Vec::new(),
            logging: LoggingConfig::default(),
            telemetry: TelemetryConfig::default(),
            on_failure: default_failure_policy(),
            kill_switch: false,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_config_is_private_and_measure_only() {
        let c = Config::default();
        assert_eq!(c.version, 1);
        assert_eq!(c.mode, Mode::MeasureOnly);
        assert!(!c.telemetry.enabled);
        assert!(!c.logging.enabled);
        assert!(!c.kill_switch);
        assert_eq!(c.on_failure, "revert_direct");
    }

    #[test]
    fn probe_settings_validation() {
        let mut p = ProbeSettings::default();
        assert!(p.validate().is_ok());
        p.timeout_ms = 10;
        assert!(p.validate().is_err());
        p.timeout_ms = 1_000;
        p.target = Some("sin-puerto".into());
        assert!(p.validate().is_err());
    }
}
