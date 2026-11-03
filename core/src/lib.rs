// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! `tramo-core` es el corazon auditable del producto.
//!
//! Reglas de este crate (reforzadas en revision de PR):
//! * Sin I/O: ni sockets, ni ficheros, ni `std::time::Instant`, ni `tokio`.
//! * Sin `unsafe`.
//! * El tiempo es un `u64` de milisegundos **monotonico inyectado por el llamador**;
//!   el reloj de pared (NTP, suspension, cambio de zona) nunca entra aqui.
//! * Todas las funciones son deterministas: mismos datos de entrada => misma salida.
//!
//! Esto permite testear el scoring sin red, sin permisos y sin simulaciones de tiempo.

pub mod decide;
pub mod hysteresis;
pub mod metrics;
pub mod model;
pub mod percentile;
pub mod score;

pub use decide::{decide, Action, Candidate, DeciderState, Decision, DecisionInput, RouteScore};
pub use hysteresis::HysteresisConfig;
pub use metrics::WindowMetrics;
pub use model::{Mode, ProbeSample, RouteId};
pub use percentile::{FixedHistogram, MAX_RTT_MS};
pub use score::{loss_equivalent_ms, score_route, ScoreWeights};
