import { UmbLitElement as b } from "@umbraco-cms/backoffice/lit-element";
import { css as y, property as m, customElement as S, html as o, nothing as p, state as c } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as z } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as E } from "@umbraco-cms/backoffice/modal";
import { b as N, a as D, e as A, m as O, s as B, f as R } from "./api-client-BCa3QQsh.js";
import { n as U } from "./instance-list-helpers-D6jp37V8.js";
var L = Object.defineProperty, H = Object.getOwnPropertyDescriptor, f = (t, e, s, a) => {
  for (var i = a > 1 ? void 0 : a ? H(e, s) : e, r = t.length - 1, n; r >= 0; r--)
    (n = t[r]) && (i = (a ? n(e, s, i) : n(i)) || i);
  return a && i && L(e, s, i), i;
};
let g = class extends b {
  constructor() {
    super(...arguments), this.role = "agent", this.content = "", this.timestamp = "", this.isStreaming = !1;
  }
  _formatTime(t) {
    if (!t) return "";
    try {
      return new Date(t).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    } catch {
      return "";
    }
  }
  render() {
    return this.role === "system" ? o`
        <div
          class="system-message"
          role="listitem"
          aria-label="System notification"
        >
          ${this.content}
        </div>
      ` : o`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        <span class="agent-content"
          >${this.content}${this.isStreaming ? o`<span class="cursor">▋</span>` : p}</span
        >
        ${this.timestamp ? o`<span class="agent-timestamp"
              >${this._formatTime(this.timestamp)}</span
            >` : p}
      </div>
    `;
  }
};
g.styles = y`
    :host {
      display: block;
    }

    .agent-message {
      background: var(--uui-color-surface);
      padding: var(--uui-size-space-4);
      border-radius: var(--uui-border-radius);
      text-align: left;
    }

    .agent-content {
      white-space: pre-wrap;
      word-break: break-word;
    }

    .agent-timestamp {
      display: block;
      margin-top: var(--uui-size-space-2);
      color: var(--uui-color-text-alt);
      font-size: var(--uui-font-size-s);
    }

    .system-message {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-font-size-s);
      text-align: center;
      padding: var(--uui-size-space-2) 0;
    }

    @keyframes blink {
      0%,
      100% {
        opacity: 1;
      }
      50% {
        opacity: 0;
      }
    }

    .cursor {
      animation: blink 1s step-end infinite;
    }
  `;
