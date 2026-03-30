export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "section",
    alias: "Shallai.UmbracoAgentRunner.Section",
    name: "Agent Workflows Section",
    meta: {
      label: "Agent Workflows",
      pathname: "agent-workflows",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionUserPermission",
        match: "Shallai.UmbracoAgentRunner.Section",
      },
    ],
  },
  {
    type: "menu",
    alias: "Shallai.UmbracoAgentRunner.Menu",
    name: "Agent Workflows Menu",
  },
  {
    type: "sectionSidebarApp",
    kind: "menu",
    alias: "Shallai.UmbracoAgentRunner.SectionSidebarApp.Menu",
    name: "Agent Workflows Sidebar Menu",
    meta: {
      label: "Agent Workflows",
      menu: "Shallai.UmbracoAgentRunner.Menu",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "Shallai.UmbracoAgentRunner.Section",
      },
    ],
  },
  {
    type: "sectionView",
    alias: "Shallai.UmbracoAgentRunner.SectionView.Dashboard",
    name: "Agent Workflows Dashboard View",
    element: () => import("./components/shallai-dashboard.element.js"),
    meta: {
      label: "Overview",
      pathname: "overview",
      icon: "icon-dashboard",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "Shallai.UmbracoAgentRunner.Section",
      },
    ],
  },
];
