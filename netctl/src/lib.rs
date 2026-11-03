// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! Plano de control. **Este crate no ejecuta comandos del sistema**: produce
//! planes declarativos que el helper privilegiado traduce a netlink/WFP en la
//! Fase 3. Contiene las garantias de seguridad que si son implementables ya:
//! snapshot, lease con heartbeat y preflight fail-safe.

pub mod firewall;
pub mod lease;
pub mod netmon;
pub mod preflight;
pub mod routes;
pub mod snapshot;

pub use firewall::{KillSwitchPolicy, allowlist_always_permitted, FirewallTag};
pub use lease::Lease;
pub use netmon::NetworkChangeDetector;
pub use preflight::{run_preflight, PreflightResult};
pub use routes::{build_route_plan, RoutePlan};
pub use snapshot::{RouteSnapshot, SnapshotStore};
