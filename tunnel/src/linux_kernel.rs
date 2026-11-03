// SPDX-License-Identifier: Apache-2.0
use crate::backend::{TunnelBackend, TunnelConfig, TunnelError, TunnelStatus};

/// Linux: modulo WireGuard del kernel via netlink (uAPI publica), sin enlazar
/// codigo GPLv2 al binario de Tramo. Requiere `CAP_NET_ADMIN`.
#[derive(Default)]
pub struct LinuxKernelWg {
    status: TunnelStatus,
}

impl TunnelBackend for LinuxKernelWg {
    fn name(&self) -> &'static str {
        "linux-kernel-wireguard"
    }
    fn up(&mut self, _cfg: &TunnelConfig) -> Result<(), TunnelError> {
        Err(TunnelError::NotImplemented(
            "Fase 3: crear interfaz wg, asignar claves/peer por netlink y verificar `wg show`",
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
