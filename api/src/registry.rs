// SPDX-License-Identifier: Apache-2.0
use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct NodeRecord {
    pub id: String,
    pub name: String,
    /// Etiqueta estatica definida por el operador. NUNCA geolocalizacion del cliente.
    pub region_label: String,
    pub endpoint: String,
    pub public_key: String,
    pub version: String,
    #[serde(default)]
    pub priority: u32,
    #[serde(default = "default_status")]
    pub status: String,
}

fn default_status() -> String {
    "unknown".into()
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Health {
    pub status: String,
    /// Carga [0,1], autodeclarada: el cliente la trata como NO confiable.
    pub load: f64,
    pub active_peers: u32,
    pub version: String,
    pub region_label: String,
    /// Numero de nodos publicados. Sin IPs de clientes ni logs de trafico.
    pub nodes: usize,
}

#[derive(Clone, Debug, Default)]
pub struct Registry {
    nodes: Vec<NodeRecord>,
}

impl Registry {
    pub fn from_json_str(json: &str) -> Result<Self, String> {
        #[derive(Deserialize)]
        struct File { nodes: Vec<NodeRecord> }
        let f: File = serde_json::from_str(json).map_err(|e| e.to_string())?;
        Ok(Self { nodes: f.nodes })
    }

    pub fn from_json_file(path: &Path) -> Result<Self, String> {
        let bytes = std::fs::read(path).map_err(|e| format!("{:?}: {e}", path))?;
        Self::from_json_str(&String::from_utf8(bytes).map_err(|e| e.to_string())?)
    }

    pub fn list(&self) -> &[NodeRecord] {
        &self.nodes
    }

    pub fn get(&self, id: &str) -> Option<&NodeRecord> {
        self.nodes.iter().find(|n| n.id == id)
    }

    pub fn health(&self, load: f64, active_peers: u32, version: &str, region: &str) -> Health {
        Health {
            status: "ok".into(),
            load: load.clamp(0.0, 1.0),
            active_peers,
            version: version.into(),
            region_label: region.into(),
            nodes: self.nodes.len(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const JSON: &str = r#"{"nodes":[{"id":"wg-fra-1","name":"Frankfurt","region_label":"eu-central","endpoint":"198.51.100.1:51820","public_key":"AAAA","version":"0.1.0","status":"ok"}]}"#;

    #[test]
    fn registry_loads_and_looks_up() {
        let r = Registry::from_json_str(JSON).unwrap();
        assert_eq!(r.list().len(), 1);
        assert_eq!(r.get("wg-fra-1").unwrap().region_label, "eu-central");
        assert!(r.get("nope").is_none());
    }

    #[test]
    fn health_contains_no_client_identifiers() {
        let r = Registry::from_json_str(JSON).unwrap();
        let h = r.health(2.5, 7, "0.1.0", "eu-central");
        assert_eq!(h.load, 1.0); // saturado
        let j = serde_json::to_string(&h).unwrap();
        assert!(!j.contains("198.51.100.1"));
        assert!(!j.contains("peer_ip"));
        assert_eq!(h.nodes, 1);
    }
}
