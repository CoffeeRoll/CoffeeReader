import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In development the ASP.NET Core API runs on :5200 and Vite proxies to it,
// so the app and API share an origin just like in production.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5200",
      "/health": "http://localhost:5200",
    },
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    chunkSizeWarningLimit: 800,
  },
});
