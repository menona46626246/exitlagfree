import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Sin proxies a servicios externos, sin analytics ni fuentes remotas.
// host 0.0.0.0 y allowedHosts para el preview de desarrollo en contenedores.
export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: true,
    allowedHosts: true,
  },
  build: {
    target: 'es2020',
    outDir: 'dist',
    sourcemap: false,
  },
});
