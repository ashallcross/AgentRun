import { UmbLitElement as f } from "@umbraco-cms/backoffice/lit-element";
import { html as r, nothing as p, css as m, state as n, customElement as b } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as g } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as v } from "@umbraco-cms/backoffice/modal";
import { g as k, a as y, c as $, d as C } from "./api-client-CgFnA5nu.js";
import { e as A, n as S, b as x, a as I, s as T, r as N, i as P } from "./instance-list-helpers-D6jp37V8.js";
var D = Object.defineProperty, L = Object.getOwnPropertyDescriptor, _ = (t) => {
  throw TypeError(t);
}, s = (t, e, a, o) => {
  for (var l = o > 1 ? void 0 : o ? L(e, a) : e, c = t.length - 1, h; c >= 0; c--)
    (h = t[c]) && (l = (o ? h(e, a, l) : h(l)) || l);
  return o && l && D(e, a, l), l;
}, w = (t, e, a) => e.has(t) || _("Cannot " + a), d = (t, e, a) => (w(t, e, "read from private field"), a ? a.call(t) : e.get(t)), E = (t, e, a) => e.has(t) ? _("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, a), R = (t, e, a, o) => (w(t, e, "write to private field"), e.set(t, a), a), u;
let i = class extends f {
  constructor() {
    super(), E(this, u), this._instances = [], this._workflowName = "", this._workflowAlias = "", this._stepCount = 0, this._loading = !0, this._error = !1, this._creating = !1, this._deleting = null, this.consumeContext(g, (t) => {
      R(this, u, t);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._workflowAlias = A(window.location.pathname), this._loadData();
  }
  async _loadData() {
    try {
      const t = await d(this, u)?.getLatestToken(), [e, a] = await Promise.all([
        k(t),
        y(this._workflowAlias, t)
      ]), o = e.find((l) => l.alias === this._workflowAlias);
      this._workflowName = o?.name ?? this._workflowAlias, this._stepCount = o?.stepCount ?? 0, this._instances = S(a), this._error = !1;
    } catch {
      this._instances = [], this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateToInstance(t) {
    window.history.pushState({}, "", x(t, window.location.pathname));
  }
  _navigateBack() {
    window.history.pushState({}, "", I(window.location.pathname));
  }
  async _createNewRun() {
    if (!this._creating) {
      this._creating = !0;
      try {
        const t = await d(this, u)?.getLatestToken(), e = await $(this._workflowAlias, t);
        this._navigateToInstance(e.id);
      } finally {
        this._creating = !1;
      }
    }
  }
  async _deleteInstance(t, e) {
    if (e.stopPropagation(), !this._deleting) {
      try {
        await v(this, {
          headline: "Delete Run",
          content: "Delete this run? This cannot be undone.",
          color: "danger",
          confirmLabel: "Delete"
        });
      } catch {
        return;
      }
      this._deleting = t;
      try {
        const a = await d(this, u)?.getLatestToken();
        await C(t, a), this._instances = this._instances.filter((o) => o.id !== t);
      } finally {
        this._deleting = null;
      }
    }
  }
  _onActionsClick(t) {
    t.stopPropagation();
  }
  _formatStep(t) {
    return t.status === "Completed" ? "Complete" : this._stepCount > 0 ? `${t.currentStepIndex + 1} of ${this._stepCount}` : `Step ${t.currentStepIndex + 1}`;
  }
  render() {
    return this._loading ? r`<uui-loader></uui-loader>` : this._error ? r`
        <div class="error-state">
          Failed to load instances. Check that you have backoffice access and
          try refreshing the page.
        </div>
      ` : r`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${this._workflowName}</h2>
        <uui-button
          label="New Run"
          look="primary"
          color="positive"
          ?disabled=${this._creating}
          @click=${this._createNewRun}
        >
          New Run
        </uui-button>
      </div>

      ${this._instances.length === 0 ? r`<div class="empty-state">No runs yet. Click 'New Run' to start.</div>` : r`
            <uui-table>
              <uui-table-head>
                <uui-table-head-cell style="width: 80px;">Run</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Status</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Step</uui-table-head-cell>
                <uui-table-head-cell>Started</uui-table-head-cell>
                <uui-table-head-cell class="actions-cell">Actions</uui-table-head-cell>
              </uui-table-head>
              ${this._instances.map(
      (t) => r`
                  <uui-table-row @click=${() => this._navigateToInstance(t.id)}>
                    <uui-table-cell>#${t.runNumber}</uui-table-cell>
                    <uui-table-cell>
                      <uui-tag .color=${T(t.status) ?? p}>
                        ${t.status}
                      </uui-tag>
                    </uui-table-cell>
                    <uui-table-cell>${this._formatStep(t)}</uui-table-cell>
                    <uui-table-cell title=${t.createdAt}>
                      ${N(t.createdAt)}
                    </uui-table-cell>
                    <uui-table-cell class="actions-cell" @click=${this._onActionsClick}>
                      <uui-action-bar>
                        <uui-button
                          label="Actions"
                          id=${`popover-trigger-${t.id}`}
                          look="secondary"
                          compact
                        >
                          <uui-icon name="icon-navigation"></uui-icon>
                        </uui-button>
                        <uui-popover-container
                          margin="6"
                          placement="bottom-end"
                          anchor=${`popover-trigger-${t.id}`}
                        >
                          <umb-popover-layout>
                            <uui-menu-item
                              label="View"
                              @click-label=${() => this._navigateToInstance(t.id)}
                            ></uui-menu-item>
                            ${P(t.status) ? r`
                                  <uui-menu-item
                                    label="Delete"
                                    @click-label=${(e) => this._deleteInstance(t.id, e)}
                                  ></uui-menu-item>
                                ` : p}
                          </umb-popover-layout>
                        </uui-popover-container>
                      </uui-action-bar>
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

    .actions-cell {
      width: 80px;
    }
  `;
s([
  n()
], i.prototype, "_instances", 2);
s([
  n()
], i.prototype, "_workflowName", 2);
s([
  n()
], i.prototype, "_workflowAlias", 2);
s([
  n()
], i.prototype, "_stepCount", 2);
s([
  n()
], i.prototype, "_loading", 2);
s([
  n()
], i.prototype, "_error", 2);
s([
  n()
], i.prototype, "_creating", 2);
s([
  n()
], i.prototype, "_deleting", 2);
i = s([
  b("shallai-instance-list")
], i);
const F = i;
export {
  i as ShallaiInstanceListElement,
  F as default
};
//# sourceMappingURL=shallai-instance-list.element-BQzahQUq.js.map
