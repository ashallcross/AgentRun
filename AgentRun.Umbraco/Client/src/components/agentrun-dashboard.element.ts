import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { customElement, html, css } from "@umbraco-cms/backoffice/external/lit";

import type { UmbRoute } from "@umbraco-cms/backoffice/router";

@customElement("agentrun-dashboard")
export class AgentRunDashboardElement extends UmbLitElement {
  private _routes: UmbRoute[] = [
    {
      path: "workflows/:alias",
      component: () => import("./agentrun-instance-list.element.js"),
      setup: () => {},
    },
    {
      path: "workflows",
      component: () => import("./agentrun-workflow-list.element.js"),
      setup: () => {},
    },
    {
      path: "instances/:id",
      component: () => import("./agentrun-instance-detail.element.js"),
      setup: () => {},
    },
    {
      path: "",
      redirectTo: "workflows",
    },
  ];

  static styles = css`
    :host {
      display: block;
    }
  `;

  render() {
    return html`
      <umb-body-layout headline="Agent Workflows">
        <umb-router-slot .routes=${this._routes}></umb-router-slot>
      </umb-body-layout>
    `;
  }
}

export default AgentRunDashboardElement;
