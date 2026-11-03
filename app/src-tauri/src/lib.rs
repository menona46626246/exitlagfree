// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! Logica de los comandos de la UI, SIN dependencias de runtime Tauri aun
//! (Fase 6). Separar la logica del runtime permite testearla en CI sin WebView
//! y mantiene el frontend como una capa tonta y auditable.

pub mod commands;
pub mod events;

pub use commands::{NodeView, StatusView, UiState};
pub use events::ThrottledEvents;
