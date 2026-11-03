// SPDX-License-Identifier: Apache-2.0
//! Fallback TCP: mide el RTT del handshake (SYN -> SYN/ACK) con
//! `connect_timeout`. No envia datos de aplicacion ni altera conexiones ajenas.

use crate::{ProbeConfig, ProbeError};
use std::net::{SocketAddr, TcpStream};
use std::time::Instant;
use tramo_core::ProbeSample;

pub fn run_tcp_probe(
    target: SocketAddr,
    cfg: &ProbeConfig,
) -> Result<Vec<ProbeSample>, ProbeError> {
    cfg.validate().map_err(ProbeError::InvalidTarget)?;
    let mut samples = Vec::with_capacity(cfg.count);
    for seq in 0..cfg.count as u64 {
        let sent = Instant::now();
        let rtt = match TcpStream::connect_timeout(&target, cfg.timeout()) {
            Ok(stream) => {
                drop(stream);
                Some(sent.elapsed().as_secs_f64() * 1_000.0)
            }
            Err(_) => None,
        };
        samples.push(ProbeSample { seq, rtt_ms: rtt });
        let elapsed = sent.elapsed();
        if elapsed < cfg.interval() {
            std::thread::sleep(cfg.interval() - elapsed);
        }
    }
    Ok(samples)
}
