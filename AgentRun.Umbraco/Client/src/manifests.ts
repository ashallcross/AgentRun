export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "section",
    alias: "AgentRun.Umbraco.Section",
    name: "Agent Workflows Section",
    meta: {
      label: "Agent Workflows",
      pathname: "agent-workflows",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionUserPermission",
        match: "AgentRun.Umbraco.Section",
      },
    ],
  },
  {
    type: "menu",
    alias: "AgentRun.Umbraco.Menu",
    name: "Agent Workflows Menu",
  },
  {
    type: "sectionSidebarApp",
    kind: "menu",
    alias: "AgentRun.Umbraco.SectionSidebarApp.Menu",
    name: "Agent Workflows Sidebar Menu",
    meta: {
      label: "Agent Workflows",
      menu: "AgentRun.Umbraco.Menu",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "AgentRun.Umbraco.Section",
      },
    ],
  },
  {
    type: "sectionView",
    alias: "AgentRun.Umbraco.SectionView.Dashboard",
    name: "Agent Workflows Dashboard View",
    element: () => import("./components/agentrun-dashboard.element.js"),
    meta: {
      label: "Overview",
      pathname: "overview",
      icon: "icon-dashboard",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "AgentRun.Umbraco.Section",
      },
    ],
  },
];
