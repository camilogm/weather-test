/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    host: true, // reachable from outside the container
  },
  preview: {
    port: 4173,
    host: true,
  },
  test: {
    // The hooks under test own timers, aborts and DOM-dependent effects, so
    // they need a document rather than a mock of one.
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Globals stay off: importing describe/it/expect keeps the test files
    // honest about where their vocabulary comes from, and keeps the app's
    // tsconfig from having to widen its `types` array for production code.
    globals: false,
  },
})
