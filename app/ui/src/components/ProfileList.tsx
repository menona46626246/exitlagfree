import type { NodeView, StatusView } from '../types';

interface Props {
  status: StatusView;
  profileName: string;
}

export default function ProfileList({ status, profileName }: Props) {
  return (
    <section className="card">
      <header className="card-head"><h3>Perfiles y nodos</h3></header>
      <div className="profile-row">
        <span className="badge">{profileName}</span>
        <span className="muted">reglas por CIDR / puerto / protocolo</span>
      </div>
      <ul className="node-list">
        {status.nodes.length === 0 && <li className="muted">Sin nodos configurados (modo offline).</li>}
        {status.nodes.map((n: NodeView) => (
          <li key={n.id}>
            <span>{n.name}</span>
            <span className="muted">{n.region_label ?? 'sin etiqueta'} · prioridad {n.priority}</span>
            <span className={n.enabled ? 'ok' : 'off'}>{n.enabled ? 'activo' : 'desactivado'}</span>
          </li>
        ))}
      </ul>
      <p className="muted">
        Split tunneling por proceso en Windows requiere un driver firmado y está fuera del MVP.
      </p>
    </section>
  );
}
