import { UmbLitElement as p } from "@umbraco-cms/backoffice/lit-element";
import { html as i, css as w, state as d, customElement as _ } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as v } from "@umbraco-cms/backoffice/auth";
import { g as k } from "./api-client-CgFnA5nu.js";
var m = Object.defineProperty, y = Object.getOwnPropertyDescriptor, h = (e) => {
  throw TypeError(e);
}, u = (e, t, a, o) => {
  for (var r = o > 1 ? void 0 : o ? y(t, a) : t, n = e.length - 1, c; n >= 0; n--)
    (c = e[n]) && (r = (o ? c(t, a, r) : c(r)) || r);
  return o && r && m(t, a, r), r;
}, f = (e, t, a) => t.has(e) || h("Cannot " + a), g = (e, t, a) => (f(e, t, "read from private field"), a ? a.call(e) : t.get(e)), b = (e, t, a) => t.has(e) ? h("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, a), C = (e, t, a, o) => (f(e, t, "write to private field"), t.set(e, a), a), s;
let l = class extends p {
  constructor() {
    super(), b(this, s), this._workflows = [], this._loading = !0, this._error = !1, this.consumeContext(v, (e) => {
      C(this, s, e);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadWorkflows();
  }
  async _loadWorkflows() {
    try {
      const e = await g(this, s)?.getLatestToken();
      this._workflows = await k(e), this._error = !1;
    } catch {
      this._workflows = [], this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateToWorkflow(e) {
    const t = window.location.pathname, a = encodeURIComponent(e), o = t.endsWith("/") ? `${t}${a}` : `${t}/${a}`;
    window.history.pushState({}, "", o);
  }
  render() {
    return this._loading ? i`<uui-loader></uui-loader>` : this._error ? i`
        <div class="error-state">
          Failed to load workflows. Check that you have backoffice access and
          try refreshing the page.
        </div>
      ` : this._workflows.length === 0 ? i`
        <div class="empty-state">
          No workflows found. Add a workflow folder to your project's workflows
          directory.
        </div>
      ` : i`
      <uui-table>
        <uui-table-head>
          <uui-table-head-cell>Name</uui-table-head-cell>
          <uui-table-head-cell style="width: 100px;">Steps</uui-table-head-cell>
          <uui-table-head-cell style="width: 120px;">Mode</uui-table-head-cell>
        </uui-table-head>
        ${this._workflows.map(
      (e) => i`
            <uui-table-row @click=${() => this._navigateToWorkflow(e.alias)}>
              <uui-table-cell>
                <span class="workflow-name">${e.name}</span>
              </uui-table-cell>
              <uui-table-cell>${e.stepCount} steps</uui-table-cell>
              <uui-table-cell>
                <uui-tag>${e.mode}</uui-tag>
              </uui-table-cell>
            </uui-table-row>
          `
    )}
      </uui-table>
    `;
  }
};
s = /* @__PURE__ */ new WeakMap();
l.styles = w`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    uui-table-row {
      cursor: pointer;
    }

    uui-table-row:hover {
      background-color: var(--uui-color-surface-emphasis);
    }

    .workflow-name {
      color: var(--uui-color-interactive);
    }

    .empty-state,
    .error-state {
      padding: var(--uui-size-layout-1);
      color: var(--uui-color-text);
      text-align: center;
    }
  `;
u([
  d()
], l.prototype, "_workflows", 2);
u([
  d()
], l.prototype, "_loading", 2);
u([
  d()
], l.prototype, "_error", 2);
l = u([
  _("shallai-workflow-list")
], l);
const T = l;
export {
  l as ShallaiWorkflowListElement,
  T as default
};
//# sourceMappingURL=shallai-workflow-list.element-BV9SVhhr.js.map
