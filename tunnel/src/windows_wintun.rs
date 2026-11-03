// SPDX-License-Identifier: Apache-2.0
use crate::backend::{TunnelBackend, TunnelConfig, TunnelError, TunnelStatus};

/// Windows: Wintun/WireGuardNT por su ABI publico, como driver firmado del
/// sistema (GPLv2, distribuido aparte con su aviso). Requiere servicio elevado.
#[derive(Default)]
pub struct WindowsWintun {
    status: TunnelStatus,
}

impl TunnelBackend for WindowsWintun {
    fn name(&self) -> &'static str {
        "windows-wintun"
    }
    fn up(&mut self, _cfg: &TunnelConfig) -> Result<(), TunnelError> {
        Err(TunnelError::NotImplemented(
            "Fase 3: crear adaptador Wintun via API publica + UAC; sin driver propio firmado",
        ))
    }
    fn down(&mut self) -> Result<(), TunnelError> {
        self.status = TunnelStatus::Down;
        Ok(())
    }
    fn status(&self) -> TunnelStatus {
        self.status
    }
    fn last_handshake_ms(&self) -> u64 {
        0
    }
}
