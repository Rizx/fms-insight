/// <reference types="vitest" />
/// <reference types="vite/client" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import legacy from "@vitejs/plugin-legacy";

export default defineConfig({
  root: "src",
  plugins: [react(), legacy({ targets: ["defaults", "not IE 11", "edge 18"] })],
  base: "/",
  server: {
    port: 1234,
  },
  build: {
    outDir: "../dist", // relative to root
    chunkSizeWarningLimit: 1800,
    emptyOutDir: true,
  },
  test: {
    environment: "jsdom",
    setupFiles: ["test/setup.ts"],
  },
});
