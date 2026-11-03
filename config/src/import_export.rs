// SPDX-License-Identifier: Apache-2.0
use crate::schema::{Config, CONFIG_VERSION};
use serde_json::Value;

/// Validacion completa antes de aplicar CUALQUIER configuracion importada.
/// Nunca se ejecuta codigo, rutas a archivos ni comandos contenidos en el JSON.
pub fn validate_config(cfg: &Config) -> Result<(), Vec<String>> {
    let mut errors = Vec::new();
    if cfg.version != CONFIG_VERSION {
        errors.push(format!(
            "version {} no soportada (esperada {}); usa una migracion",
            cfg.version, CONFIG_VERSION
        ));
    }
    if let Err(e) = cfg.weights.validate() {
        errors.push(e);
    }
    if let Err(e) = cfg.hysteresis.validate() {
        errors.push(e);
    }
    if let Err(e) = cfg.probe.validate() {
        errors.push(format!("probe: {e}"));
    }
    if cfg.on_failure != "revert_direct" && cfg.on_failure != "block" {
        errors.push("on_failure debe ser revert_direct o block".into());
    }
    if cfg.telemetry.enabled && cfg.telemetry.consent_given_at.is_none() {
        errors.push("telemetria activada sin consent_given_at explicito".into());
    }
    if cfg.telemetry.enabled && !cfg.telemetry.preview_before_send {
        errors.push("telemetria exige preview_before_send = true".into());
    }
    if cfg.logging.enabled && !matches!(cfg.logging.ip_redaction.as_str(), "none" | "truncate" | "hash") {
        errors.push("ip_redaction debe ser none, truncate o hash".into());
    }
    let mut ids = std::collections::HashSet::new();
    for p in &cfg.profiles {
        if let Err(e) = p.validate() {
            errors.push(format!("perfil '{}': {e}", p.id));
        }
        if !ids.insert(p.id.clone()) {
            errors.push(format!("id de perfil duplicado: {}", p.id));
        }
    }
    for n in &cfg.nodes {
        if n.id.trim().is_empty() || n.id == "direct" {
            errors.push("id de nodo vacio o reservado ('direct')".into());
        }
        if let Err(e) = crate::profile::validate_destination(&n.endpoint) {
            errors.push(format!("nodo '{}': {e}", n.id));
        }
        if !crate::schema::node_key_is_well_formed(&n.public_key) {
            errors.push(format!("nodo '{}': public_key no parece base64 de 32 bytes", n.id));
        }
    }
    if errors.is_empty() { Ok(()) } else { Err(errors) }
}

pub fn import_config(json: &str) -> Result<Config, String> {
    let value: Value = serde_json::from_str(json).map_err(|e| format!("JSON invalido: {e}"))?;
    if let Some(obj) = value.as_object() {
        // Defensa extra: rechazar campos que puedan interpretarse como ejecucion.
        for forbidden in ["hook", "script", "command", "on_up", "on_down", "exec"] {
            if obj.contains_key(forbidden) {
                return Err(format!("campo no permitido en configuracion: '{forbidden}'"));
            }
        }
    }
    let cfg: Config = serde_json::from_value(value).map_err(|e| format!("esquema invalido: {e}"))?;
    validate_config(&cfg).map_err(|errs| errs.join("; "))?;
    Ok(cfg)
}

/// Exporta SIEMPRE sin claves privadas, salvo `--include-secrets`, que la CLI
/// advierte explicitamente. El resultado es deterministico y legible.
pub fn export_config(cfg: &Config, include_secrets: bool) -> Result<String, String> {
    validate_config(cfg).map_err(|e| e.join("; "))?;
    let mut out = cfg.clone();
    if !include_secrets {
        for n in &mut out.nodes {
            n.private_key = None;
        }
    }
    serde_json::to_string_pretty(&out).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::schema::{NodeEntry, ProbeSettings};

    fn sample() -> Config {
        Config {
            nodes: vec![NodeEntry {
                id: "wg-fra-1".into(),
                name: "Frankfurt".into(),
                endpoint: "198.51.100.10:51820".into(),
                public_key: "A".repeat(43) + "=",
                region_label: Some("eu-central".into()),
                priority: 0,
                geo_cost_ms: 0.0,
                enabled: true,
                private_key: Some("CLAVE_PRIVADA_QUE_NO_DEBE_EXPORTARSE".into()),
            }],
            ..Config::default()
        }
    }

    #[test]
    fn export_strips_private_keys_by_default() {
        let json = export_config(&sample(), false).unwrap();
        assert!(!json.contains("CLAVE_PRIVADA"));
        assert!(!json.contains("private_key"));
        let json = export_config(&sample(), true).unwrap();
        assert!(json.contains("CLAVE_PRIVADA"));
    }

    #[test]
    fn import_rejects_execution_shaped_fields() {
        let json = r#"{"version":1,"command":"curl http://evil"}"#;
        assert!(import_config(json).is_err());
    }

    #[test]
    fn import_rejects_telemetry_without_consent() {
        let mut cfg = sample();
        cfg.telemetry.enabled = true;
        let json = serde_json::to_string(&cfg).unwrap();
        let err = import_config(&json).unwrap_err();
        assert!(err.contains("consent_given_at"));
    }

    #[test]
    fn import_rejects_unsupported_version_and_bad_probe() {
        let mut cfg = sample();
        cfg.version = 99;
        assert!(import_config(&serde_json::to_string(&cfg).unwrap()).is_err());
        cfg.version = 1;
        cfg.probe = ProbeSettings { timeout_ms: 1, ..ProbeSettings::default() };
        assert!(import_config(&serde_json::to_string(&cfg).unwrap()).is_err());
    }

    #[test]
    fn round_trip_keeps_effective_config() {
        let cfg = sample();
        let imported = import_config(&export_config(&cfg, false).unwrap()).unwrap();
        assert_eq!(imported.mode, cfg.mode);
        assert_eq!(imported.weights, cfg.weights);
        assert_eq!(imported.nodes[0].id, "wg-fra-1");
        assert!(imported.nodes[0].private_key.is_none());
    }
}
