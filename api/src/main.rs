// SPDX-License-Identifier: Apache-2.0
//! `tramo-api` — registro de nodos y health.
//! Uso: tramo-api --addr 0.0.0.0:8080 --nodes config/examples/nodes.json
//! Variable de entorno opcional: TRAMO_API_KEY (cabecera X-Tramo-Key).
//! TLS: terminar con Caddy/Traefik. Este proceso NO escucha en HTTPS.

use std::sync::{Arc, Mutex};
use std::time::Instant;
use tramo_api::{ApiKey, HttpResponse, RateLimiter, Registry};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut args = std::env::args().skip(1);
    let mut addr = "0.0.0.0:8080".to_string();
    let mut nodes_path = "config/examples/nodes.json".to_string();
    while let Some(a) = args.next() {
        match a.as_str() {
            "--addr" => addr = args.next().unwrap_or(addr),
            "--nodes" => nodes_path = args.next().unwrap_or(nodes_path),
            _ => eprintln!("argumento ignorado: {a}"),
        }
    }

    let registry = Arc::new(Registry::from_json_file(std::path::Path::new(&nodes_path))?);
    let limiter = Arc::new(Mutex::new(RateLimiter::default()));
    let api_key = Arc::new(ApiKey::from_env());
    let started = Instant::now();
    let listener = std::net::TcpListener::bind(&addr)?;
    eprintln!("tramo-api escuchando en http://{addr} (TLS detras de proxy inverso)");

    for stream in listener.incoming() {
        let stream = match stream {
            Ok(s) => s,
            Err(e) => {
                eprintln!("accept: {e}");
                continue;
            }
        };
        let (registry, limiter, api_key) = (registry.clone(), limiter.clone(), api_key.clone());
        std::thread::spawn(move || {
            let now_ms = started.elapsed().as_millis() as u64;
            let peer = stream.peer_addr().map(|a| a.ip().to_string()).unwrap_or_else(|_| "?".into());
            let req = match tramo_api::parse_request(&stream) {
                Ok(r) => r,
                Err(e) => {
                    let _ = tramo_api::write_response(stream, &tramo_api::HttpRequest {
                        method: "GET".into(), path: "/".into(),
                        headers: Default::default(), body: Vec::new(),
                    }, HttpResponse::text(400, e));
                    return;
                }
            };
            let allowed = limiter.lock().map(|mut l| l.allow(&peer, now_ms)).unwrap_or(false);
            let authed = api_key.verify(req.headers.get("x-tramo-key").map(|s| s.as_str()));
            let resp = if !allowed {
                HttpResponse::text(429, "rate limit: 60 peticiones/minuto/IP")
            } else if !authed {
                HttpResponse::text(401, "X-Tramo-Key ausente o incorrecta")
            } else {
                route(&req, &registry)
            };
            let _ = tramo_api::write_response(stream, &req, resp);
        });
    }
    Ok(())
}

fn route(req: &tramo_api::HttpRequest, registry: &Registry) -> HttpResponse {
    match req.path.as_str() {
        "/v1/health" => HttpResponse::json(&registry.health(0.0, 0, env!("CARGO_PKG_VERSION"), "self-hosted")),
        "/v1/nodes" => HttpResponse::json(&registry.list()),
        "/openapi.yaml" => HttpResponse::yaml(include_str!("../openapi.yaml")),
        p if p.starts_with("/v1/nodes/") => {
            let id = p.trim_start_matches("/v1/nodes/");
            match registry.get(id) {
                Some(n) => HttpResponse::json(n),
                None => HttpResponse::text(404, "nodo no encontrado"),
            }
        }
        _ => HttpResponse::text(404, "ruta no encontrada"),
    }
}
