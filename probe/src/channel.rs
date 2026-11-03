// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};

/// Canal de medicion. Es distinto del plano de datos del juego: permite comparar
/// directa y tunel simultaneamente sin contaminar las medidas.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ProbeChannel {
    Direct,
    Tunnel,
}

/// Instruccion declarativa para `tramo-netctl` (NO ejecuta nada por si misma).
/// Linux: `SO_MARK` en el socket de sonda + `ip rule fwmark <M> lookup main`
/// para que la sonda directa esquive la tabla del tunel.
/// Windows: bind a la IP de la interfaz fisica; limitacion conocida y mostrada
/// en la UI cuando no se puede garantizar la exclusion.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct BypassHint {
    pub channel: ProbeChannel,
    pub fwmark: Option<u32>,
    pub bind_ip: Option<String>,
    #[serde(default)]
    pub reliable: bool,
}

impl BypassHint {
    pub fn direct_linux(fwmark: u32) -> Self {
        Self { channel: ProbeChannel::Direct, fwmark: Some(fwmark), bind_ip: None, reliable: true }
    }

    pub fn direct_windows(bind_ip: &str) -> Self {
        Self {
            channel: ProbeChannel::Direct,
            fwmark: None,
            bind_ip: Some(bind_ip.to_string()),
            // Wintun puede capturar el trafico aunque se fije el origen.
            reliable: false,
        }
    }

    pub fn tunnel() -> Self {
        Self { channel: ProbeChannel::Tunnel, fwmark: None, bind_ip: None, reliable: true }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn windows_direct_bypass_is_flagged_as_unreliable() {
        let h = BypassHint::direct_windows("192.168.1.20");
        assert!(!h.reliable);
        assert_eq!(h.channel, ProbeChannel::Direct);
    }

    #[test]
    fn linux_direct_bypass_has_fwmark() {
        let h = BypassHint::direct_linux(0xca6d);
        assert!(h.reliable);
        assert_eq!(h.fwmark, Some(0xca6d));
    }
}
