import { UmbLitElement as w } from "@umbraco-cms/backoffice/lit-element";
import { html as n, nothing as f, css as m, state as l, customElement as v } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as b } from "@umbraco-cms/backoffice/auth";
import { g, a as k, c as y } from "./index-D-gGkm5y.js";
import { e as C, n as $, b as A, a as S, i as x, s as I, d as T, r as L } from "./instance-list-helpers-JWQgi_HM.js";
var M = Object.defineProperty, N = Object.getOwnPropertyDescriptor, p = (t) => {
  throw TypeError(t);
}, r = (t, e, a, o) => {
  for (var s = o > 1 ? void 0 : o ? N(e, a) : e, c = t.length - 1, h; c >= 0; c--)
    (h = t[c]) && (s = (o ? h(e, a, s) : h(s)) || s);
  return o && s && M(e, a, s), s;
}, _ = (t, e, a) => e.has(t) || p("Cannot " + a), d = (t, e, a) => (_(t, e, "read from private field"), a ? a.call(t) : e.get(t)), E = (t, e, a) => e.has(t) ? p("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, a), P = (t, e, a, o) => (_(t, e, "write to private field"), e.set(t, a), a), u;
let i = class extends w {
  constructor() {
    super(), E(this, u), this._instances = [], this._workflowName = "", this._workflowAlias = "", this._stepCount = 0, this._loading = !0, this._error = !1, this._creating = !1, this._workflowMode = "interactive", this.consumeContext(b, (t) => {
      P(this, u, t);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._workflowAlias = C(window.location.pathname), this._loadData();
  }
  async _loadData() {
    try {
      const t = await d(this, u)?.getLatestToken(), [e, a] = await Promise.all([
        g(t),
        k(this._workflowAlias, t)
      ]), o = e.find((s) => s.alias === this._workflowAlias);
      this._workflowName = o?.name ?? this._workflowAlias, this._stepCount = o?.stepCount ?? 0, this._workflowMode = o?.mode ?? "interactive", this._instances = $(a), this._error = !1;
    } catch {
      this._instances = [], this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateToInstance(t) {
    window.history.pushState({}, "", A(t, window.location.pathname));
  }
  _navigateBack() {
    window.history.pushState({}, "", S(window.location.pathname));
  }
  async _createNewRun() {
    if (!this._creating) {
      this._creating = !0;
      try {
        const t = await d(this, u)?.getLatestToken(), e = await y(this._workflowAlias, t);
        this._navigateToInstance(e.id);
      } finally {
        this._creating = !1;
      }
    }
  }
  _formatStep(t) {
    return t.status === "Completed" ? "Complete" : this._stepCount > 0 ? `${t.currentStepIndex + 1} of ${this._stepCount}` : `Step ${t.currentStepIndex + 1}`;
  }
  render() {
    if (this._loading)
      return n`<uui-loader></uui-loader>`;
    if (this._error)
      return n`
        <div class="error-state">
          Failed to load instances. Check that you have backoffice access and
          try refreshing the page.
        </div>
      `;
    const t = x(this._workflowMode);
    return n`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${this._workflowName}</h2>
        <uui-button
          label=${t.newButton}
          look="primary"
          color="positive"
          ?disabled=${this._creating}
          @click=${this._createNewRun}
        >
          ${t.newButton}
        </uui-button>
      </div>

      ${this._instances.length === 0 ? n`<div class="empty-state">${t.emptyState}</div>` : n`
            <uui-table>
              <uui-table-head>
                <uui-table-head-cell style="width: 80px;">Run</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Status</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Step</uui-table-head-cell>
                <uui-table-head-cell>Started</uui-table-head-cell>
              </uui-table-head>
              ${this._instances.map(
      (e) => n`
                  <uui-table-row @click=${() => this._navigateToInstance(e.id)}>
                    <uui-table-cell>#${e.runNumber}</uui-table-cell>
                    <uui-table-cell>
                      <uui-tag .color=${I(e.status, this._workflowMode) ?? f}>
                        ${T(e.status, this._workflowMode)}
                      </uui-tag>
                    </uui-table-cell>
                    <uui-table-cell>${this._formatStep(e)}</uui-table-cell>
                    <uui-table-cell title=${e.createdAt}>
                      ${L(e.createdAt)}
                    </uui-table-cell>
                  </uui-table-row>
                `
    )}
            </uui-table>
          `}
    `;
  }
};
u = /* @__PURE__ */ new WeakMap();
i.styles = m`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      margin-bottom: var(--uui-size-layout-1);
    }

    .header h2 {
      flex: 1;
      margin: 0;
    }

    uui-table-row {
      cursor: pointer;
    }

    uui-table-row:hover {
      background-color: var(--uui-color-surface-emphasis);
    }

    .empty-state,
    .error-state {
      padding: var(--uui-size-layout-1);
      color: var(--uui-color-text);
      text-align: center;
    }
  `;
r([
  l()
], i.prototype, "_instances", 2);
r([
  l()
], i.prototype, "_workflowName", 2);
r([
  l()
], i.prototype, "_workflowAlias", 2);
r([
  l()
], i.prototype, "_stepCount", 2);
r([
  l()
], i.prototype, "_loading", 2);
r([
  l()
], i.prototype, "_error", 2);
r([
  l()
], i.prototype, "_creating", 2);
r([
  l()
], i.prototype, "_workflowMode", 2);
i = r([
  v("agentrun-instance-list")
], i);
const D = i;
export {
  i as AgentRunInstanceListElement,
  D as default
};
//# sourceMappingURL=agentrun-instance-list.element-CbW0cv8J.js.map
