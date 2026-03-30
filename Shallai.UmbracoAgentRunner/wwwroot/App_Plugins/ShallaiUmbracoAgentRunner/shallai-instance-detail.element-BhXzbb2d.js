import { UmbLitElement as o } from "@umbraco-cms/backoffice/lit-element";
import { html as u, css as c, customElement as m } from "@umbraco-cms/backoffice/external/lit";
var p = Object.getOwnPropertyDescriptor, d = (l, n, r, s) => {
  for (var e = s > 1 ? void 0 : s ? p(n, r) : n, a = l.length - 1, i; a >= 0; a--)
    (i = l[a]) && (e = i(e) || e);
  return e;
};
let t = class extends o {
  render() {
    return u`
      <uui-box headline="Instance Detail">
        <p>Instance detail view</p>
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
  m("shallai-instance-detail")
], t);
const f = t;
export {
  t as ShallaiInstanceDetailElement,
  f as default
};
//# sourceMappingURL=shallai-instance-detail.element-BhXzbb2d.js.map
