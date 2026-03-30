import { UmbLitElement as o } from "@umbraco-cms/backoffice/lit-element";
import { html as u, css as c, customElement as m } from "@umbraco-cms/backoffice/external/lit";
var p = Object.getOwnPropertyDescriptor, d = (l, a, r, n) => {
  for (var e = n > 1 ? void 0 : n ? p(a, r) : a, s = l.length - 1, i; s >= 0; s--)
    (i = l[s]) && (e = i(e) || e);
  return e;
};
let t = class extends o {
  render() {
    return u`
      <uui-box headline="Instances">
        <p>Instance list view</p>
      </uui-box>
    `;
  }
};
t.styles = c`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }
  `;
t = d([
  m("shallai-instance-list")
], t);
const f = t;
export {
  t as ShallaiInstanceListElement,
  f as default
};
//# sourceMappingURL=shallai-instance-list.element-D3dzDShQ.js.map
