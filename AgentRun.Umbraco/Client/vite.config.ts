import { defineConfig } from "vite";

export default defineConfig({
  build: {
    lib: {
      entry: "src/index.ts",
      formats: ["es"],
      fileName: "agentrun-umbraco",
    },
    outDir: "../wwwroot/App_Plugins/AgentRunUmbraco",
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      external: [/^@umbraco/],
    },
  },
  publicDir: "public",
});
