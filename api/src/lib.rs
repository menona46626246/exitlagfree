// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! API de nodos autohospedada.
//!
//! Decision de Fase 1: servidor HTTP/1.1 minimo con `std` y `serde_json`, sin
//! framework. Motivo: menos superficie de supply chain para un servicio que
//! puede quedar expuesto a Internet. En Fase 7 se evaluara migrar a axum+utoipa
//! sin cambiar el contrato (OpenAPI ya publicado).
//!
//! Propiedades: SOLO lectura (GET), sin cuerpo de entrada, sin endpoints de
//! configuracion ni comandos -> imposibilidad estructural de RCE. Health sin
//! PII, rate limiting y API key opcional en tiempo constante.

pub mod auth;
pub mod http;
pub mod ratelimit;
pub mod registry;

pub use auth::{constant_time_eq, ApiKey};
pub use http::{parse_request, write_response, HttpRequest, HttpResponse};
pub use ratelimit::RateLimiter;
pub use registry::{Health, NodeRecord, Registry};
