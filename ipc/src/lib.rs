// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! IPC local entre el motor (sin privilegios) y `tramo-helper` (privilegiado).
//!
//! Propiedades de seguridad:
//! * **Enum cerrado**: no existe comando que acepte texto libre, rutas a
//!   archivos, scripts ni binarios. Imposibilita ejecucion remota de codigo.
//! * **Framing acotado**: 64 KiB maximo por mensaje, longitud verificada antes
//!   de asignar memoria.
//! * **Autenticacion de par**: UDS 0600 + `SO_PEERCRED` (Linux) / Named Pipe
//!   con SID (Windows). La comprobacion de credenciales se conecta en Fase 3;
//!   los permisos del socket se verifican ya desde el arranque.
//! * El helper **nunca** expone este socket en TCP ni en una interfaz de red.

pub mod framing;
pub mod protocol;
pub mod unix;
pub mod windows;

pub use framing::{encode_frame, parse_frame_len, read_frame, MAX_FRAME};
pub use protocol::{Command, Reply};

#[derive(Debug)]
pub enum IpcError {
    Frame(String),
    Io(std::io::Error),
    Json(String),
    NotSupported(String),
    Unauthenticated,
}

impl std::fmt::Display for IpcError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Frame(s) => write!(f, "frame invalido: {s}"),
            Self::Io(e) => write!(f, "error de IPC: {e}"),
            Self::Json(s) => write!(f, "mensaje invalido: {s}"),
            Self::NotSupported(s) => write!(f, "no soportado en esta plataforma: {s}"),
            Self::Unauthenticated => write!(f, "par no autenticado"),
        }
    }
}

impl std::error::Error for IpcError {}

impl From<std::io::Error> for IpcError {
    fn from(e: std::io::Error) -> Self {
        Self::Io(e)
    }
}
