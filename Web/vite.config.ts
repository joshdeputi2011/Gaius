import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
export default defineConfig({
  plugins: [react()],
  server: { fs: { allow: [".."] } },
  build: {
    rollupOptions: { output: { manualChunks: { "pdf-tools": ["pdf-lib"] } } },
  },
});
