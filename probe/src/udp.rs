// SPDX-License-Identifier: Apache-2.0
//! Sonda UDP con numero de secuencia. Requiere un extremo que responda (el nodo
//! relay expone un eco de diagnostico; los servidores de juego que respondan
//! tambien sirven). Si no hay respuesta se degrada a TCP, nunca se inyecta
//! trafico en una sesion de juego existente.

use crate::{ProbeConfig, ProbeError};
use std::net::{IpAddr, Ipv4Addr, SocketAddr, UdpSocket};
use std::time::Instant;
use tramo_core::ProbeSample;

/// `bind_ip` permite fijar la interfaz de origen. En Linux la exclusion real de
/// la ruta del tunel se hace con `SO_MARK` + `ip rule` en `tramo-netctl`; en
/// Windows el bind a la IP fisica es una aproximacion con limitaciones
/// documentadas (Wintun puede capturar el trafico igualmente).
pub fn run_udp_probe(
    target: SocketAddr,
    cfg: &ProbeConfig,
    bind_ip: Option<Ipv4Addr>,
) -> Result<Vec<ProbeSample>, ProbeError> {
    cfg.validate().map_err(ProbeError::InvalidTarget)?;
    let local = SocketAddr::new(
        IpAddr::V4(bind_ip.unwrap_or(Ipv4Addr::UNSPECIFIED)),
        0,
    );
    let sock = UdpSocket::bind(local)?;
    sock.set_read_timeout(Some(cfg.timeout()))?;

    let mut samples = Vec::with_capacity(cfg.count);
    let mut buf = [0u8; 64];
    for seq in 0..cfg.count as u64 {
        let mut payload = [0u8; 16];
        payload[..8].copy_from_slice(&seq.to_be_bytes());
        payload[8..].copy_from_slice(b"tramoprobe");
        let sent = Instant::now();
        if sock.send_to(&payload, target).is_err() {
            samples.push(ProbeSample::lost(seq));
            std::thread::sleep(cfg.interval());
            continue;
        }
        let rtt = match sock.recv_from(&mut buf) {
            Ok((n, _)) if n >= 8 && buf[..8] == seq.to_be_bytes() => {
                Some(sent.elapsed().as_secs_f64() * 1_000.0)
            }
            // Respuesta de otro numero de secuencia: reordenamiento. Se cuenta
            // como perdida de esta sonda; la ventana tolera reordenamiento.
            Ok(_) => None,
            Err(_) => None, // timeout / ICMP port unreachable
        };
        samples.push(ProbeSample { seq, rtt_ms: rtt });
        let elapsed = sent.elapsed();
        if elapsed < cfg.interval() {
            std::thread::sleep(cfg.interval() - elapsed);
        }
    }
    Ok(samples)
}
