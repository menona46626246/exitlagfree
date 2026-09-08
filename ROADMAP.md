# ROADMAP — GameRoute Optimizer

Mejoras futuras (sin compromiso de fecha; ordenadas por valor/riesgo).

## v0.9 (actual, en desarrollo)
- [x] Andamiaje multi-proyecto + CI
- [ ] Modo diagnóstico completo (probes, traceroute, scoring)
- [ ] Gestión de juegos y relays
- [ ] Túnel WireGuard (rutas por destino y global) con restauración
- [ ] Auto-optimización con histéresis y failover
- [ ] Documentación completa

## v1.0 — primera versión estable
- [ ] Instalador firmado (Inno Setup + certificado propio; hoy script listo, firma no)
- [ ] Paquete portable autocontenido por release (win-x64)
- [ ] Símbolo de aplicación propio y branding completo (hoy: nombre propio, sin assets de terceros)
- [ ] Pruebas en hardware/ISP reales y tabla de casos documentados

## v1.1+
- [ ] Servicio residente opcional con inicio automático y arranque a nivel de sistema (hoy: helper bajo demanda)
- [ ] Historial de relays con disponibilidad a largo plazo (p95/p99 históricos por hora del día)
- [ ] Exportación de informes PDF/HTML con gráficos
- [ ] Integración de LiveCharts2/ScottPlot evaluada para gráficos avanzados
- [ ] Soporte de túnel por procesos vía API oficial de WireGuard (AllowedIPs por proceso no es posible sin drivers; evaluar)
- [ ] WireGuard sobre UDP con keepalive configurable por relay
- [ ] Detección heurística de congestión local (WiFi: señal, retransmisiones) con avisos claros
- [ ] Más idiomas (en/…) con recursos satélite
- [ ] Windows on ARM (probado)
- [ ] Scripts de prueba de humo automáticos contra endpoints públicos opt-in (el usuario elige el host)
- [ ] Integración continua con artefactos de release firmados con hash publicado

## No planificado (explícitamente fuera de alcance)
- Nube/backend, cuentas, relays de terceros, telemetría, publicidad.
- Inyección en juegos, lectura de memoria, hooking, bypass de anti-cheat.
- Evasión de restricciones regionales o políticas de servicio.
