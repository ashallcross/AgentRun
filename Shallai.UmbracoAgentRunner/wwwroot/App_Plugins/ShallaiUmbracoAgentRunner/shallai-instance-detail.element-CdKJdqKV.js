import { UmbLitElement as $ } from "@umbraco-cms/backoffice/lit-element";
import { css as k, property as p, state as u, customElement as M, html as n, nothing as l } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as O } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as E } from "@umbraco-cms/backoffice/modal";
import { b as D, a as A, e as B, m as R, s as L, f as U, h as j } from "./api-client-C7EyKSQQ.js";
import { n as H } from "./instance-list-helpers-D6jp37V8.js";
var q = Object.defineProperty, W = Object.getOwnPropertyDescriptor, v = (t, e, a, i) => {
  for (var s = i > 1 ? void 0 : i ? W(e, a) : e, r = t.length - 1, o; r >= 0; r--)
    (o = t[r]) && (s = (i ? o(e, a, s) : o(s)) || s);
  return i && s && q(e, a, s), s;
};
let g = class extends $ {
  constructor() {
    super(...arguments), this.toolName = "", this.toolCallId = "", this.summary = "", this.arguments = null, this.result = null, this.status = "running", this._expanded = !1;
  }
  _toggle() {
    this.status !== "running" && (this._expanded = !this._expanded);
  }
  _onKeydown(t) {
    (t.key === "Enter" || t.key === " ") && (t.preventDefault(), this._toggle());
  }
  _renderStatusIcon() {
    return this.status === "running" ? n`<uui-loader></uui-loader>` : this.status === "error" ? n`<uui-icon class="status-icon error" name="icon-alert"></uui-icon>` : n`<uui-icon class="status-icon complete" name="icon-check"></uui-icon>`;
  }
  _renderBody() {
    return this._expanded ? n`
      <div class="tool-call-body">
        ${this.arguments ? n`
              <div class="section-label">Arguments</div>
              <div class="mono-block">${JSON.stringify(this.arguments, null, 2)}</div>
            ` : l}
        ${this.result != null ? n`
              <div class="section-label">Result</div>
              <div class="mono-block">${this.result}</div>
            ` : l}
      </div>
    ` : l;
  }
  render() {
    const t = this.status !== "running";
    return n`
      <div
        class="tool-call ${this.status}"
        role="group"
        aria-label="Tool call: ${this.toolName}"
        aria-expanded=${t ? String(this._expanded) : l}
        tabindex=${t ? "0" : l}
        @click=${this._toggle}
        @keydown=${this._onKeydown}
      >
        <div class="tool-call-header">
          ${this._renderStatusIcon()}
          <span class="tool-name">${this.toolName}</span>
          <span class="tool-summary">${this.summary ? `— ${this.summary}` : ""}</span>
          ${t ? n`<uui-icon class="chevron ${this._expanded ? "open" : ""}" name="icon-navigation-down"></uui-icon>` : l}
        </div>
        ${this._renderBody()}
      </div>
    `;
  }
};
g.styles = k`
    :host {
      display: block;
      margin-top: var(--uui-size-space-2);
    }

    .tool-call {
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      padding: var(--uui-size-space-3);
      transition: border-color 150ms ease;
    }

    .tool-call.error {
      border-color: var(--uui-color-danger);
    }

    .tool-call-header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-2);
      cursor: pointer;
      user-select: none;
    }

    .tool-call.running .tool-call-header {
      cursor: default;
    }

    .tool-name {
      font-weight: 500;
      font-size: var(--uui-font-size-s);
    }

    .tool-summary {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-font-size-s);
      flex: 1;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .chevron {
      font-size: 12px;
      color: var(--uui-color-text-alt);
      transition: transform 150ms ease;
    }

    .chevron.open {
      transform: rotate(180deg);
    }

    .status-icon {
      font-size: 14px;
      flex-shrink: 0;
    }

    .status-icon.complete {
      color: var(--uui-color-positive);
    }

    .status-icon.error {
      color: var(--uui-color-danger);
    }

    .tool-call-body {
      margin-top: var(--uui-size-space-3);
      background: var(--uui-color-surface);
      border-radius: var(--uui-border-radius);
      padding: var(--uui-size-space-3);
    }

    .section-label {
      font-size: var(--uui-font-size-s);
      font-weight: 500;
      color: var(--uui-color-text-alt);
      margin-bottom: var(--uui-size-space-1);
    }

    .section-label:not(:first-child) {
      margin-top: var(--uui-size-space-3);
    }

    .mono-block {
      font-family: monospace;
      font-size: var(--uui-font-size-s);
      white-space: pre-wrap;
      word-break: break-word;
      overflow-y: auto;
      max-height: 300px;
    }
  `;
