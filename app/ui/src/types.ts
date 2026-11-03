export type Mode = 'measure_only' | 'suggest' | 'auto';

export interface NodeView {
  id: string;
  name: string;
  region_label: string | null;
  priority: number;
  enabled: boolean;
}

export interface StatusView {
  mode: string;
  optimization_enabled: boolean;
  active_route: string;
  kill_switch: boolean;
  telemetry_enabled: boolean;
  logging_enabled: boolean;
  nodes: NodeView[];
  disclaimer: string;
}

export interface MetricPoint {
  at_ms: number;
  p50_ms: number;
  jitter_ms: number;
  loss: number;
}

export interface RouteScore {
  route: string;
  score: number;
  eligible: boolean;
  reason: string;
}

export interface DiagnosticResult {
  name: string;
  ok: boolean | null;
  detail: string;
  requires_privilege: boolean;
}