f([
  m({ type: String })
], g.prototype, "role", 2);
f([
  m({ type: String })
], g.prototype, "content", 2);
f([
  m({ type: String })
], g.prototype, "timestamp", 2);
f([
  m({ type: Boolean, attribute: "is-streaming" })
], g.prototype, "isStreaming", 2);
g = f([
  S("shallai-chat-message")
], g);
var q = Object.defineProperty, j = Object.getOwnPropertyDescriptor, _ = (t, e, s, a) => {
  for (var i = a > 1 ? void 0 : a ? j(e, s) : e, r = t.length - 1, n; r >= 0; r--)
    (n = t[r]) && (i = (a ? n(e, s, i) : n(i)) || i);
  return a && i && q(e, s, i), i;
};
let h = class extends b {
  constructor() {
    super(...arguments), this.messages = [], this.isStreaming = !1, this._autoScrollPaused = !1, this._hasNewMessages = !1, this._wasStreaming = !1;
  }
  _onScroll(t) {
    const e = t.target;
    if (!e) return;
    const { scrollTop: s, scrollHeight: a, clientHeight: i } = e;
    a - s - i < 50 ? (this._autoScrollPaused = !1, this._hasNewMessages = !1) : this._autoScrollPaused = !0;
  }
  _scrollToBottom() {
    const t = this.renderRoot.querySelector("uui-scroll-container");
    t && (t.scrollTop = t.scrollHeight, this._autoScrollPaused = !1, this._hasNewMessages = !1);
  }
  updated() {
    this._wasStreaming && !this.isStreaming ? this._wasStreaming = !1 : this.isStreaming && (this._wasStreaming = !0);
    const t = this.renderRoot.querySelector("uui-scroll-container");
    if (!t) return;
    const e = t;
    e.scrollHeight - e.scrollTop - e.clientHeight < 50 || !this._autoScrollPaused ? e.scrollTop = e.scrollHeight : this.isStreaming && (this._hasNewMessages = !0);
  }
  render() {
    if (this.messages.length === 0)
      return o`<div class="empty-state">Start the workflow to begin</div>`;
    const t = this.messages.length - 1;
    return o`
      <uui-scroll-container @scroll=${this._onScroll}>
        <div class="message-list" role="log" aria-live="polite">
          ${this.messages.map(
      (e, s) => o`
              <shallai-chat-message
                .role=${e.role}
                .content=${e.content}
                .timestamp=${e.timestamp}
                ?is-streaming=${s === t && e.role === "agent" && this.isStreaming}
              ></shallai-chat-message>
            `
    )}
        </div>
      </uui-scroll-container>

      ${this._autoScrollPaused && this._hasNewMessages ? o`
            <div class="new-messages-indicator">
              <uui-button
                label="New messages"
                look="primary"
                compact
                @click=${this._scrollToBottom}
              >
                New messages ↓
              </uui-button>
            </div>
          ` : p}

      <div class="sr-only" aria-live="assertive">
        ${this.isStreaming ? "Agent is responding..." : ""}
        ${!this.isStreaming && this._wasStreaming ? "Response complete" : ""}
      </div>
    `;
  }
};
h.styles = y`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      position: relative;
    }

    uui-scroll-container {
      flex: 1;
      overflow-y: auto;
    }

    .message-list {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-4);
    }

    .empty-state {
      display: flex;
      align-items: center;
      justify-content: center;
      height: 100%;
      color: var(--uui-color-text-alt);
      text-align: center;
    }

    .new-messages-indicator {
      position: absolute;
      bottom: var(--uui-size-space-4);
      left: 50%;
      transform: translateX(-50%);
      z-index: 10;
    }

    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      padding: 0;
      margin: -1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      white-space: nowrap;
      border-width: 0;
    }
  `;
