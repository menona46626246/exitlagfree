import type { StatusView, DiagnosticResult } from './types';

/**
 * Adaptador hacia el runtime Tauri. Hasta la Fase 6 no existe el puente IPC:
 * devolvemos `null` y la UI lo indica explicitamente. Nunca se inventan datos
 * como si fueran mediciones reales.
 */
export async function backendStatus(): Promise<StatusView | null> {
  const internals = (window as unknown as {
    __TAURI_INTERNALS__?: { invoke?: (cmd: string, args?: unknown) => Promise<unknown> };
  }).__TAURI_INTERNALS__;
  if (!internals?.invoke) return null;
  try {
    return (await internals.invoke('status')) as StatusView;
  } catch {
    return null;
  }
}

export async function setMode(mode: string): Promise<boolean> {
  const internals = (window as unknown as {
    __TAURI_INTERNALS__?: { invoke?: (cmd: string, args?: unknown) => Promise<unknown> };
  }).__TAURI_INTERNALS__;
  if (!internals?.invoke) return false;
  try {
    await internals.invoke('set_mode', { mode });
    return true;
  } catch {
    return false;
  }
}

/** Serie de DEMOSTRACION, claramente etiquetada en la UI. No es telemetria. */
export const DEMO_SERIES: Array<{ at_ms: number; p50_ms: number; jitter_ms: number; loss: number }> =
  Array.from({ length: 60 }, (_, i) => ({
    at_ms: i * 1000,
    p50_ms: 24 + Math.sin(i / 6) * 3 + (i % 11 === 0 ? 9 : 0),
    jitter_ms: 1.5 + Math.abs(Math.sin(i / 4)) * 2,
    loss: i % 17 === 0 ? 0.02 : 0,
  }));

export const DEMO_DIAGNOSTICS: DiagnosticResult[] = [
  { name: 'udp_probe', ok: true, detail: 'Sondas UDP/TCP sin privilegios', requires_privilege: false },
  { name: 'icmp_raw', ok: null, detail: 'Requiere CAP_NET_RAW; se degrada a UDP/TCP', requires_privilege: true },
  { name: 'tun', ok: null, detail: 'Requiere UAC / polkit + CAP_NET_ADMIN', requires_privilege: true },
];
