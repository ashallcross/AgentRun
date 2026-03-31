import { UmbLitElement as f } from "@umbraco-cms/backoffice/lit-element";
import { nothing as d, html as s, css as _, state as c, customElement as b } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as w } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as k } from "@umbraco-cms/backoffice/modal";
import { b as x, a as y, e as C } from "./api-client-pcmz0vNh.js";
import { n as $ } from "./instance-list-helpers-D6jp37V8.js";
function S(e) {
  const t = e.split("/");
  return decodeURIComponent(t[t.length - 1]);
}
function I(e, t) {
  const i = e.split("/");
  return i.length >= 2 && i.splice(-2, 2, "workflows", encodeURIComponent(t)), i.join("/");
}
function h(e) {
  switch (e.status) {
    case "Pending":
      return "Pending";
    case "Active":
      return "Running...";
    case "Complete":
      return e.writesTo?.[0] ?? "Complete";
    case "Error":
      return "Failed";
    default:
      return e.status;
  }
}
function P(e) {
  switch (e) {
    case "Pending":
      return "icon-circle-dotted";
    case "Active":
      return "icon-sync";
    case "Complete":
      return "icon-check";
    case "Error":
      return "icon-wrong";
    default:
      return "icon-circle-dotted";
  }
}
var z = Object.defineProperty, E = Object.getOwnPropertyDescriptor, g = (e) => {
  throw TypeError(e);
}, o = (e, t, i, n) => {
  for (var a = n > 1 ? void 0 : n ? E(t, i) : t, l = e.length - 1, u; l >= 0; l--)
    (u = e[l]) && (a = (n ? u(t, i, a) : u(a)) || a);
  return n && a && z(t, i, a), a;
}, v = (e, t, i) => t.has(e) || g("Cannot " + i), m = (e, t, i) => (v(e, t, "read from private field"), i ? i.call(e) : t.get(e)), A = (e, t, i) => t.has(e) ? g("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, i), N = (e, t, i, n) => (v(e, t, "write to private field"), t.set(e, i), i), p;
let r = class extends f {
  constructor() {
    super(), A(this, p), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this.consumeContext(w, (e) => {
      N(this, p, e);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const e = S(window.location.pathname), t = await m(this, p)?.getLatestToken(), i = await x(e, t);
      this._instance = i;
      const n = await y(i.workflowAlias, t), l = $(n).find((u) => u.id === i.id);
      this._runNumber = l?.runNumber ?? 0, this._error = !1;
    } catch {
      this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateBack() {
    if (!this._instance) return;
    const e = I(
      window.location.pathname,
      this._instance.workflowAlias
    );
    window.history.pushState({}, "", e);
  }
  _onStepClick(e) {
    e.status !== "Pending" && (this._selectedStepId = e.id, this.dispatchEvent(
      new CustomEvent("step-selected", {
        detail: { stepId: e.id, status: e.status },
        bubbles: !0,
        composed: !0
      })
    ));
  }
  _onStartClick() {
    console.log("Start not yet implemented");
  }
  async _onCancelClick() {
    if (!(this._cancelling || !this._instance)) {
      try {
        await k(this, {
          headline: "Cancel workflow run",
          content: "Cancel this workflow run?",
          color: "danger",
          confirmLabel: "Cancel Run"
        });
      } catch {
        return;
      }
      this._cancelling = !0;
      try {
        const e = await m(this, p)?.getLatestToken();
        await C(this._instance.id, e), await this._loadData();
      } catch {
        console.warn("Failed to cancel instance");
      } finally {
        this._cancelling = !1;
      }
    }
  }
  _renderStepProgress() {
    return this._instance ? s`
      <div class="step-sidebar">
        <ul role="list">
          ${this._instance.steps.map(
      (e, t) => s`
              <li
                role="listitem"
                class="step-item
                  ${e.status === "Pending" ? "pending" : ""}
                  ${e.status === "Active" ? "active" : ""}
                  ${e.status === "Complete" ? "complete" : ""}
                  ${e.status === "Error" ? "error" : ""}
                  ${e.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === e.id ? "selected" : ""}"
                aria-label="Step ${t + 1}: ${e.name} — ${h(e)}"
                @click=${e.status !== "Pending" ? () => this._onStepClick(e) : d}
                aria-current=${e.status === "Active" ? "step" : d}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${e.status === "Active" ? "step-icon-spin" : ""}"
                    name=${P(e.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${e.name}</span>
                  <span class="step-subtitle">${h(e)}</span>
                </div>
              </li>
            `
    )}
        </ul>
      </div>
    ` : d;
  }
  render() {
    if (this._loading)
      return s`<uui-loader></uui-loader>`;
    if (this._error || !this._instance)
      return s`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    const e = this._instance, t = e.status === "Pending", i = e.status === "Running" || e.status === "Pending";
    return s`
      <div class="header">
        <uui-button look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${e.workflowName || e.workflowAlias} — Run #${this._runNumber}</h2>
        ${t ? s`
              <uui-button look="primary" @click=${this._onStartClick}>
                Start
              </uui-button>
            ` : d}
        ${i ? s`
              <uui-button
                look="secondary"
                color="danger"
                ?disabled=${this._cancelling}
                @click=${this._onCancelClick}
              >
                Cancel
              </uui-button>
            ` : d}
      </div>

      <div class="detail-grid">
        ${this._renderStepProgress()}
        <div class="main-placeholder">
          Click 'Start' to begin the workflow.
        </div>
      </div>
    `;
  }
};
p = /* @__PURE__ */ new WeakMap();
r.styles = _`
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

    .detail-grid {
      display: grid;
      grid-template-columns: 240px 1fr;
      gap: var(--uui-size-layout-1);
      height: 100%;
    }

    .step-sidebar {
      border-right: 1px solid var(--uui-color-border);
      padding-right: var(--uui-size-layout-1);
    }

    .step-sidebar ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0;
      position: relative;
    }

    .step-item {
      display: flex;
      flex-direction: row;
      align-items: flex-start;
      gap: var(--uui-size-space-4);
      padding: var(--uui-size-space-4) var(--uui-size-space-4);
      border-radius: var(--uui-border-radius);
      position: relative;
      transition: background-color 150ms ease;
    }

    /* Vertical connecting line between steps */
    .step-item:not(:last-child)::after {
      content: "";
      position: absolute;
      left: 23px;
      top: 40px;
      bottom: -1px;
      width: 2px;
      background: var(--uui-color-border);
    }

    .step-item.clickable {
      cursor: pointer;
    }

    .step-item.clickable:hover {
      background: var(--uui-color-surface-emphasis);
    }

    .step-item.selected {
      background: var(--uui-color-surface-emphasis);
      border-left: 3px solid var(--uui-color-current);
      padding-left: calc(var(--uui-size-space-4) - 3px);
    }

    .step-icon-wrapper {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 28px;
      height: 28px;
      border-radius: 50%;
      flex-shrink: 0;
      background: var(--uui-color-surface);
      border: 2px solid var(--uui-color-border);
      position: relative;
      z-index: 1;
    }

    .step-item.pending .step-icon-wrapper {
      border-color: var(--uui-color-border);
      background: var(--uui-color-surface);
    }

    .step-item.active .step-icon-wrapper {
      border-color: var(--uui-color-current);
      background: var(--uui-color-current);
    }

    .step-item.complete .step-icon-wrapper {
      border-color: var(--uui-color-positive);
      background: var(--uui-color-positive);
    }

    .step-item.error .step-icon-wrapper {
      border-color: var(--uui-color-danger);
      background: var(--uui-color-danger);
    }

    .step-icon {
      font-size: 14px;
    }

    .step-item.pending .step-icon {
      color: var(--uui-color-text-alt);
    }

    .step-item.active .step-icon,
    .step-item.complete .step-icon,
    .step-item.error .step-icon {
      color: var(--uui-color-surface);
    }

    .step-text {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding-top: 3px;
    }

    .step-name {
      color: var(--uui-color-text);
      font-weight: 500;
    }

    .step-item.pending .step-name {
      color: var(--uui-color-text-alt);
    }

    .step-subtitle {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }

    .step-icon-spin {
      animation: spin 1.5s linear infinite;
    }

    .main-placeholder {
      display: flex;
      align-items: center;
      justify-content: center;
      height: 100%;
      color: var(--uui-color-text-alt);
      text-align: center;
    }

    .error-state {
      padding: var(--uui-size-layout-1);
      color: var(--uui-color-text);
      text-align: center;
    }
  `;
o([
  c()
], r.prototype, "_instance", 2);
o([
  c()
], r.prototype, "_loading", 2);
o([
  c()
], r.prototype, "_error", 2);
o([
  c()
], r.prototype, "_selectedStepId", 2);
o([
  c()
], r.prototype, "_cancelling", 2);
o([
  c()
], r.prototype, "_runNumber", 2);
r = o([
  b("shallai-instance-detail")
], r);
const B = r;
export {
  r as ShallaiInstanceDetailElement,
  B as default
};
//# sourceMappingURL=shallai-instance-detail.element-BvVHc1Xr.js.map
