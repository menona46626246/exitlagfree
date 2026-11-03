// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! Configuracion versionada de Tramo.
//! Principios: validar siempre al importar, nunca ejecutar nada contenido en el
//! JSON, y **no exportar claves privadas** salvo peticion explicita.

pub mod import_export;
pub mod profile;
pub mod rules;
pub mod schema;
pub mod secrets;

pub use import_export::{export_config, import_config, validate_config};
pub use profile::GameProfile;
pub use rules::{Rule, RuleKind};
pub use schema::{Config, LoggingConfig, NodeEntry, ProbeSettings, TelemetryConfig};
