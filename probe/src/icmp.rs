// SPDX-License-Identifier: Apache-2.0
//! Sonda ICMP echo. Requiere socket RAW (`CAP_NET_RAW` / ping_group_range en
//! Linux; `IcmpSendEcho` sin elevacion en Windows en Fase 3). Si falla, el
//! motor degrada a UDP/TCP: nunca se exige administrador para medir.
//!
//! Nota: `socket2` 0.5 usa `&mut [MaybeUninit<u8>]` en `recv_from`; el unico
//! `unsafe` de este crate esta aqui, acotado y justificado, tras una lectura
//! completada con exito. `core/`, `config/` e `ipc/` prohiben `unsafe`.

use crate::{ProbeConfig, ProbeError};
use socket2::{Domain, Protocol, SockAddr, Socket, Type};
use std::mem::MaybeUninit;
use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::time::Instant;
use tramo_core::ProbeSample;

fn checksum(data: &[u8]) -> u16 {
    let mut sum: u32 = 0;
    let mut chunks = data.chunks_exact(2);
    for c in &mut chunks {
        sum += u16::from_be_bytes([c[0], c[1]]) as u32;
    }
    if let Some(&last) = chunks.remainder().first() {
        sum += u16::from_be_bytes([last, 0]) as u32;
    }
    while sum >> 16 != 0 {
        sum = (sum & 0xffff) + (sum >> 16);
    }
    !(sum as u16)
}

fn echo_request(id: u16, seq: u16) -> [u8; 16] {
    let mut pkt = [0u8; 16];
    pkt[0] = 8; // Echo Request
    pkt[4..6].copy_from_slice(&id.to_be_bytes());
    pkt[6..8].copy_from_slice(&seq.to_be_bytes());
    pkt[8..].copy_from_slice(b"tramoping");
    let csum = checksum(&pkt);
    pkt[2..4].copy_from_slice(&csum.to_be_bytes());
    pkt
}

pub fn icmp_available() -> bool {
    Socket::new(Domain::IPV4, Type::RAW, Some(Protocol::ICMPV4)).is_ok()
}

pub fn run_icmp_probe(
    target: Ipv4Addr,
    cfg: &ProbeConfig,
) -> Result<Vec<ProbeSample>, ProbeError> {
    cfg.validate().map_err(ProbeError::InvalidTarget)?;
    let sock = Socket::new(Domain::IPV4, Type::RAW, Some(Protocol::ICMPV4)).map_err(|e| {
        ProbeError::Permission(format!(
            "no se puede abrir socket RAW ICMP ({e}); usa CAP_NET_RAW o degrada a UDP/TCP"
        ))
    })?;
    sock.set_read_timeout(Some(cfg.timeout()))?;
    let remote = SockAddr::from(SocketAddr::new(IpAddr::V4(target), 0));
    let id = (std::process::id() & 0xffff) as u16;

    let mut samples = Vec::with_capacity(cfg.count);
    for seq in 0..cfg.count as u64 {
        let pkt = echo_request(id, seq as u16);
        let sent = Instant::now();
        let mut buf = vec![MaybeUninit::<u8>::uninit(); 2048];
        let rtt = if sock.send_to(&pkt, &remote).is_ok() {
            match sock.recv_from(&mut buf) {
                Ok((n, _)) => {
                    // SAFETY: `recv_from` devuelve `n` bytes inicializados al
                    // principio de `buf`; solo se lee ese rango, una vez.
                    let bytes: Vec<u8> = buf[..n]
                        .iter()
                        .map(|b| unsafe { b.assume_init() })
                        .collect();
                    let ihl = ((bytes.first().copied().unwrap_or(0x45) & 0x0f) as usize) * 4;
                    if bytes.len() >= ihl + 8 && bytes[ihl] == 0 {
                        let reply_id = u16::from_be_bytes([bytes[ihl + 4], bytes[ihl + 5]]);
                        let reply_seq = u16::from_be_bytes([bytes[ihl + 6], bytes[ihl + 7]]);
                        if reply_id == id && reply_seq as u64 == seq {
                            Some(sent.elapsed().as_secs_f64() * 1_000.0)
                        } else {
                            None
                        }
                    } else {
                        None
                    }
                }
                Err(_) => None,
            }
        } else {
            None
        };
        samples.push(ProbeSample { seq, rtt_ms: rtt });
        let elapsed = sent.elapsed();
        if elapsed < cfg.interval() {
            std::thread::sleep(cfg.interval() - elapsed);
        }
    }
    Ok(samples)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn checksum_of_echo_request_is_deterministic_and_seq_dependent() {
        let a = echo_request(0x1234, 1);
        assert_eq!(a, echo_request(0x1234, 1));
        let mut zeroed_1 = a;
        zeroed_1[2] = 0;
        zeroed_1[3] = 0;
        let mut zeroed_2 = echo_request(0x1234, 2);
        zeroed_2[2] = 0;
        zeroed_2[3] = 0;
        assert_ne!(checksum(&zeroed_1), checksum(&zeroed_2));
        // El checksum insertado es exactamente el calculado con el campo a cero.
        assert_eq!(checksum(&zeroed_1), u16::from_be_bytes([a[2], a[3]]));
        // Verificar un paquete completo produce 0 (propiedad de ones' complement).
        assert_eq!(checksum(&a), 0);
    }
}
