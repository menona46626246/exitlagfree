// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Unico conjunto de operaciones que el helper privilegiado acepta.
/// Cualquier otro comando falla en la deserializacion: no hay `exec`, `script`,
/// `hook`, rutas arbitrarias ni carga de codigo.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "cmd", rename_all = "snake_case")]
pub enum Command {
    Status,
    Preflight,
    ApplyRoute { route: String },
    Restore,
    SetKillSwitch { enabled: bool },
    Shutdown,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "status", rename_all = "snake_case")]
pub enum Reply {
    Ok { detail: String },
    Error { detail: String },
    Status {
        privileged: bool,
        active_route: String,
        kill_switch: bool,
        lease_valid: bool,
    },
}

impl Reply {
    pub fn ok(detail: impl Into<String>) -> Self {
        Self::Ok { detail: detail.into() }
    }

    pub fn err(detail: impl Into<String>) -> Self {
        Self::Error { detail: detail.into() }
    }
}

/// Frontera de seguridad del IPC: ademas del enum cerrado, se rechaza
/// explicitamente cualquier clave desconocida (defensa en profundidad frente a
/// `serde` ignorando campos por defecto).
pub fn command_from_slice(bytes: &[u8]) -> Result<Command, String> {
    let value: serde_json::Value = serde_json::from_slice(bytes).map_err(|e| e.to_string())?;
    let obj = value.as_object().ok_or_else(|| "el comando debe ser un objeto JSON".to_string())?;
    const ALLOWED: &[&str] = &["cmd", "route", "enabled"];
    for key in obj.keys() {
        if !ALLOWED.contains(&key.as_str()) {
            return Err(format!("campo no permitido en comando IPC: '{key}'"));
        }
    }
    serde_json::from_value::<Command>(value).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn commands_round_trip_with_closed_tag() {
        let c = Command::ApplyRoute { route: "wg-fra-1".into() };
        let json = serde_json::to_string(&c).unwrap();
        assert_eq!(json, r#"{"cmd":"apply_route","route":"wg-fra-1"}"#);
        assert_eq!(serde_json::from_str::<Command>(&json).unwrap(), c);
    }

    #[test]
    fn arbitrary_commands_are_rejected() {
        assert!(command_from_slice(br#"{"cmd":"exec","command":"whoami"}"#).is_err());
        assert!(command_from_slice(br#"{"cmd":"run_script","path":"/tmp/x"}"#).is_err());
        assert!(command_from_slice(br#"{"cmd":"apply_route","route":"x","hook":"y"}"#).is_err());
    }

    #[test]
    fn known_commands_with_only_allowed_fields_are_accepted() {
        assert_eq!(
            command_from_slice(br#"{"cmd":"set_kill_switch","enabled":true}"#).unwrap(),
            Command::SetKillSwitch { enabled: true }
        );
    }

    #[test]
    fn status_reply_contains_no_secrets() {
        let r = Reply::Status {
            privileged: true,
            active_route: "direct".into(),
            kill_switch: false,
            lease_valid: true,
        };
        let j = serde_json::to_string(&r).unwrap();
        assert!(!j.to_lowercase().contains("key"));
        assert!(j.contains("\"active_route\":\"direct\""));
    }
}
