// SPDX-License-Identifier: Apache-2.0
//! Almacenamiento de secretos. En Fase 3 se conecta al keyring del SO
//! (DPAPI/Credential Manager en Windows, Secret Service/libsecret en Linux).
//! Este modulo solo ofrece la comprobacion de permisos del fichero alternativo.

use std::path::Path;

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum SecretBackend {
    OsKeyring,
    File0600,
}

pub fn preferred_backend() -> SecretBackend {
    // El keyring del SO es el preferido; el fichero 0600 es el degradado
    // documentado y siempre se advierte al usuario en la UI.
    SecretBackend::OsKeyring
}

/// Comprueba que un fichero de claves no es legible por otros usuarios.
/// En Windows no existe el bit de modo Unix: se documenta que la proteccion
/// recae en DPAPI/ACL y esta funcion devuelve `true` con una advertencia.
pub fn assert_private_file(path: &Path) -> Result<(), String> {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let meta = std::fs::metadata(path).map_err(|e| format!("no se puede leer el fichero: {e}"))?;
        let mode = meta.permissions().mode() & 0o777;
        if mode & 0o077 != 0 {
            return Err(format!(
                "permisos inseguros en {:?} ({mode:o}): se requiere 0600. Ejecuta chmod 600.",
                path
            ));
        }
    }
    #[cfg(not(unix))]
    let _ = path;
    Ok(())
}

pub fn key_encoding_is_well_formed(key: &str) -> bool {
    // Clave WireGuard = 32 bytes en base64 => 44 caracteres terminados en '='.
    let key = key.trim();
    key.len() == 44
        && key.ends_with('=')
        && key
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '+' || c == '/' || c == '=')
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn key_shape_validation() {
        assert!(key_encoding_is_well_formed(&("A".repeat(43) + "=")));
        assert!(!key_encoding_is_well_formed("corto"));
        assert!(!key_encoding_is_well_formed(&("A".repeat(44))));
    }

    #[test]
    fn file_permissions_are_enforced_on_unix() {
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            let dir = std::env::temp_dir().join(format!("tramo-secrets-{}", std::process::id()));
            std::fs::create_dir_all(&dir).unwrap();
            let path = dir.join("key");
            std::fs::write(&path, "x").unwrap();
            std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o644)).unwrap();
            assert!(assert_private_file(&path).is_err());
            std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o600)).unwrap();
            assert!(assert_private_file(&path).is_ok());
            let _ = std::fs::remove_dir_all(&dir);
        }
    }
}
