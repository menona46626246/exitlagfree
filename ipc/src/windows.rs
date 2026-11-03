// SPDX-License-Identifier: Apache-2.0
//! Transporte Windows (Fase 3): Named Pipe con ACL restringida al SID del
//! usuario y `GetNamedPipeClientProcessId`. Se deja el contrato explicito para
//! que el helper no pueda exponer accidentalmente un socket TCP.
//!
//! Este modulo es neutral en cuanto a plataforma: en Linux simplemente informa
//! de que el backend no aplica, sin APIs de Windows compiladas condicionalmente.

use crate::{Command, IpcError, Reply};

pub const PIPE_NAME: &str = r"\\.\pipe\tramo-helper";

pub fn send_command(_pipe: &str, _cmd: &Command) -> Result<Reply, IpcError> {
    Err(IpcError::NotSupported(
        "backend Named Pipe + verificacion de SID se implementa en Fase 3".into(),
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn windows_backend_fails_closed_instead_of_silently_ignoring() {
        assert!(matches!(send_command(PIPE_NAME, &Command::Status), Err(IpcError::NotSupported(_))));
    }
}
