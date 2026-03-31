import { UmbLitElement as x } from "@umbraco-cms/backoffice/lit-element";
import { nothing as d, html as a, css as C, state as u, customElement as S } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as $ } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as I } from "@umbraco-cms/backoffice/modal";
import { b as E, a as P, s as A, e as z } from "./api-client-CgFnA5nu.js";
import { n as D } from "./instance-list-helpers-D6jp37V8.js";
function T(t) {
  const e = t.split("/");
  return decodeURIComponent(e[e.length - 1]);
}
function U(t, e) {
  const i = t.split("/");
  return i.length >= 2 && i.splice(-2, 2, "workflows", encodeURIComponent(e)), i.join("/");
}
function b(t) {
  switch (t.status) {
    case "Pending":
      return "Pending";
    case "Active":
      return "Running...";
    case "Complete":
      return t.writesTo?.[0] ?? "Complete";
    case "Error":
      return "Failed";
    default:
      return t.status;
  }
}
function N(t) {
  switch (t) {
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
var R = Object.defineProperty, L = Object.getOwnPropertyDescriptor, w = (t) => {
  throw TypeError(t);
}, l = (t, e, i, s) => {
  for (var r = s > 1 ? void 0 : s ? L(e, i) : e, c = t.length - 1, o; c >= 0; c--)
    (o = t[c]) && (r = (s ? o(e, i, r) : o(r)) || r);
  return s && r && R(e, i, r), r;
}, k = (t, e, i) => e.has(t) || w("Cannot " + i), m = (t, e, i) => (k(t, e, "read from private field"), i ? i.call(t) : e.get(t)), O = (t, e, i) => e.has(t) ? w("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, i), W = (t, e, i, s) => (k(t, e, "write to private field"), e.set(t, i), i), p;
let n = class extends x {
  constructor() {
    super(), O(this, p), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this._streaming = !1, this._providerError = !1, this.consumeContext($, (t) => {
      W(this, p, t);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const t = T(window.location.pathname), e = await m(this, p)?.getLatestToken(), i = await E(t, e);
      this._instance = i;
      const s = await P(i.workflowAlias, e), c = D(s).find((o) => o.id === i.id);
      this._runNumber = c?.runNumber ?? 0, this._error = !1;
    } catch {
      this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateBack() {
    if (!this._instance) return;
    const t = U(
      window.location.pathname,
      this._instance.workflowAlias
    );
    window.history.pushState({}, "", t);
  }
  _onStepClick(t) {
    t.status !== "Pending" && (this._selectedStepId = t.id, this.dispatchEvent(
      new CustomEvent("step-selected", {
        detail: { stepId: t.id, status: t.status },
        bubbles: !0,
        composed: !0
      })
    ));
  }
  async _onStartClick() {
    if (!(this._streaming || !this._instance)) {
      this._streaming = !0, this._providerError = !1;
      try {
        const t = await m(this, p)?.getLatestToken(), e = await A(this._instance.id, t);
        if (!e.ok) {
          if (e.status === 400) {
            this._providerError = !0;
            return;
          }
          await this._loadData();
          return;
        }
        if (!e.body) return;
        const i = e.body.getReader(), s = new TextDecoder();
        let r = "";
        for (; ; ) {
          const { done: c, value: o } = await i.read();
          if (c) break;
          r += s.decode(o, { stream: !0 });
          const f = r.split(`

`);
          r = f.pop();
          for (const y of f) {
            const v = y.split(`
`), g = v.find((h) => h.startsWith("event:"))?.slice(7), _ = v.find((h) => h.startsWith("data:"))?.slice(5);
            g && _ && this._handleSseEvent(g, JSON.parse(_));
          }
        }
      } catch {
        console.warn("Streaming failed");
      } finally {
        this._streaming = !1, await this._loadData();
      }
    }
  }
  _handleSseEvent(t, e) {
    switch (t) {
      case "step.started":
        if (this._instance) {
          const i = this._instance.steps.find((s) => s.id === e.stepId);
          i && (i.status = "Active"), this.requestUpdate();
        }
        break;
      case "step.finished":
        if (this._instance) {
          const i = this._instance.steps.find((s) => s.id === e.stepId);
          i && (i.status = e.status), this.requestUpdate();
        }
        break;
      case "run.finished":
        this._instance && (this._instance.status = "Completed", this.requestUpdate());
        break;
      case "run.error":
        this._instance && (this._instance.status = "Failed", this.requestUpdate());
        break;
    }
  }
  async _onCancelClick() {
    if (!(this._cancelling || !this._instance)) {
      try {
        await I(this, {
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
        const t = await m(this, p)?.getLatestToken();
        await z(this._instance.id, t), await this._loadData();
      } catch {
        console.warn("Failed to cancel instance");
      } finally {
        this._cancelling = !1;
      }
    }
  }
  _renderStepProgress() {
    return this._instance ? a`
      <div class="step-sidebar">
        <ul role="list">
          ${this._instance.steps.map(
      (t, e) => a`
              <li
                role="listitem"
                class="step-item
                  ${t.status === "Pending" ? "pending" : ""}
                  ${t.status === "Active" ? "active" : ""}
                  ${t.status === "Complete" ? "complete" : ""}
                  ${t.status === "Error" ? "error" : ""}
                  ${t.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === t.id ? "selected" : ""}"
                aria-label="Step ${e + 1}: ${t.name} — ${b(t)}"
                @click=${t.status !== "Pending" ? () => this._onStepClick(t) : d}
                aria-current=${t.status === "Active" ? "step" : d}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${t.status === "Active" ? "step-icon-spin" : ""}"
                    name=${N(t.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${t.name}</span>
                  <span class="step-subtitle">${b(t)}</span>
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
      return a`<uui-loader></uui-loader>`;
    if (this._error || !this._instance)
      return a`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    const t = this._instance, e = t.status === "Pending" && !this._streaming, i = t.steps.some((o) => o.status === "Active"), s = t.status === "Running" && !i && !this._streaming && t.steps.some((o) => o.status === "Pending"), r = (t.status === "Running" || t.status === "Pending") && !this._streaming, c = this._providerError ? a`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : this._streaming || t.status === "Running" && i ? a`<div class="main-placeholder">Step in progress...</div>` : t.status === "Completed" ? a`<div class="main-placeholder">Workflow completed.</div>` : t.status === "Failed" ? a`<div class="main-placeholder">Workflow failed.</div>` : s ? a`<div class="main-placeholder">Step complete. Click 'Continue' to proceed.</div>` : a`<div class="main-placeholder">Click 'Start' to begin the workflow.</div>`;
    return a`
      <div class="header">
        <uui-button look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${t.workflowName || t.workflowAlias} — Run #${this._runNumber}</h2>
        ${e ? a`
              <uui-button look="primary" @click=${this._onStartClick}>
                Start
              </uui-button>
            ` : d}
        ${s ? a`
              <uui-button look="primary" @click=${this._onStartClick}>
                Continue
              </uui-button>
            ` : d}
        ${this._streaming ? a`<uui-loader-bar></uui-loader-bar>` : d}
        ${r ? a`
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
        ${c}
      </div>
    `;
  }
};
p = /* @__PURE__ */ new WeakMap();
n.styles = C`
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
l([
  u()
], n.prototype, "_instance", 2);
l([
  u()
], n.prototype, "_loading", 2);
l([
  u()
], n.prototype, "_error", 2);
l([
  u()
], n.prototype, "_selectedStepId", 2);
l([
  u()
], n.prototype, "_cancelling", 2);
l([
  u()
], n.prototype, "_runNumber", 2);
l([
  u()
], n.prototype, "_streaming", 2);
l([
  u()
], n.prototype, "_providerError", 2);
n = l([
  S("shallai-instance-detail")
], n);
const J = n;
export {
  n as ShallaiInstanceDetailElement,
  J as default
};
//# sourceMappingURL=shallai-instance-detail.element-AwanNTgF.js.map
