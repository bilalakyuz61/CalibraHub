import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../wwwroot/__tmp_drag',
    emptyOutDir: true,
    rollupOptions: {
      input: 'src/__dragtest.jsx',
      output: { entryFileNames: 'dt.js', assetFileNames: 'dt.[ext]', format: 'iife' },
    },
    cssCodeSplit: false,
  },
})
