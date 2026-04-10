import { UmbLitElement as p } from "@umbraco-cms/backoffice/lit-element";
import { html as s, css as w, state as d, customElement as _ } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as v } from "@umbraco-cms/backoffice/auth";
import { g as k } from "./index-BHPGdLBM.js";
var g = Object.defineProperty, m = Object.getOwnPropertyDescriptor, h = (e) => {
  throw TypeError(e);
}, u = (e, t, o, a) => {
  for (var r = a > 1 ? void 0 : a ? m(t, o) : t, n = e.length - 1, c; n >= 0; n--)
    (c = e[n]) && (r = (a ? c(t, o, r) : c(r)) || r);
  return a && r && g(t, o, r), r;
}, f = (e, t, o) => t.has(e) || h("Cannot " + o), y = (e, t, o) => (f(e, t, "read from private field"), o ? o.call(e) : t.get(e)), b = (e, t, o) => t.has(e) ? h("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, o), C = (e, t, o, a) => (f(e, t, "write to private field"), t.set(e, o), o), i;
let l = class extends p {
  constructor() {
    super(), b(this, i), this._workflows = [], this._loading = !0, this._error = !1, this.consumeContext(v, (e) => {
      C(this, i, e);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadWorkflows();
  }
  async _loadWorkflows() {
    try {
      const e = await y(this, i)?.getLatestToken();
      this._workflows = await k(e), this._error = !1;
    } catch {
      this._workflows = [], this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateToWorkflow(e) {
    const t = window.location.pathname, o = encodeURIComponent(e), a = t.endsWith("/") ? `${t}${o}` : `${t}/${o}`;
    window.history.pushState({}, "", a);
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
                <uui-tag>${e.mode}</uui-tag>
              </uui-table-cell>
            </uui-table-row>
          `
    )}
      </uui-table>
    `;
  }
};
i = /* @__PURE__ */ new WeakMap();
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
  _("agentrun-workflow-list")
], l);
const T = l;
export {
  l as AgentRunWorkflowListElement,
  T as default
};
//# sourceMappingURL=agentrun-workflow-list.element-BRcT9EFJ.js.map
