// SPDX-License-Identifier: Apache-2.0
//! `tramoctl` permite usar el motor SIN la UI y SIN privilegios. Es la via
//! reproducible para benchmarks y para usuarios que solo quieren medir.

use clap::{Parser, Subcommand};
use std::net::Ipv4Addr;
use tramo_core::{ProbeSample, ScoreWeights, WindowMetrics};
use tramo_probe::ProbeConfig;

#[derive(Parser, Debug)]
#[command(name = "tramoctl", version, about = "Tramo: medicion de red auditable (CLI headless)")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand, Debug)]
enum Command {
    /// Mide latencia/jitter/perdida contra un destino configurable.
    Measure {
        #[arg(long)]
        target: String,
        #[arg(long, default_value_t = 40)]
        count: usize,
        #[arg(long, default_value_t = 200)]
        interval_ms: u64,
        #[arg(long, default_value_t = 1000)]
        timeout_ms: u64,
        /// IP de la interfaz fisica para excluir la sonda del tunel.
        #[arg(long)]
        bind_ip: Option<String>,
        /// auto | udp | tcp | icmp
        #[arg(long, default_value = "auto")]
        protocol: String,
    },
    /// Calcula metricas y score desde un JSON de sondas (sin red, determinista).
    Score {
        #[arg(long)]
        input: String,
        #[arg(long)]
        weights: Option<String>,
    },
    /// Diagnostico DNS / conectividad / permisos / firewall / MTU.
    Diagnose {
        #[arg(long)]
        target: Option<String>,
    },
    /// Validar / exportar / importar configuracion.
    Config {
        #[command(subcommand)]
        action: ConfigCommand,
    },
}

#[derive(Subcommand, Debug)]
enum ConfigCommand {
    Validate {
        #[arg(long)]
        file: String,
    },
    Export {
        #[arg(long)]
        file: String,
        /// ADVERTENCIA: incluye claves privadas. No compartir el resultado.
        #[arg(long)]
        include_secrets: bool,
    },
    Import {
        #[arg(long)]
        file: String,
    },
}

fn main() {
    if let Err(e) = run() {
        eprintln!("error: {e}");
        std::process::exit(1);
    }
}

fn run() -> Result<(), String> {
    let cli = Cli::parse();
    match cli.command {
        Command::Measure { target, count, interval_ms, timeout_ms, bind_ip, protocol } => {
            measure(&target, count, interval_ms, timeout_ms, bind_ip, &protocol)
        }
        Command::Score { input, weights } => score(&input, weights.as_deref()),
        Command::Diagnose { target } => {
            let results = tramo_engine::run_diagnostics(target.as_deref());
            println!("{}", serde_json::to_string_pretty(&results).map_err(|e| e.to_string())?);
            Ok(())
        }
        Command::Config { action } => match action {
            ConfigCommand::Validate { file } => {
                let json = std::fs::read_to_string(&file).map_err(|e| e.to_string())?;
                let cfg = tramo_config::import_config(&json)?;
                println!("OK: {} perfiles, {} nodos, modo {:?}", cfg.profiles.len(), cfg.nodes.len(), cfg.mode);
                Ok(())
            }
            ConfigCommand::Export { file, include_secrets } => {
                let json = std::fs::read_to_string(&file).map_err(|e| e.to_string())?;
                let cfg = tramo_config::import_config(&json)?;
                if include_secrets {
                    eprintln!("ADVERTENCIA: la salida puede contener claves privadas. No la compartas.");
                }
                println!("{}", tramo_config::export_config(&cfg, include_secrets)?);
                Ok(())
            }
            ConfigCommand::Import { file } => {
                let json = std::fs::read_to_string(&file).map_err(|e| e.to_string())?;
                let cfg = tramo_config::import_config(&json)?;
                println!("{}", tramo_config::export_config(&cfg, false)?);
                Ok(())
            }
        },
    }
}

