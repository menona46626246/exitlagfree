// SPDX-License-Identifier: Apache-2.0
//! Agente de salud del relay. Expone SOLO metadatos agregados definidos por el
//! operador + carga. Nunca publica IPs de clientes, destinos de juego ni logs
//! de trafico. Escucha en HTTP plano: poner detras de proxy inverso con TLS.

use std::io::{BufRead, BufReader, Write};
use std::net::TcpListener;

fn load_avg() -> f64 {
    // Linux: primer campo de /proc/loadavg normalizado por 4 nucleos, acotado.
    #[cfg(target_os = "linux")]
    {
        if let Ok(s) = std::fs::read_to_string("/proc/loadavg") {
            if let Some(v) = s.split_whitespace().next().and_then(|x| x.parse::<f64>().ok()) {
                return (v / 4.0).clamp(0.0, 1.0);
            }
        }
    }
    0.0
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut args = std::env::args().skip(1);
    let mut addr = "0.0.0.0:8081".to_string();
    while let Some(a) = args.next() {
        if a == "--addr" {
            addr = args.next().unwrap_or(addr);
        }
    }
    let region = std::env::var("TRAMO_REGION_LABEL").unwrap_or_else(|_| "self-hosted".into());
    let listener = TcpListener::bind(&addr)?;
    eprintln!("tramo-node-agent escuchando en http://{addr}");

    for stream in listener.incoming() {
        let Ok(mut stream) = stream else { continue };
        let mut reader = BufReader::new(stream.try_clone()?);
        let mut line = String::new();
        if reader.read_line(&mut line).is_err() {
            continue;
        }
        loop {
            let mut h = String::new();
            if reader.read_line(&mut h).unwrap_or(0) == 0 || h == "\r\n" || h == "\n" {
                break;
            }
        }
        let path = line.split_whitespace().nth(1).unwrap_or("/");
        let (status, body) = if path == "/v1/health" || path == "/health" {
            let json = serde_json::json!({
                "status": "ok",
                "load": load_avg(),
                "active_peers": 0_u32,
                "version": env!("CARGO_PKG_VERSION"),
                "region_label": region,
            }).to_string();
            ("200 OK", json)
        } else {
            ("404 Not Found", r#"{"error":"not found"}"#.to_string())
        };
        let ct = "application/json";
        let head = format!(
            "HTTP/1.1 {status}\r\nContent-Type: {ct}\r\nContent-Length: {}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n",
            body.len()
        );
        let _ = stream.write_all(head.as_bytes());
        let _ = stream.write_all(body.as_bytes());
    }
    Ok(())
}
