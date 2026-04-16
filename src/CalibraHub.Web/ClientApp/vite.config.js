import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/Logistics': {
        target: 'https://localhost:61001',
        changeOrigin: true,
        secure: false
      }
    }
  },
  build: {
    outDir: '../wwwroot/react',
    emptyOutDir: true,
    rollupOptions: {
      input: 'src/mount.jsx',
      output: {
        entryFileNames: 'calibrahub-widgets.js',
        assetFileNames: 'calibrahub-widgets.[ext]',
        format: 'iife',
      }
    },
    cssCodeSplit: false,
  }
})