fn measure(
    target: &str,
    count: usize,
    interval_ms: u64,
    timeout_ms: u64,
    bind_ip: Option<String>,
    protocol: &str,
) -> Result<(), String> {
    let cfg = ProbeConfig { interval_ms, timeout_ms, count };
    cfg.validate()?;
    let addr = tramo_probe::resolve_target(target).map_err(|e| e.to_string())?;
    let bind = bind_ip
        .map(|s| s.parse::<Ipv4Addr>().map_err(|e| format!("bind_ip invalida: {e}")))
        .transpose()?;

    let icmp_ok = tramo_probe::icmp_available();
    let (chosen, reason) = match protocol {
        "udp" => ("udp", "forzado por el usuario".to_string()),
        "tcp" => ("tcp", "forzado por el usuario".to_string()),
        "icmp" => ("icmp", "forzado por el usuario".to_string()),
        _ => tramo_probe::select_protocol(icmp_ok, true),
    };

    let mut samples: Vec<ProbeSample> = match chosen {
        "icmp" => match addr.ip() {
            std::net::IpAddr::V4(v4) => tramo_probe::run_icmp_probe(v4, &cfg).map_err(|e| e.to_string())?,
            std::net::IpAddr::V6(_) => {
                tramo_probe::run_udp_probe(addr, &cfg, bind).map_err(|e| e.to_string())?
            }
        },
        "tcp" => tramo_probe::run_tcp_probe(addr, &cfg).map_err(|e| e.to_string())?,
        _ => tramo_probe::run_udp_probe(addr, &cfg, bind).map_err(|e| e.to_string())?,
    };

    // Degradacion explicita: si UDP no obtiene ninguna respuesta, probar TCP.
    if chosen == "udp" && samples.iter().all(|s| s.rtt_ms.is_none()) {
        eprintln!("sin eco UDP; degradando a handshake TCP");
        samples = tramo_probe::run_tcp_probe(addr, &cfg).map_err(|e| e.to_string())?;
    }

    let window_ms = (interval_ms * count as u64).max(10_000);
    let m = WindowMetrics::from_samples(&samples, window_ms, 0);
    let out = match m {
        Some(m) => serde_json::json!({
            "target": target,
            "protocol": chosen,
            "protocol_reason": reason,
            "samples": m.samples,
            "received": m.received,
            "p50_ms": m.p50_ms,
            "p95_ms": m.p95_ms,
            "p99_ms": m.p99_ms,
            "jitter_ms": m.jitter_ms,
            "jitter_rfc3550_ms": m.jitter_rfc3550_ms,
            "loss": m.loss,
            "consecutive_timeouts": m.consecutive_timeouts,
            "score": tramo_core::score_route(&m, 0.0, 0.0, &ScoreWeights::default()),
        }),
        None => serde_json::json!({
            "target": target,
            "protocol": chosen,
            "protocol_reason": reason,
            "error": "sin respuestas: ruta caida, firewall bloqueando o destino sin eco",
            "samples": samples.len(),
        }),
    };
    println!("{}", serde_json::to_string_pretty(&out).map_err(|e| e.to_string())?);
    Ok(())
}

fn score(input: &str, weights_path: Option<&str>) -> Result<(), String> {
    let raw = std::fs::read_to_string(input).map_err(|e| e.to_string())?;
    let v: serde_json::Value = serde_json::from_str(&raw).map_err(|e| e.to_string())?;
    let samples: Vec<ProbeSample> = serde_json::from_value(
        v.get("samples").cloned().ok_or("falta 'samples'")?,
    )
    .map_err(|e| format!("samples invalidos: {e}"))?;
    let window_ms = v.get("window_ms").and_then(|x| x.as_u64()).unwrap_or(60_000);
    let flaps = v.get("flaps").and_then(|x| x.as_u64()).unwrap_or(0) as usize;
    let load = v.get("load").and_then(|x| x.as_f64()).unwrap_or(0.0);
    let geo = v.get("geo_cost_ms").and_then(|x| x.as_f64()).unwrap_or(0.0);
    let weights = match weights_path {
        Some(p) => {
            let w = std::fs::read_to_string(p).map_err(|e| e.to_string())?;
            serde_json::from_str::<ScoreWeights>(&w).map_err(|e| e.to_string())?
        }
        None => ScoreWeights::default(),
    };
    weights.validate()?;
    let m = WindowMetrics::from_samples(&samples, window_ms, flaps)
        .ok_or("sin muestras recibidas: no hay metricas que puntuar")?;
    let out = serde_json::json!({
        "p50_ms": m.p50_ms,
        "p95_ms": m.p95_ms,
        "p99_ms": m.p99_ms,
        "jitter_ms": m.jitter_ms,
        "loss": m.loss,
        "loss_equivalent_ms": tramo_core::loss_equivalent_ms(m.loss),
        "instability": m.instability,
        "score": tramo_core::score_route(&m, load, geo, &weights),
    });
    println!("{}", serde_json::to_string_pretty(&out).map_err(|e| e.to_string())?);
    Ok(())
}