v([
  p({ type: String })
], g.prototype, "toolName", 2);
v([
  p({ type: String })
], g.prototype, "toolCallId", 2);
v([
  p({ type: String })
], g.prototype, "summary", 2);
v([
  p({ attribute: !1 })
], g.prototype, "arguments", 2);
v([
  p({ type: String })
], g.prototype, "result", 2);
v([
  p({ type: String })
], g.prototype, "status", 2);
v([
  u()
], g.prototype, "_expanded", 2);
g = v([
  M("shallai-tool-call")
], g);
var F = Object.defineProperty, J = Object.getOwnPropertyDescriptor, b = (t, e, a, i) => {
  for (var s = i > 1 ? void 0 : i ? J(e, a) : e, r = t.length - 1, o; r >= 0; r--)
    (o = t[r]) && (s = (i ? o(e, a, s) : o(s)) || s);
  return i && s && F(e, a, s), s;
};
let _ = class extends $ {
  constructor() {
    super(...arguments), this.role = "agent", this.content = "", this.timestamp = "", this.isStreaming = !1, this.toolCalls = [];
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
    return this.role === "system" ? n`
        <div
          class="system-message"
          role="listitem"
          aria-label="System notification"
        >
          ${this.content}
        </div>
      ` : n`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        <span class="agent-content"
          >${this.content}${this.isStreaming ? n`<span class="cursor">▋</span>` : l}</span
        >
        ${this.toolCalls.map((t) => n`
          <shallai-tool-call
            .toolName=${t.toolName}
            .toolCallId=${t.toolCallId}
            .summary=${t.summary}
            .arguments=${t.arguments}
            .result=${t.result}
            .status=${t.status}
          ></shallai-tool-call>
        `)}
        ${this.timestamp ? n`<span class="agent-timestamp"
              >${this._formatTime(this.timestamp)}</span
            >` : l}
      </div>
    `;
  }
};
_.styles = k`
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
b([
  p({ type: String })
], _.prototype, "role", 2);
b([
  p({ type: String })
], _.prototype, "content", 2);
b([
  p({ type: String })
], _.prototype, "timestamp", 2);
b([
  p({ type: Boolean, attribute: "is-streaming" })
], _.prototype, "isStreaming", 2);
b([
  p({ attribute: !1 })
], _.prototype, "toolCalls", 2);
_ = b([
  M("shallai-chat-message")
], _);
var K = Object.defineProperty, X = Object.getOwnPropertyDescriptor, w = (t, e, a, i) => {
  for (var s = i > 1 ? void 0 : i ? X(e, a) : e, r = t.length - 1, o; r >= 0; r--)
    (o = t[r]) && (s = (i ? o(e, a, s) : o(s)) || s);
  return i && s && K(e, a, s), s;
};
let f = class extends $ {
  constructor() {
    super(...arguments), this.messages = [], this.isStreaming = !1, this._autoScrollPaused = !1, this._hasNewMessages = !1, this._wasStreaming = !1;
  }
  _onScroll(t) {
    const e = t.target;
    if (!e) return;
    const { scrollTop: a, scrollHeight: i, clientHeight: s } = e;
    i - a - s < 50 ? (this._autoScrollPaused = !1, this._hasNewMessages = !1) : this._autoScrollPaused = !0;
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
      return n`<div class="empty-state">Start the workflow to begin</div>`;
    const t = this.messages.length - 1;
    return n`
      <uui-scroll-container @scroll=${this._onScroll}>
        <div class="message-list" role="log" aria-live="polite">
          ${this.messages.map(
      (e, a) => n`
              <shallai-chat-message
                .role=${e.role}
                .content=${e.content}
                .timestamp=${e.timestamp}
                .toolCalls=${e.toolCalls ?? []}
                ?is-streaming=${a === t && e.role === "agent" && this.isStreaming}
              ></shallai-chat-message>
            `
    )}
        </div>
      </uui-scroll-container>

      ${this._autoScrollPaused && this._hasNewMessages ? n`
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
          ` : l}

      <div class="sr-only" aria-live="assertive">
        ${this.isStreaming ? "Agent is responding..." : ""}
        ${!this.isStreaming && this._wasStreaming ? "Response complete" : ""}
      </div>
    `;
  }
};
f.styles = k`
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
w([
  p({ attribute: !1 })
], f.prototype, "messages", 2);
w([
  p({ type: Boolean, attribute: "is-streaming" })
], f.prototype, "isStreaming", 2);
w([
  u()
], f.prototype, "_autoScrollPaused", 2);
w([
  u()
], f.prototype, "_hasNewMessages", 2);
w([
  u()
], f.prototype, "_wasStreaming", 2);
f = w([
  M("shallai-chat-panel")
], f);
function G(t) {
  const e = t.split("/");
  return decodeURIComponent(e[e.length - 1]);
}
function V(t, e) {
  const a = t.split("/");
  return a.length >= 2 && a.splice(-2, 2, "workflows", encodeURIComponent(e)), a.join("/");
}
function T(t) {
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
function Q(t) {
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
var Y = Object.defineProperty, Z = Object.getOwnPropertyDescriptor, P = (t) => {
  throw TypeError(t);
}, d = (t, e, a, i) => {
  for (var s = i > 1 ? void 0 : i ? Z(e, a) : e, r = t.length - 1, o; r >= 0; r--)
    (o = t[r]) && (s = (i ? o(e, a, s) : o(s)) || s);
  return i && s && Y(e, a, s), s;
}, N = (t, e, a) => e.has(t) || P("Cannot " + a), C = (t, e, a) => (N(t, e, "read from private field"), a ? a.call(t) : e.get(t)), tt = (t, e, a) => e.has(t) ? P("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, a), et = (t, e, a, i) => (N(t, e, "write to private field"), e.set(t, a), a), y;
let c = class extends $ {
  constructor() {
    super(), tt(this, y), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this._streaming = !1, this._providerError = !1, this._chatMessages = [], this._streamingText = "", this._viewingStepId = null, this._historyMessages = [], this._toolBatchOpen = !1, this.consumeContext(O, (t) => {
      et(this, y, t);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const t = G(window.location.pathname), e = await C(this, y)?.getLatestToken(), a = await D(t, e);
      this._instance = a;
      const i = await A(a.workflowAlias, e), r = H(i).find((o) => o.id === a.id);
      this._runNumber = r?.runNumber ?? 0, this._error = !1;
    } catch {
      this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateBack() {
    if (!this._instance) return;
    const t = V(
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
        const e = await C(this, y)?.getLatestToken(), a = this._instance.id, i = await B(a, t.id, e);
        this._historyMessages = R(i);
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
        const t = await C(this, y)?.getLatestToken(), e = await L(this._instance.id, t);
        if (!e.ok) {
          if (e.status === 400) {
            this._providerError = !0;
            return;
          }
          await this._loadData();
          return;
        }
        if (!e.body) return;
        const a = e.body.getReader(), i = new TextDecoder();
        let s = "";
        for (; ; ) {
          const { done: r, value: o } = await a.read();
          if (r) break;
          s += i.decode(o, { stream: !0 });
          const h = s.split(`

`);
          s = h.pop();
          for (const x of h) {
            const m = x.split(`
`), S = m.find((I) => I.startsWith("event:"))?.slice(7), z = m.find((I) => I.startsWith("data:"))?.slice(5);
            S && z && this._handleSseEvent(S, JSON.parse(z));
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
        const a = e.content;
        if (!a) break;
        this._toolBatchOpen = !1, this._streamingText += a;
        const i = this._chatMessages.length - 1, s = this._chatMessages[i];
        s && s.role === "agent" && s.isStreaming ? this._chatMessages = [
          ...this._chatMessages.slice(0, i),
          { ...s, content: this._streamingText }
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
      case "tool.start": {
        this._finaliseStreamingMessage();
        const a = e.toolCallId, i = e.toolName, s = {
          toolCallId: a,
          toolName: i,
          summary: i,
          arguments: null,
          result: null,
          status: "running"
        }, r = this._chatMessages[this._chatMessages.length - 1];
        if (this._toolBatchOpen && r && r.role === "agent") {
          const o = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, o),
            { ...r, toolCalls: [...r.toolCalls ?? [], s] },
            ...this._chatMessages.slice(o + 1)
          ];
        } else if (r && r.role === "agent" && !r.toolCalls?.length) {
          const o = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, o),
            { ...r, toolCalls: [s] },
            ...this._chatMessages.slice(o + 1)
          ];
        } else
          this._chatMessages = [
            ...this._chatMessages,
            {
              role: "agent",
              content: "",
              timestamp: (/* @__PURE__ */ new Date()).toISOString(),
              toolCalls: [s]
            }
          ];
        this._toolBatchOpen = !0;
        break;
      }
      case "tool.args": {
        const a = e.toolCallId, i = e.arguments, s = this._chatMessages.findLastIndex(
          (h) => h.role === "agent" && h.toolCalls?.some((x) => x.toolCallId === a)
        );
        if (s === -1) break;
        const r = this._chatMessages[s], o = r.toolCalls.map(
          (h) => h.toolCallId === a ? { ...h, arguments: i, summary: U(h.toolName, i) } : h
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, s),
          { ...r, toolCalls: o },
          ...this._chatMessages.slice(s + 1)
        ];
        break;
      }
      case "tool.end": {
        const a = e.toolCallId, i = this._chatMessages.findLastIndex(
          (o) => o.role === "agent" && o.toolCalls?.some((h) => h.toolCallId === a)
        );
        if (i === -1) break;
        const s = this._chatMessages[i], r = s.toolCalls.map(
          (o) => o.toolCallId === a && o.status === "running" ? { ...o, status: "complete" } : o
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, i),
          { ...s, toolCalls: r },
          ...this._chatMessages.slice(i + 1)
        ];
        break;
      }
      case "tool.result": {
        const a = e.toolCallId, i = e.result, s = typeof i == "string" ? i : JSON.stringify(i), r = typeof s == "string" && s.startsWith("Tool '") && (s.includes("error") || s.includes("failed")), o = this._chatMessages.findLastIndex(
          (m) => m.role === "agent" && m.toolCalls?.some((S) => S.toolCallId === a)
        );
        if (o === -1) break;
        const h = this._chatMessages[o], x = h.toolCalls.map(
          (m) => m.toolCallId === a ? { ...m, result: s, status: r ? "error" : "complete" } : m
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, o),
          { ...h, toolCalls: x },
          ...this._chatMessages.slice(o + 1)
        ];
        break;
      }
      case "step.started":
        if (this._finaliseStreamingMessage(), this._toolBatchOpen = !1, this._instance) {
          const a = this._instance.steps.find((i) => i.id === e.stepId);
          a && (a.status = "Active"), this.requestUpdate();
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
          const s = this._instance.steps.find((r) => r.id === e.stepId);
          s && (s.status = e.status), this.requestUpdate();
        }
        const a = e.stepName || e.stepId, i = e.status === "Error" ? "failed" : "completed";
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `${a} ${i}`,
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
        const t = await C(this, y)?.getLatestToken();
        await j(this._instance.id, t), await this._loadData();
      } catch {
        console.warn("Failed to cancel instance");
      } finally {
        this._cancelling = !1;
      }
    }
  }
  _renderStepProgress() {
    return this._instance ? n`
      <div class="step-sidebar">
        <ul role="list">
          ${this._instance.steps.map(
      (t, e) => n`
              <li
                role="listitem"
                class="step-item
                  ${t.status === "Pending" ? "pending" : ""}
                  ${t.status === "Active" ? "active" : ""}
                  ${t.status === "Complete" ? "complete" : ""}
                  ${t.status === "Error" ? "error" : ""}
                  ${t.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === t.id ? "selected" : ""}"
                aria-label="Step ${e + 1}: ${t.name} — ${T(t)}"
                @click=${t.status !== "Pending" ? () => this._onStepClick(t) : l}
                aria-current=${t.status === "Active" ? "step" : l}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${t.status === "Active" ? "step-icon-spin" : ""}"
                    name=${Q(t.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${t.name}</span>
                  <span class="step-subtitle">${T(t)}</span>
                </div>
              </li>
            `
    )}
        </ul>
      </div>
    ` : l;
  }
  render() {
    if (this._loading)
      return n`<uui-loader></uui-loader>`;
    if (this._error || !this._instance)
      return n`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    const t = this._instance, e = t.status === "Pending" && !this._streaming, a = t.steps.some((o) => o.status === "Active"), i = t.status === "Running" && !a && !this._streaming && t.steps.some((o) => o.status === "Pending"), s = (t.status === "Running" || t.status === "Pending") && !this._streaming, r = this._providerError ? n`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : n`<shallai-chat-panel
          .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
          ?is-streaming=${this._streaming && !this._viewingStepId}
        ></shallai-chat-panel>`;
    return n`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${t.workflowName || t.workflowAlias} — Run #${this._runNumber}</h2>
        ${e ? n`
              <uui-button label="Start" look="primary" @click=${this._onStartClick}>
                Start
              </uui-button>
            ` : l}
        ${i ? n`
              <uui-button label="Continue" look="primary" @click=${this._onStartClick}>
                Continue
              </uui-button>
            ` : l}
        ${this._streaming ? n`<uui-loader-bar></uui-loader-bar>` : l}
        ${s ? n`
              <uui-button
                label="Cancel"
                look="secondary"
                color="danger"
                ?disabled=${this._cancelling}
                @click=${this._onCancelClick}
              >
                Cancel
              </uui-button>
            ` : l}
      </div>

      <div class="detail-grid">
        ${this._renderStepProgress()}
        ${r}
      </div>
    `;
  }
};
y = /* @__PURE__ */ new WeakMap();
c.styles = k`
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
d([
  u()
], c.prototype, "_instance", 2);
d([
  u()
], c.prototype, "_loading", 2);
d([
  u()
], c.prototype, "_error", 2);
d([
  u()
], c.prototype, "_selectedStepId", 2);
d([
  u()
], c.prototype, "_cancelling", 2);
d([
  u()
], c.prototype, "_runNumber", 2);
d([
  u()
], c.prototype, "_streaming", 2);
d([
  u()
], c.prototype, "_providerError", 2);
d([
  u()
], c.prototype, "_chatMessages", 2);
d([
  u()
], c.prototype, "_streamingText", 2);
d([
  u()
], c.prototype, "_viewingStepId", 2);
d([
  u()
], c.prototype, "_historyMessages", 2);
c = d([
  M("shallai-instance-detail")
], c);
const lt = c;
export {
  c as ShallaiInstanceDetailElement,
  lt as default
};
//# sourceMappingURL=shallai-instance-detail.element-CdKJdqKV.js.map
