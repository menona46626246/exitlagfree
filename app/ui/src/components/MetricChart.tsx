import type { MetricPoint } from '../types';

interface Props {
  points: MetricPoint[];
  label: string;
  value: (p: MetricPoint) => number;
  unit: string;
  color: string;
}

/** Grafica SVG sin librerias externas: menos CPU/RAM y cero dependencias. */
export default function MetricChart({ points, label, value, unit, color }: Props) {
  const width = 520;
  const height = 120;
  const pad = 8;
  const values = points.map(value);
  const max = Math.max(1, ...values) * 1.15;
  const min = Math.min(0, ...values);
  const span = Math.max(1, max - min);
  const toPath = (v: (p: MetricPoint) => number) =>
    points
      .map((p, i) => {
        const x = pad + (i / Math.max(1, points.length - 1)) * (width - pad * 2);
        const y = height - pad - ((v(p) - min) / span) * (height - pad * 2);
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  const last = points.length > 0 ? points[points.length - 1] : undefined;
  return (
    <section className="card chart" aria-label={`Grafica de ${label}`}>
      <header className="card-head">
        <h3>{label}</h3>
        <strong style={{ color }}>
          {last ? value(last).toFixed(2) : '—'} {unit}
        </strong>
      </header>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label} preserveAspectRatio="none">
        <line x1={pad} y1={height - pad} x2={width - pad} y2={height - pad} className="axis" />
        <path d={toPath(value)} fill="none" stroke={color} strokeWidth={2} vectorEffect="non-scaling-stroke" />
      </svg>
      <small>p50 / jitter / pérdida en ventana móvil · {points.length} muestras</small>
    </section>
  );
}
