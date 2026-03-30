export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Shallai Umbraco Agent Runner Entrypoint",
    alias: "Shallai.UmbracoAgentRunner.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },
];
