import { UmbLitElement as l } from "@umbraco-cms/backoffice/lit-element";
import { html as m, css as p, customElement as c } from "@umbraco-cms/backoffice/external/lit";
var i = Object.getOwnPropertyDescriptor, d = (r, s, u, n) => {
  for (var t = n > 1 ? void 0 : n ? i(s, u) : s, o = r.length - 1, a; o >= 0; o--)
    (a = r[o]) && (t = a(t) || t);
  return t;
};
let e = class extends l {
  constructor() {
    super(...arguments), this._routes = [
      {
        path: "workflows/:alias",
        component: () => import("./agentrun-instance-list.element-C87eGtr4.js"),
        setup: () => {
        }
      },
      {
        path: "workflows",
        component: () => import("./agentrun-workflow-list.element-BKfYCXHx.js"),
        setup: () => {
        }
      },
      {
        path: "instances/:id",
        component: () => import("./agentrun-instance-detail.element-C9FDJOy6.js"),
        setup: () => {
        }
      },
      {
        path: "",
        redirectTo: "workflows"
      }
    ];
  }
  render() {
    return m`
      <umb-body-layout headline="Agent Workflows">
        <umb-router-slot .routes=${this._routes}></umb-router-slot>
      </umb-body-layout>
    `;
  }
};
e.styles = p`
    :host {
      display: block;
    }
  `;
e = d([
  c("agentrun-dashboard")
], e);
const f = e;
export {
  e as AgentRunDashboardElement,
  f as default
};
//# sourceMappingURL=agentrun-dashboard.element-siPAiwEJ.js.map
