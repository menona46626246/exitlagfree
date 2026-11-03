// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct RouteSnapshot {
    pub id: String,
    pub created_ms: u64,
    /// Representacion declarativa del estado previo (reglas/rutas), no comandos.
    pub entries: Vec<String>,
}

pub struct SnapshotStore {
    path: PathBuf,
}

impl SnapshotStore {
    pub fn new(path: impl AsRef<Path>) -> Self {
        Self { path: path.as_ref().to_path_buf() }
    }

    pub fn save(&self, snap: &RouteSnapshot) -> Result<(), String> {
        let json = serde_json::to_string_pretty(snap).map_err(|e| e.to_string())?;
        std::fs::write(&self.path, json).map_err(|e| e.to_string())?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&self.path, std::fs::Permissions::from_mode(0o600))
                .map_err(|e| e.to_string())?;
        }
        Ok(())
    }

    pub fn load(&self) -> Result<RouteSnapshot, String> {
        let bytes = std::fs::read(&self.path).map_err(|e| e.to_string())?;
        serde_json::from_slice(&bytes).map_err(|e| e.to_string())
    }

    pub fn remove(&self) -> Result<(), String> {
        match std::fs::remove_file(&self.path) {
            Ok(()) => Ok(()),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(e) => Err(e.to_string()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn snapshot_round_trip_and_removal() {
        let path = std::env::temp_dir().join(format!("tramo-snap-{}.json", std::process::id()));
        let store = SnapshotStore::new(&path);
        let snap = RouteSnapshot { id: "s1".into(), created_ms: 42, entries: vec!["ip rule pref 100".into()] };
        store.save(&snap).unwrap();
        assert_eq!(store.load().unwrap(), snap);
        store.remove().unwrap();
        assert!(store.load().is_err());
    }

    #[cfg(unix)]
    #[test]
    fn snapshot_file_is_0600() {
        use std::os::unix::fs::PermissionsExt;
        let path = std::env::temp_dir().join(format!("tramo-snap-perm-{}.json", std::process::id()));
        let store = SnapshotStore::new(&path);
        store.save(&RouteSnapshot { id: "s".into(), created_ms: 1, entries: vec![] }).unwrap();
        let mode = std::fs::metadata(&path).unwrap().permissions().mode() & 0o777;
        assert_eq!(mode, 0o600);
        let _ = std::fs::remove_file(&path);
    }
}
