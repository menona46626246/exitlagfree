// SPDX-License-Identifier: Apache-2.0
//! Transporte UDS. En Fase 3 se añade `SO_PEERCRED` (uid/pid del par) con la
//! crate `nix`; aqui ya se exige que el socket sea 0600 y se usa el framing
//! acotado. El helper escucha SOLO en un socket Unix, nunca en TCP.

#[cfg(unix)]
use std::os::unix::net::{UnixListener, UnixStream};
use std::path::Path;

use crate::protocol::command_from_slice;
use crate::{encode_frame, read_frame, Command, IpcError, Reply};

#[cfg(unix)]
pub fn assert_socket_private(path: &Path) -> Result<(), IpcError> {
    use std::os::unix::fs::PermissionsExt;
    let meta = std::fs::metadata(path).map_err(IpcError::Io)?;
    let mode = meta.permissions().mode() & 0o777;
    if mode & 0o077 != 0 {
        return Err(IpcError::Frame(format!(
            "socket {:?} con permisos inseguros {mode:o}; se requiere 0600",
            path
        )));
    }
    Ok(())
}

#[cfg(unix)]
pub fn send_command(path: &Path, cmd: &Command) -> Result<Reply, IpcError> {
    assert_socket_private(path)?;
    let mut stream = UnixStream::connect(path)?;
    let payload = serde_json::to_vec(cmd).map_err(|e| IpcError::Json(e.to_string()))?;
    let frame = encode_frame(&payload)?;
    use std::io::Write;
    stream.write_all(&frame)?;
    let reply = read_frame(&mut stream)?;
    serde_json::from_slice::<Reply>(&reply).map_err(|e| IpcError::Json(e.to_string()))
}

#[cfg(unix)]
pub fn serve_once(
    listener: &UnixListener,
    handler: impl FnOnce(Command) -> Reply,
) -> Result<(), IpcError> {
    let (mut stream, _addr) = listener.accept()?;
    let payload = read_frame(&mut stream)?;
    let cmd: Command = command_from_slice(&payload).map_err(IpcError::Json)?;
    let reply = handler(cmd);
    let bytes = serde_json::to_vec(&reply).map_err(|e| IpcError::Json(e.to_string()))?;
    let frame = encode_frame(&bytes)?;
    use std::io::Write;
    stream.write_all(&frame)?;
    Ok(())
}

#[cfg(not(unix))]
pub fn send_command(_path: &Path, _cmd: &Command) -> Result<Reply, IpcError> {
    Err(IpcError::NotSupported(
        "en Windows se usa un Named Pipe con ACL por SID (Fase 3)".into(),
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[cfg(unix)]
    #[test]
    fn command_is_dispatched_over_a_local_socket() {
        let dir = std::env::temp_dir().join(format!("tramo-ipc-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("helper.sock");
        let listener = UnixListener::bind(&path).unwrap();
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o600)).unwrap();

        let p = path.clone();
        let handle = std::thread::spawn(move || {
            serve_once(&listener, |cmd| match cmd {
                Command::Status => Reply::ok("ok"),
                _ => Reply::err("inesperado"),
            })
        });
        let reply = send_command(&p, &Command::Status).unwrap();
        handle.join().unwrap().unwrap();
        assert_eq!(reply, Reply::ok("ok"));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[cfg(unix)]
    #[test]
    fn world_readable_socket_is_refused() {
        use std::os::unix::fs::PermissionsExt;
        let dir = std::env::temp_dir().join(format!("tramo-ipc-bad-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("helper.sock");
        let _listener = UnixListener::bind(&path).unwrap();
        std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o666)).unwrap();
        assert!(matches!(send_command(&path, &Command::Status), Err(IpcError::Frame(_))));
        let _ = std::fs::remove_dir_all(&dir);
    }
}