_([
  m({ attribute: !1 })
], h.prototype, "messages", 2);
_([
  m({ type: Boolean, attribute: "is-streaming" })
], h.prototype, "isStreaming", 2);
_([
  c()
], h.prototype, "_autoScrollPaused", 2);
_([
  c()
], h.prototype, "_hasNewMessages", 2);
_([
  c()
], h.prototype, "_wasStreaming", 2);
h = _([
  S("shallai-chat-panel")
], h);
function W(t) {
  const e = t.split("/");
  return decodeURIComponent(e[e.length - 1]);
}
function F(t, e) {
  const s = t.split("/");
  return s.length >= 2 && s.splice(-2, 2, "workflows", encodeURIComponent(e)), s.join("/");
}
function M(t) {
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
function X(t) {
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
var G = Object.defineProperty, J = Object.getOwnPropertyDescriptor, I = (t) => {
  throw TypeError(t);
}, u = (t, e, s, a) => {
  for (var i = a > 1 ? void 0 : a ? J(e, s) : e, r = t.length - 1, n; r >= 0; r--)
    (n = t[r]) && (i = (a ? n(e, s, i) : n(i)) || i);
  return a && i && G(e, s, i), i;
}, P = (t, e, s) => e.has(t) || I("Cannot " + s), v = (t, e, s) => (P(t, e, "read from private field"), s ? s.call(t) : e.get(t)), V = (t, e, s) => e.has(t) ? I("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, s), K = (t, e, s, a) => (P(t, e, "write to private field"), e.set(t, s), s), d;
let l = class extends b {
  constructor() {
    super(), V(this, d), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this._streaming = !1, this._providerError = !1, this._chatMessages = [], this._streamingText = "", this._viewingStepId = null, this._historyMessages = [], this.consumeContext(z, (t) => {
      K(this, d, t);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const t = W(window.location.pathname), e = await v(this, d)?.getLatestToken(), s = await N(t, e);
      this._instance = s;
      const a = await D(s.workflowAlias, e), r = U(a).find((n) => n.id === s.id);
      this._runNumber = r?.runNumber ?? 0, this._error = !1;
    } catch {
      this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateBack() {
    if (!this._instance) return;
    const t = F(
      window.location.pathname,
      this._instance.workflowAlias
    );
    window.history.pushState({}, "", t);
  }
  async _onStepClick(t) {
    if (t.status !== "Pending") {
      if (this._selectedStepId = t.id, this.dispatchEvent(
        new CustomEvent("step-selected", {
          detail: { stepId: t.id, status: t.status },
          bubbles: !0,
          composed: !0
        })
      ), t.status === "Active") {
        this._viewingStepId = null;
        return;
      }
      this._viewingStepId = t.id;
      try {
        const e = await v(this, d)?.getLatestToken(), s = this._instance.id, a = await A(s, t.id, e);
        this._historyMessages = O(a);
      } catch {
        this._historyMessages = [];
      }
    }
  }
  disconnectedCallback() {
    super.disconnectedCallback(), this._streaming = !1;
  }
  async _onStartClick() {
    if (!(this._streaming || !this._instance)) {
      this._streaming = !0, this._providerError = !1, this._viewingStepId = null, this._streamingText = "";
      try {
        const t = await v(this, d)?.getLatestToken(), e = await B(this._instance.id, t);
        if (!e.ok) {
          if (e.status === 400) {
            this._providerError = !0;
            return;
          }
          await this._loadData();
          return;
        }
        if (!e.body) return;
        const s = e.body.getReader(), a = new TextDecoder();
        let i = "";
        for (; ; ) {
          const { done: r, value: n } = await s.read();
          if (r) break;
          i += a.decode(n, { stream: !0 });
          const x = i.split(`

`);
          i = x.pop();
          for (const T of x) {
            const k = T.split(`
`), $ = k.find((w) => w.startsWith("event:"))?.slice(7), C = k.find((w) => w.startsWith("data:"))?.slice(5);
            $ && C && this._handleSseEvent($, JSON.parse(C));
          }
        }
      } catch {
        this._finaliseStreamingMessage(), this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: "Connection lost.",
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
      } finally {
        this._streaming = !1, await this._loadData();
      }
    }
  }
  _finaliseStreamingMessage() {
    if (!this._streamingText) return;
    const t = this._chatMessages.length - 1, e = this._chatMessages[t];
    e && e.role === "agent" && e.isStreaming && (this._chatMessages = [
      ...this._chatMessages.slice(0, t),
      { ...e, content: this._streamingText, isStreaming: !1 }
    ]), this._streamingText = "";
  }
  _handleSseEvent(t, e) {
    switch (t) {
      case "text.delta": {
        const s = e.content;
        if (!s) break;
        this._streamingText += s;
        const a = this._chatMessages.length - 1, i = this._chatMessages[a];
        i && i.role === "agent" && i.isStreaming ? this._chatMessages = [
          ...this._chatMessages.slice(0, a),
          { ...i, content: this._streamingText }
        ] : this._chatMessages = [
          ...this._chatMessages,
          {
            role: "agent",
            content: this._streamingText,
            timestamp: (/* @__PURE__ */ new Date()).toISOString(),
            isStreaming: !0
          }
        ];
        break;
      }
      case "tool.start":
        this._finaliseStreamingMessage();
        break;
      case "step.started":
        if (this._finaliseStreamingMessage(), this._instance) {
          const s = this._instance.steps.find((a) => a.id === e.stepId);
          s && (s.status = "Active"), this.requestUpdate();
        }
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `Starting ${e.stepName || e.stepId}...`,
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
        break;
      case "step.finished": {
        if (this._finaliseStreamingMessage(), this._instance) {
          const i = this._instance.steps.find((r) => r.id === e.stepId);
          i && (i.status = e.status), this.requestUpdate();
        }
        const s = e.stepName || e.stepId, a = e.status === "Error" ? "failed" : "completed";
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `${s} ${a}`,
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
        break;
      }
      case "run.finished":
        this._finaliseStreamingMessage(), this._instance && (this._instance.status = "Completed", this.requestUpdate());
        break;
      case "run.error":
        this._finaliseStreamingMessage(), this._instance && (this._instance.status = "Failed", this.requestUpdate()), this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `Error: ${e.message || "Workflow failed"}`,
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
        break;
    }
  }
  async _onCancelClick() {
    if (!(this._cancelling || !this._instance)) {
      try {
        await E(this, {
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
        const t = await v(this, d)?.getLatestToken();
        await R(this._instance.id, t), await this._loadData();
      } catch {
        console.warn("Failed to cancel instance");
      } finally {
        this._cancelling = !1;
      }
    }
  }
  _renderStepProgress() {
    return this._instance ? o`
      <div class="step-sidebar">
        <ul role="list">
          ${this._instance.steps.map(
      (t, e) => o`
              <li
                role="listitem"
                class="step-item
                  ${t.status === "Pending" ? "pending" : ""}
                  ${t.status === "Active" ? "active" : ""}
                  ${t.status === "Complete" ? "complete" : ""}
                  ${t.status === "Error" ? "error" : ""}
                  ${t.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === t.id ? "selected" : ""}"
                aria-label="Step ${e + 1}: ${t.name} — ${M(t)}"
                @click=${t.status !== "Pending" ? () => this._onStepClick(t) : p}
                aria-current=${t.status === "Active" ? "step" : p}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${t.status === "Active" ? "step-icon-spin" : ""}"
                    name=${X(t.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${t.name}</span>
                  <span class="step-subtitle">${M(t)}</span>
                </div>
              </li>
            `
    )}
        </ul>
      </div>
    ` : p;
  }
  render() {
    if (this._loading)
      return o`<uui-loader></uui-loader>`;
    if (this._error || !this._instance)
      return o`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    const t = this._instance, e = t.status === "Pending" && !this._streaming, s = t.steps.some((n) => n.status === "Active"), a = t.status === "Running" && !s && !this._streaming && t.steps.some((n) => n.status === "Pending"), i = (t.status === "Running" || t.status === "Pending") && !this._streaming, r = this._providerError ? o`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : o`<shallai-chat-panel
          .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
          ?is-streaming=${this._streaming && !this._viewingStepId}
        ></shallai-chat-panel>`;
    return o`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${t.workflowName || t.workflowAlias} — Run #${this._runNumber}</h2>
        ${e ? o`
              <uui-button label="Start" look="primary" @click=${this._onStartClick}>
                Start
              </uui-button>
            ` : p}
        ${a ? o`
              <uui-button label="Continue" look="primary" @click=${this._onStartClick}>
                Continue
              </uui-button>
            ` : p}
        ${this._streaming ? o`<uui-loader-bar></uui-loader-bar>` : p}
        ${i ? o`
              <uui-button
                label="Cancel"
                look="secondary"
                color="danger"
                ?disabled=${this._cancelling}
                @click=${this._onCancelClick}
              >
                Cancel
              </uui-button>
            ` : p}
      </div>

      <div class="detail-grid">
        ${this._renderStepProgress()}
        ${r}
      </div>
    `;
  }
};
d = /* @__PURE__ */ new WeakMap();
l.styles = y`
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
u([
  c()
], l.prototype, "_instance", 2);
u([
  c()
], l.prototype, "_loading", 2);
u([
  c()
], l.prototype, "_error", 2);
u([
  c()
], l.prototype, "_selectedStepId", 2);
u([
  c()
], l.prototype, "_cancelling", 2);
u([
  c()
], l.prototype, "_runNumber", 2);
u([
  c()
], l.prototype, "_streaming", 2);
u([
  c()
], l.prototype, "_providerError", 2);
u([
  c()
], l.prototype, "_chatMessages", 2);
u([
  c()
], l.prototype, "_streamingText", 2);
u([
  c()
], l.prototype, "_viewingStepId", 2);
u([
  c()
], l.prototype, "_historyMessages", 2);
l = u([
  S("shallai-instance-detail")
], l);
const it = l;
export {
  l as ShallaiInstanceDetailElement,
  it as default
};
//# sourceMappingURL=shallai-instance-detail.element-B5EBZCJH.js.map
