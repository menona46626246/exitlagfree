// SPDX-License-Identifier: Apache-2.0
use crate::backend::{TunnelBackend, TunnelConfig, TunnelError, TunnelStatus};

/// Fallback userspace (boringtun, BSD-3-Clause / wireguard-go, MIT, aislado en
/// subproceso). Menos eficiente, util donde no hay modulo kernel ni driver.
#[derive(Default)]
pub struct UserspaceBoringTun {
    status: TunnelStatus,
}

impl TunnelBackend for UserspaceBoringTun {
    fn name(&self) -> &'static str {
        "userspace-boringtun"
    }
    fn up(&mut self, _cfg: &TunnelConfig) -> Result<(), TunnelError> {
        Err(TunnelError::NotImplemented(
            "Fase 3: TUN userspace + handshake Noise; opcional, no dependencia del MVP",
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
