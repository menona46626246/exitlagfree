// SPDX-License-Identifier: Apache-2.0
use std::collections::HashMap;
use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HttpRequest {
    pub method: String,
    pub path: String,
    pub headers: HashMap<String, String>,
    pub body: Vec<u8>,
}

/// Parser minimo y acotado de HTTP/1.1 para un servicio de SOLO lectura.
/// Rechaza cuerpos grandes, metodos distintos de GET/HEAD y lineas excesivas.
pub fn parse_request(stream: &TcpStream) -> Result<HttpRequest, String> {
    let mut reader = BufReader::new(stream);
    let mut line = String::new();
    if reader.read_line(&mut line).map_err(|e| e.to_string())? == 0 {
        return Err("conexion cerrada".into());
    }
    if line.len() > 8_192 {
        return Err("linea de peticion demasiado larga".into());
    }
    let mut parts = line.split_whitespace();
    let method = parts.next().unwrap_or("").to_string();
    let path = parts.next().unwrap_or("").to_string();
    if method != "GET" && method != "HEAD" {
        return Err("metodo no permitido (API de solo lectura)".into());
    }
    if !path.starts_with('/') || path.len() > 512 {
        return Err("ruta invalida".into());
    }

    let mut headers = HashMap::new();
    loop {
        let mut h = String::new();
        let n = reader.read_line(&mut h).map_err(|e| e.to_string())?;
        if n == 0 || h == "\r\n" || h == "\n" {
            break;
        }
        if h.len() > 8_192 || headers.len() > 64 {
            return Err("cabeceras excesivas".into());
        }
        if let Some((k, v)) = h.split_once(':') {
            headers.insert(k.trim().to_ascii_lowercase(), v.trim().to_string());
        }
    }
    // Nunca se lee el cuerpo: no hay endpoints que lo acepten.
    Ok(HttpRequest { method, path, headers, body: Vec::new() })
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct HttpResponse {
    pub status: u16,
    pub content_type: &'static str,
    pub body: String,
}

impl HttpResponse {
    pub fn json<T: serde::Serialize>(value: &T) -> Self {
        Self {
            status: 200,
            content_type: "application/json; charset=utf-8",
            body: serde_json::to_string(value).unwrap_or_else(|_| "{}".into()),
        }
    }
    pub fn text(status: u16, body: impl Into<String>) -> Self {
        Self { status, content_type: "text/plain; charset=utf-8", body: body.into() }
    }
    pub fn yaml(body: &str) -> Self {
        Self { status: 200, content_type: "application/yaml; charset=utf-8", body: body.into() }
    }
}

pub fn write_response(mut stream: TcpStream, req: &HttpRequest, resp: HttpResponse) -> std::io::Result<()> {
    let reason = match resp.status {
        200 => "OK",
        401 => "Unauthorized",
        404 => "Not Found",
        429 => "Too Many Requests",
        400 => "Bad Request",
        _ => "Error",
    };
    let head = format!(
        "HTTP/1.1 {} {}\r\nContent-Type: {}\r\nContent-Length: {}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n",
        resp.status, reason, resp.content_type, resp.body.len()
    );
    stream.write_all(head.as_bytes())?;
    if req.method != "HEAD" {
        stream.write_all(resp.body.as_bytes())?;
    }
    stream.flush()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn response_has_no_store_and_correct_length() {
        let r = HttpResponse::json(&serde_json::json!({"ok": true}));
        assert_eq!(r.status, 200);
        assert!(r.content_type.contains("json"));
        assert_eq!(r.body, r#"{"ok":true}"#);
    }

    #[test]
    fn unsafe_status_codes_are_available() {
        assert_eq!(HttpResponse::text(429, "limite").status, 429);
        assert_eq!(HttpResponse::text(401, "clave").status, 401);
    }
}
