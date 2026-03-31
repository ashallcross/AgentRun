import { UmbLitElement as u } from "@umbraco-cms/backoffice/lit-element";
import { html as m, css as p, customElement as i } from "@umbraco-cms/backoffice/external/lit";
var h = Object.getOwnPropertyDescriptor, c = (r, s, n, a) => {
  for (var t = a > 1 ? void 0 : a ? h(s, n) : s, o = r.length - 1, l; o >= 0; o--)
    (l = r[o]) && (t = l(t) || t);
  return t;
};
let e = class extends u {
  constructor() {
    super(...arguments), this._routes = [
      {
        path: "workflows/:alias",
        component: () => import("./shallai-instance-list.element-BfAuSlHI.js"),
        setup: () => {
        }
      },
      {
        path: "workflows",
        component: () => import("./shallai-workflow-list.element-CKAgRo-V.js"),
        setup: () => {
        }
      },
      {
        path: "instances/:id",
        component: () => import("./shallai-instance-detail.element-BvVHc1Xr.js"),
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
e = c([
  i("shallai-dashboard")
], e);
const f = e;
export {
  e as ShallaiDashboardElement,
  f as default
};
//# sourceMappingURL=shallai-dashboard.element-DzwgUv5M.js.map
