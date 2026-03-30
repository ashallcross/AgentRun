import { UmbLitElement as m } from "@umbraco-cms/backoffice/lit-element";
import { html as l, nothing as p, css as w, state as s, customElement as g } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as b } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as v } from "@umbraco-cms/backoffice/modal";
import { g as y, a as k, c as $, d as C } from "./api-client-DNQcFMx0.js";
function A(t) {
  const e = Math.floor((Date.now() - new Date(t).getTime()) / 1e3);
  if (e < 60) return "just now";
  const a = Math.floor(e / 60);
  if (a < 60) return `${a} minute${a === 1 ? "" : "s"} ago`;
  const i = Math.floor(a / 60);
  if (i < 24) return `${i} hour${i === 1 ? "" : "s"} ago`;
  const o = Math.floor(i / 24);
  if (o < 30) return `${o} day${o === 1 ? "" : "s"} ago`;
  const u = Math.floor(o / 30);
  return `${u} month${u === 1 ? "" : "s"} ago`;
}
function S(t) {
  const e = t.split("/"), a = e[e.length - 1];
  return decodeURIComponent(a);
}
function I(t, e) {
  const a = e.split("/");
  return a.length >= 2 && a.splice(-2, 2, "instances", t), a.join("/");
}
function T(t) {
  const e = t.lastIndexOf("/");
  return e > 0 ? t.substring(0, e) : t;
}
function x(t) {
  switch (t) {
    case "Completed":
      return "positive";
    case "Failed":
      return "danger";
    case "Running":
      return "warning";
    default:
      return;
  }
}
function D(t) {
  return t === "Completed" || t === "Failed" || t === "Cancelled";
}
function N(t) {
  const a = [...t].sort(
    (i, o) => new Date(i.createdAt).getTime() - new Date(o.createdAt).getTime()
  ).map((i, o) => ({ ...i, runNumber: o + 1 }));
  return a.reverse(), a;
}
var P = Object.defineProperty, L = Object.getOwnPropertyDescriptor, _ = (t) => {
  throw TypeError(t);
}, r = (t, e, a, i) => {
  for (var o = i > 1 ? void 0 : i ? L(e, a) : e, u = t.length - 1, h; u >= 0; u--)
    (h = t[u]) && (o = (i ? h(e, a, o) : h(o)) || o);
  return i && o && P(e, a, o), o;
}, f = (t, e, a) => e.has(t) || _("Cannot " + a), d = (t, e, a) => (f(t, e, "read from private field"), a ? a.call(t) : e.get(t)), M = (t, e, a) => e.has(t) ? _("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, a), R = (t, e, a, i) => (f(t, e, "write to private field"), e.set(t, a), a), c;
let n = class extends m {
  constructor() {
    super(), M(this, c), this._instances = [], this._workflowName = "", this._workflowAlias = "", this._stepCount = 0, this._loading = !0, this._error = !1, this._creating = !1, this._deleting = null, this.consumeContext(b, (t) => {
      R(this, c, t);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._workflowAlias = S(window.location.pathname), this._loadData();
  }
  async _loadData() {
    try {
      const t = await d(this, c)?.getLatestToken(), [e, a] = await Promise.all([
        y(t),
        k(this._workflowAlias, t)
      ]), i = e.find((o) => o.alias === this._workflowAlias);
      this._workflowName = i?.name ?? this._workflowAlias, this._stepCount = i?.stepCount ?? 0, this._instances = N(a), this._error = !1;
    } catch {
      this._instances = [], this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateToInstance(t) {
    window.history.pushState({}, "", I(t, window.location.pathname));
  }
  _navigateBack() {
    window.history.pushState({}, "", T(window.location.pathname));
  }
  async _createNewRun() {
    if (!this._creating) {
      this._creating = !0;
      try {
        const t = await d(this, c)?.getLatestToken(), e = await $(this._workflowAlias, t);
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
        const a = await d(this, c)?.getLatestToken();
        await C(t, a), this._instances = this._instances.filter((i) => i.id !== t);
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
    return this._loading ? l`<uui-loader></uui-loader>` : this._error ? l`
        <div class="error-state">
          Failed to load instances. Check that you have backoffice access and
          try refreshing the page.
        </div>
      ` : l`
      <div class="header">
        <uui-button look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${this._workflowName}</h2>
        <uui-button
          look="primary"
          color="positive"
          ?disabled=${this._creating}
          @click=${this._createNewRun}
        >
          New Run
        </uui-button>
      </div>

      ${this._instances.length === 0 ? l`<div class="empty-state">No runs yet. Click 'New Run' to start.</div>` : l`
            <uui-table>
              <uui-table-head>
                <uui-table-head-cell style="width: 80px;">Run</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Status</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Step</uui-table-head-cell>
                <uui-table-head-cell>Started</uui-table-head-cell>
                <uui-table-head-cell class="actions-cell">Actions</uui-table-head-cell>
              </uui-table-head>
              ${this._instances.map(
      (t) => l`
                  <uui-table-row @click=${() => this._navigateToInstance(t.id)}>
                    <uui-table-cell>#${t.runNumber}</uui-table-cell>
                    <uui-table-cell>
                      <uui-tag .color=${x(t.status) ?? p}>
                        ${t.status}
                      </uui-tag>
                    </uui-table-cell>
                    <uui-table-cell>${this._formatStep(t)}</uui-table-cell>
                    <uui-table-cell title=${t.createdAt}>
                      ${A(t.createdAt)}
                    </uui-table-cell>
                    <uui-table-cell class="actions-cell" @click=${this._onActionsClick}>
                      <uui-action-bar>
                        <uui-button
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
                            ${D(t.status) ? l`
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
c = /* @__PURE__ */ new WeakMap();
n.styles = w`
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
r([
  s()
], n.prototype, "_instances", 2);
r([
  s()
], n.prototype, "_workflowName", 2);
r([
  s()
], n.prototype, "_workflowAlias", 2);
r([
  s()
], n.prototype, "_stepCount", 2);
r([
  s()
], n.prototype, "_loading", 2);
r([
  s()
], n.prototype, "_error", 2);
r([
  s()
], n.prototype, "_creating", 2);
r([
  s()
], n.prototype, "_deleting", 2);
n = r([
  g("shallai-instance-list")
], n);
const B = n;
export {
  n as ShallaiInstanceListElement,
  B as default
};
//# sourceMappingURL=shallai-instance-list.element-CieA6PCC.js.map
