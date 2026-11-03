// SPDX-License-Identifier: Apache-2.0
#![forbid(unsafe_code)]
//! Integracion del tunel. En este bloque se entrega el **contrato** y trois
//! backends que fallan cerrado (`NotImplemented`) hasta la Fase 3.
//! Ninguna funcion devuelve `Ok` sin haber aplicado y verificado la ruta real:
//! es la regla anti-"falso tunel activo".

pub mod backend;
pub mod handshake;
pub mod keys;
pub mod linux_kernel;
pub mod userspace_boringtun;
pub mod windows_wintun;

pub use backend::{TunnelBackend, TunnelConfig, TunnelError, TunnelStatus};
pub use handshake::HandshakeState;
pub use keys::{key_shape_is_valid, redact_key};
pub use linux_kernel::LinuxKernelWg;
pub use userspace_boringtun::UserspaceBoringTun;
pub use windows_wintun::WindowsWintun;
