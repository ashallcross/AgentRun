import { UmbLitElement as w } from "@umbraco-cms/backoffice/lit-element";
import { html as s, css as p, state as h, customElement as _ } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as v } from "@umbraco-cms/backoffice/auth";
const k = "/umbraco/api/shallai";
async function b(e, t) {
  const a = { Accept: "application/json" };
  t && (a.Authorization = `Bearer ${t}`);
  const o = await fetch(`${k}${e}`, { headers: a });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
  return o.json();
}
function y(e) {
  return b("/workflows", e);
}
var m = Object.defineProperty, g = Object.getOwnPropertyDescriptor, d = (e) => {
  throw TypeError(e);
}, u = (e, t, a, o) => {
  for (var r = o > 1 ? void 0 : o ? g(t, a) : t, n = e.length - 1, c; n >= 0; n--)
    (c = e[n]) && (r = (o ? c(t, a, r) : c(r)) || r);
  return o && r && m(t, a, r), r;
}, f = (e, t, a) => t.has(e) || d("Cannot " + a), $ = (e, t, a) => (f(e, t, "read from private field"), a ? a.call(e) : t.get(e)), C = (e, t, a) => t.has(e) ? d("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, a), W = (e, t, a, o) => (f(e, t, "write to private field"), t.set(e, a), a), i;
let l = class extends w {
  constructor() {
    super(), C(this, i), this._workflows = [], this._loading = !0, this._error = !1, this.consumeContext(v, (e) => {
      W(this, i, e);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadWorkflows();
  }
  async _loadWorkflows() {
    try {
      const e = await $(this, i)?.getLatestToken();
      this._workflows = await y(e), this._error = !1;
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
    return this._loading ? s`<uui-loader></uui-loader>` : this._error ? s`
        <div class="error-state">
          Failed to load workflows. Check that you have backoffice access and
          try refreshing the page.
        </div>
      ` : this._workflows.length === 0 ? s`
        <div class="empty-state">
          No workflows found. Add a workflow folder to your project's workflows
          directory.
        </div>
      ` : s`
      <uui-table>
        <uui-table-head>
          <uui-table-head-cell>Name</uui-table-head-cell>
          <uui-table-head-cell style="width: 100px;">Steps</uui-table-head-cell>
          <uui-table-head-cell style="width: 120px;">Mode</uui-table-head-cell>
        </uui-table-head>
        ${this._workflows.map(
      (e) => s`
            <uui-table-row @click=${() => this._navigateToWorkflow(e.alias)}>
              <uui-table-cell>
                <span class="workflow-name">${e.name}</span>
              </uui-table-cell>
              <uui-table-cell>${e.stepCount} steps</uui-table-cell>
              <uui-table-cell>
                <uui-badge>${e.mode}</uui-badge>
              </uui-table-cell>
            </uui-table-row>
          `
    )}
      </uui-table>
    `;
  }
};
i = /* @__PURE__ */ new WeakMap();
l.styles = p`
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
  h()
], l.prototype, "_workflows", 2);
u([
  h()
], l.prototype, "_loading", 2);
u([
  h()
], l.prototype, "_error", 2);
l = u([
  _("shallai-workflow-list")
], l);
const P = l;
export {
  l as ShallaiWorkflowListElement,
  P as default
};
//# sourceMappingURL=shallai-workflow-list.element-DDeE5c60.js.map
