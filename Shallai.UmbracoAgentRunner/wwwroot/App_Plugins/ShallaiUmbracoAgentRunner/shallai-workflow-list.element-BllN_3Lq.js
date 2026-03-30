import { UmbLitElement as n } from "@umbraco-cms/backoffice/lit-element";
import { html as u, css as f, customElement as m } from "@umbraco-cms/backoffice/external/lit";
var p = Object.getOwnPropertyDescriptor, w = (o, r, i, s) => {
  for (var e = s > 1 ? void 0 : s ? p(r, i) : r, t = o.length - 1, a; t >= 0; t--)
    (a = o[t]) && (e = a(e) || e);
  return e;
};
let l = class extends n {
  render() {
    return u`
      <uui-box headline="Workflows">
        <p>Workflow list view</p>
      </uui-box>
    `;
  }
};
l.styles = f`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }
  `;
l = w([
  m("shallai-workflow-list")
], l);
const h = l;
export {
  l as ShallaiWorkflowListElement,
  h as default
};
//# sourceMappingURL=shallai-workflow-list.element-BllN_3Lq.js.map
