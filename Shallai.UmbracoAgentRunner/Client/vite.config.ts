import { defineConfig } from "vite";

export default defineConfig({
  build: {
    lib: {
      entry: "src/index.ts",
      formats: ["es"],
      fileName: "shallai-umbraco-agent-runner",
    },
    outDir: "../wwwroot/App_Plugins/ShallaiUmbracoAgentRunner",
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      external: [/^@umbraco/],
    },
  },
  publicDir: "public",
});
