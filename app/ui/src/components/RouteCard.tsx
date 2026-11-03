import type { RouteScore, StatusView } from '../types';

interface Props {
  status: StatusView;
  scores: RouteScore[];
}

export default function RouteCard({ status, scores }: Props) {
  const recommended = [...scores].filter((s) => s.eligible).sort((a, b) => a.score - b.score)[0];
  return (
    <section className="card">
      <header className="card-head">
        <h3>Ruta activa</h3>
        <span className="badge">{status.active_route}</span>
      </header>
      <p className="muted">{status.disclaimer}</p>
      <table className="scores">
        <thead>
          <tr><th>Ruta</th><th>Score (ms eq.)</th><th>Estado</th></tr>
        </thead>
        <tbody>
          {scores.map((s) => (
            <tr key={s.route} className={s.route === status.active_route ? 'active' : ''}>
              <td>{s.route}</td>
              <td>{Number.isFinite(s.score) ? s.score.toFixed(1) : '—'}</td>
              <td>{s.eligible ? 'elegible' : s.reason || 'no elegible'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="muted">
        Recomendado: <strong>{recommended ? recommended.route : 'sin candidatos elegibles'}</strong>.
        Solo se aplica en modo «auto cambiar» y tras superar histéresis y cooldown.
      </p>
    </section>
  );
}
