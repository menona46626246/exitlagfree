// SPDX-License-Identifier: Apache-2.0
//! Descubrimiento basico de MTU. NO sustituye a PMTUD real (no fija DF ni
//! interpreta ICMP fragmentation-needed): solo comprueba que un datagrama UDP
//! de tamano dado obtiene eco. Se documenta como diagnostico aproximado.

use crate::ProbeError;
use std::net::{IpAddr, Ipv4Addr, SocketAddr, UdpSocket};
use std::time::Duration;

pub const CANDIDATE_PAYLOADS: [usize; 5] = [1472, 1400, 1200, 576, 256];

pub fn estimate_path_mtu(
    target: SocketAddr,
    bind_ip: Option<Ipv4Addr>,
    timeout: Duration,
) -> Result<(usize, &'static str), ProbeError> {
    let local = SocketAddr::new(IpAddr::V4(bind_ip.unwrap_or(Ipv4Addr::UNSPECIFIED)), 0);
    let sock = UdpSocket::bind(local)?;
    sock.set_read_timeout(Some(timeout))?;
    for &size in &CANDIDATE_PAYLOADS {
        let mut payload = vec![0u8; size.max(16)];
        payload[..8].copy_from_slice(&(size as u64).to_be_bytes());
        payload[8..16].copy_from_slice(b"tramomtu!");
        if sock.send_to(&payload, target).is_err() {
            continue;
        }
        let mut buf = vec![0u8; 2048];
        if let Ok((n, _)) = sock.recv_from(&mut buf) {
            if n >= 8 && buf[..8] == (size as u64).to_be_bytes() {
                // 20 bytes de cabecera IP + 8 de UDP
                return Ok((size + 28, "estimacion por eco UDP; no es PMTUD con DF"));
            }
        }
    }
    Err(ProbeError::InvalidTarget(
        "sin eco para ningun tamano probado; revisa firewall o usa TCP".into(),
    ))
}
