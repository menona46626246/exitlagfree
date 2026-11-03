// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! Orquestacion: ventanas rodantes, estado del decisor, watchdog de handshake
//! y diagnostico. Este crate **no aplica rutas**: entrega `Decision` al plano
//! de control y eventos serializables a la UI.

pub mod diagnose;
pub mod events;
pub mod scheduler;
pub mod state;
pub mod watchdog;

pub use diagnose::{run_diagnostics, DiagnosticResult};
pub use events::EngineEvent;
pub use scheduler::RollingWindow;
pub use state::{DecisionRecord, EngineState};
pub use watchdog::HandshakeWatchdog;
