// SPDX-License-Identifier: Apache-2.0
//! Helper privilegiado. En este bloque solo responde `Status`/`Preflight`;
//! TODAS las operaciones que tocarian rutas devuelven un error explicito de
//! Fase 3. Es deliberado: nunca se simula un cambio aplicado.
//!
//! Fase 3 añadira: autenticacion SO_PEERCRED/SID, creacion de TUN, aplicacion
//! del RoutePlan, etiquetado de firewall y restauracion por lease.

use tramo_ipc::{Command, Reply};

fn dispatch(cmd: Command) -> Reply {
    match cmd {
        Command::Status => Reply::Status {
            privileged: true,
            active_route: "direct".into(),
            kill_switch: false,
            lease_valid: true,
        },
        Command::Preflight => Reply::ok("preflight: backends de tunel pendientes de Fase 3"),
        Command::Restore => Reply::ok("sin rutas propias que restaurar"),
        Command::ApplyRoute { route } => Reply::err(format!(
            "ApplyRoute('{route}') no implementado hasta Fase 3; la red no se ha modificado"
        )),
        Command::SetKillSwitch { enabled } => Reply::err(format!(
            "SetKillSwitch({enabled}) no implementado hasta Fase 3; firewall intacto"
        )),
        Command::Shutdown => Reply::ok("helper finalizado"),
    }
}

#[cfg(unix)]
fn main() -> Result<(), Box<dyn std::error::Error>> {
    use std::os::unix::fs::PermissionsExt;
    use std::os::unix::net::UnixListener;
    use tramo_ipc::unix::serve_once;

    let mut args = std::env::args().skip(1);
    let mut socket = std::path::PathBuf::from("/run/user/1000/tramo-helper.sock");
    while let Some(a) = args.next() {
        if a == "--socket" {
            if let Some(p) = args.next() {
                socket = std::path::PathBuf::from(p);
            }
        }
    }
    if let Some(parent) = socket.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let _ = std::fs::remove_file(&socket);
    let listener = UnixListener::bind(&socket)?;
    std::fs::set_permissions(&socket, std::fs::Permissions::from_mode(0o600))?;
    eprintln!("tramo-helper escuchando en {:?} (solo IPC local)", socket);
    loop {
        match serve_once(&listener, dispatch) {
            Ok(()) => {}
            Err(e) => eprintln!("conexion rechazada: {e}"),
        }
    }
}

#[cfg(not(unix))]
fn main() {
    eprintln!("tramo-helper: backend Named Pipe (Windows) se implementa en Fase 3");
    let _ = dispatch;
}
