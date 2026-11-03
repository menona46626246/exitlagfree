import { useEffect, useState } from 'react';
import MetricChart from './components/MetricChart';
import RouteCard from './components/RouteCard';
import ProfileList from './components/ProfileList';
import { backendStatus, DEMO_DIAGNOSTICS, DEMO_SERIES, setMode } from './api';
import type { Mode, RouteScore, StatusView } from './types';

const DEMO_STATUS: StatusView = {
  mode: 'MeasureOnly',
  optimization_enabled: false,
  active_route: 'direct',
  kill_switch: false,
  telemetry_enabled: false,
  logging_enabled: false,
  nodes: [
    { id: 'direct', name: 'Ruta directa', region_label: null, priority: 0, enabled: true },
    { id: 'wg-lab-1', name: 'Relay de laboratorio', region_label: 'lab-local', priority: 0, enabled: true },
  ],
  disclaimer:
    'Un relay siempre añade propagación y encapsulado. Tramo solo cambia de ruta si la medición lo justifica; no garantiza menor ping.',
};

const DEMO_SCORES: RouteScore[] = [
  { route: 'direct', score: 26.4, eligible: true, reason: '' },
  { route: 'wg-lab-1', score: 38.9, eligible: true, reason: '' },
];

export default function App() {
  const [status, setStatus] = useState<StatusView>(DEMO_STATUS);
  const [connected, setConnected] = useState(false);
  const [mode, setModeState] = useState<Mode>('measure_only');
  const [theme, setTheme] = useState<'dark' | 'light'>('dark');
  const [notice, setNotice] = useState('');

  useEffect(() => {
    void backendStatus().then((s) => {
      if (s) {
        setStatus(s);
        setConnected(true);
      }
    });
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  const changeMode = async (next: Mode) => {
    setModeState(next);
    if (connected) {
      const ok = await setMode(next);
      setNotice(ok ? `Modo ${next} aplicado` : 'No se pudo aplicar el modo en el motor');
    } else {
      setNotice('Motor no conectado: cambio solo en la vista (Fase 6)');
    }
  };

  const toggle = () => {
    if (mode === 'measure_only') {
      setNotice('En modo «solo medir» no se modifican rutas. Selecciona «sugerir» o «auto cambiar».');
      return;
    }
    setNotice('El plano de control se conecta en la Fase 3 del MVP.');
  };

  return (
    <div className="app">
      <header className="topbar">
        <div>
          <h1>Tramo</h1>
          <p className="muted">Medición de red auditable · Apache-2.0</p>
        </div>
        <div className="top-actions">
          <button className="ghost" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>
            Tema: {theme === 'dark' ? 'oscuro' : 'claro'}
          </button>
          <span className={connected ? 'pill ok' : 'pill warn'}>
            {connected ? 'Motor conectado' : 'Vista de demostración (motor no conectado)'}
          </span>
        </div>
      </header>

      {!connected && (
        <div className="banner">
          Los valores mostrados son de <strong>demostración local</strong>, no mediciones reales.
          El puente IPC con el motor Rust llega en la Fase 6. No se envía ningún dato a terceros.
        </div>
      )}

      <section className="controls card">
        <div className="control-group">
          <label>Modo</label>
          <div className="segmented">
            {(['measure_only', 'suggest', 'auto'] as Mode[]).map((m) => (
              <button key={m} className={mode === m ? 'active' : ''} onClick={() => void changeMode(m)}>
                {{ measure_only: 'Solo medir', suggest: 'Sugerir', auto: 'Auto cambiar' }[m]}
              </button>
            ))}
          </div>
        </div>
        <div className="control-group">
          <label>Optimización</label>
          <button className="toggle" onClick={toggle} aria-pressed={status.optimization_enabled}>
            {status.optimization_enabled ? 'Activa' : 'Desactivada'}
          </button>
        </div>
        <div className="control-group">
          <label>Privacidad</label>
          <span className="muted">
            Telemetría {status.telemetry_enabled ? 'activa' : 'desactivada'} · logs {status.logging_enabled ? 'activos' : 'desactivados'}
          </span>
        </div>
      </section>

      {notice && <div className="notice" role="status">{notice}</div>}

      <div className="grid">
        <MetricChart points={DEMO_SERIES} label="Latencia p50" value={(p) => p.p50_ms} unit="ms" color="#4fd1c5" />
        <MetricChart points={DEMO_SERIES} label="Jitter" value={(p) => p.jitter_ms} unit="ms" color="#f6ad55" />
        <MetricChart points={DEMO_SERIES} label="Pérdida" value={(p) => p.loss * 100} unit="%" color="#fc8181" />
        <RouteCard status={status} scores={DEMO_SCORES} />
        <ProfileList status={status} profileName="Juego genérico (ejemplo)" />
        <section className="card">
          <header className="card-head"><h3>Diagnóstico</h3></header>
          <ul className="diag-list">
            {DEMO_DIAGNOSTICS.map((d) => (
              <li key={d.name}>
                <span className={d.ok === true ? 'ok' : d.ok === false ? 'off' : 'warn'}>●</span>
                <strong>{d.name}</strong>
                <span className="muted">{d.detail}</span>
                {d.requires_privilege && <span className="badge">requiere permisos</span>}
              </li>
            ))}
          </ul>
          <p className="muted">Ejecuta <code>tramoctl diagnose --target host:puerto</code> para datos reales.</p>
        </section>
      </div>

      <footer className="footer">
        <span>Sin cuentas · sin telemetría por defecto · logs locales opcionales · configuración exportable</span>
        <span>IPv4 obligatorio · IPv6 deseable · Windows y Linux</span>
      </footer>
    </div>
  );
}
