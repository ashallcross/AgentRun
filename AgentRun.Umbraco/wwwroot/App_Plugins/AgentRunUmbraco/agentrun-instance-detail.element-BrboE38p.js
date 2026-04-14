import { UmbLitElement as M } from "@umbraco-cms/backoffice/lit-element";
import { css as I, property as d, state as c, customElement as z, html as r, nothing as p } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as F } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as U } from "@umbraco-cms/backoffice/modal";
import { s as W, b as j, a as V, d as T, m as P, e as q, r as K, f as J, h as O, i as G } from "./index-OBKXZ3lQ.js";
import { n as X } from "./instance-list-helpers-Cp7Qb08Y.js";
var Y = Object.defineProperty, Q = Object.getOwnPropertyDescriptor, k = (t, e, s, i) => {
  for (var a = i > 1 ? void 0 : i ? Q(e, s) : e, o = t.length - 1, n; o >= 0; o--)
    (n = t[o]) && (a = (i ? n(e, s, a) : n(a)) || a);
  return i && a && Y(e, s, a), a;
};
let b = class extends M {
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
    return this.status === "running" ? r`<uui-loader></uui-loader>` : this.status === "error" ? r`<uui-icon class="status-icon error" name="icon-alert"></uui-icon>` : r`<uui-icon class="status-icon complete" name="icon-check"></uui-icon>`;
  }
  _renderBody() {
    return this._expanded ? r`
      <div class="tool-call-body">
        ${this.arguments ? r`
              <div class="section-label">Arguments</div>
              <div class="mono-block">${JSON.stringify(this.arguments, null, 2)}</div>
            ` : p}
        ${this.result != null ? r`
              <div class="section-label">Result</div>
              <div class="mono-block">${this.result}</div>
            ` : p}
      </div>
    ` : p;
  }
  render() {
    const t = this.status !== "running";
    return r`
      <div
        class="tool-call ${this.status}"
        role="group"
        aria-label="Tool call: ${this.toolName}"
        aria-expanded=${t ? String(this._expanded) : p}
        tabindex=${t ? "0" : p}
        @click=${this._toggle}
        @keydown=${this._onKeydown}
      >
        <div class="tool-call-header">
          ${this._renderStatusIcon()}
          <span class="tool-name">${this.toolName}</span>
          <span class="tool-summary">${this.summary ? `— ${this.summary}` : ""}</span>
          ${t ? r`<uui-icon class="chevron ${this._expanded ? "open" : ""}" name="icon-navigation-down"></uui-icon>` : p}
        </div>
        ${this._renderBody()}
      </div>
    `;
  }
};
b.styles = I`
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
k([
  d({ type: String })
], b.prototype, "toolName", 2);
k([
  d({ type: String })
], b.prototype, "toolCallId", 2);
k([
  d({ type: String })
], b.prototype, "summary", 2);
k([
  d({ attribute: !1 })
], b.prototype, "arguments", 2);
k([
  d({ type: String })
], b.prototype, "result", 2);
k([
  d({ type: String })
], b.prototype, "status", 2);
k([
  c()
], b.prototype, "_expanded", 2);
b = k([
  z("agentrun-tool-call")
], b);
var Z = Object.defineProperty, tt = Object.getOwnPropertyDescriptor, $ = (t, e, s, i) => {
  for (var a = i > 1 ? void 0 : i ? tt(e, s) : e, o = t.length - 1, n; o >= 0; o--)
    (n = t[o]) && (a = (i ? n(e, s, a) : n(a)) || a);
  return i && a && Z(e, s, a), a;
};
let x = class extends M {
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
  _renderMarkdown(t) {
    const e = W(t), s = document.createElement("div");
    return s.className = "agent-content", s.innerHTML = e, r`${this._templateFromNode(s)}`;
  }
  _templateFromNode(t) {
    const e = document.createElement("div");
    return e.className = "agent-content", e.innerHTML = t.innerHTML, r`<div class="agent-content" .innerHTML=${t.innerHTML}></div>`;
  }
  render() {
    if (this.role === "system")
      return r`
        <div
          class="system-message"
          role="listitem"
          aria-label="System notification"
        >
          ${this.content}
        </div>
      `;
    if (this.role === "user")
      return r`
        <div class="user-message" role="listitem" aria-label="Your message">
          <span class="user-content">${this.content}</span>
          ${this.timestamp ? r`<span class="user-timestamp">${this._formatTime(this.timestamp)}</span>` : p}
        </div>
      `;
    const t = this.isStreaming ? r`<span class="agent-content" style="white-space:pre-wrap">${this.content}<span class="cursor">▋</span></span>` : this._renderMarkdown(this.content);
    return r`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        ${t}
        ${this.toolCalls.map((e) => r`
          <agentrun-tool-call
            .toolName=${e.toolName}
            .toolCallId=${e.toolCallId}
            .summary=${e.summary}
            .arguments=${e.arguments}
            .result=${e.result}
            .status=${e.status}
          ></agentrun-tool-call>
        `)}
        ${this.timestamp ? r`<span class="agent-timestamp"
              >${this._formatTime(this.timestamp)}</span
            >` : p}
      </div>
    `;
  }
};
x.styles = I`
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
      word-break: break-word;
    }

    .agent-content :is(h1, h2, h3, h4, h5, h6) {
      margin: var(--uui-size-space-3) 0 var(--uui-size-space-2) 0;
    }

    .agent-content :is(h1, h2, h3, h4, h5, h6):first-child {
      margin-top: 0;
    }

    .agent-content p {
      margin: 0 0 var(--uui-size-space-2) 0;
    }

    .agent-content p:last-child {
      margin-bottom: 0;
    }

    .agent-content pre {
      background: var(--uui-color-surface-emphasis);
      padding: var(--uui-size-space-3);
      border-radius: var(--uui-border-radius);
      overflow-x: auto;
      font-family: monospace;
      font-size: var(--uui-font-size-s);
    }

    .agent-content code {
      background: var(--uui-color-surface-emphasis);
      padding: 1px 4px;
      border-radius: 3px;
      font-family: monospace;
      font-size: var(--uui-font-size-s);
    }

    .agent-content pre code {
      background: none;
      padding: 0;
    }

    .agent-content a {
      color: var(--uui-color-interactive);
    }

    .agent-content blockquote {
      border-left: 3px solid var(--uui-color-border);
      padding-left: var(--uui-size-space-3);
      margin: var(--uui-size-space-2) 0;
      color: var(--uui-color-text-alt);
    }

    .agent-content ul,
    .agent-content ol {
      padding-left: var(--uui-size-space-5);
      margin: var(--uui-size-space-2) 0;
    }

    .agent-content hr {
      border: none;
      border-top: 1px solid var(--uui-color-border);
      margin: var(--uui-size-space-3) 0;
    }

    .agent-timestamp {
      display: block;
      margin-top: var(--uui-size-space-2);
      color: var(--uui-color-text-alt);
      font-size: var(--uui-font-size-s);
    }

    .user-message {
      background: var(--uui-color-surface-emphasis);
      padding: var(--uui-size-space-4);
      border-radius: var(--uui-border-radius);
      text-align: left;
    }

    .user-content {
      white-space: pre-wrap;
      word-break: break-word;
    }

    .user-timestamp {
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
$([
  d({ type: String })
], x.prototype, "role", 2);
$([
  d({ type: String })
], x.prototype, "content", 2);
$([
  d({ type: String })
], x.prototype, "timestamp", 2);
$([
  d({ type: Boolean, attribute: "is-streaming" })
], x.prototype, "isStreaming", 2);
$([
  d({ attribute: !1 })
], x.prototype, "toolCalls", 2);
x = $([
  z("agentrun-chat-message")
], x);
var et = Object.defineProperty, st = Object.getOwnPropertyDescriptor, w = (t, e, s, i) => {
  for (var a = i > 1 ? void 0 : i ? st(e, s) : e, o = t.length - 1, n; o >= 0; o--)
    (n = t[o]) && (a = (i ? n(e, s, a) : n(a)) || a);
  return i && a && et(e, s, a), a;
};
let f = class extends M {
  constructor() {
    super(...arguments), this.messages = [], this.isStreaming = !1, this.inputEnabled = !1, this.inputPlaceholder = "Send a message to start.", this._inputValue = "", this._autoScrollPaused = !1, this._hasNewMessages = !1, this._wasStreaming = !1;
  }
  _onScroll(t) {
    const e = t.target;
    if (!e) return;
    const { scrollTop: s, scrollHeight: i, clientHeight: a } = e;
    i - s - a < 50 ? (this._autoScrollPaused = !1, this._hasNewMessages = !1) : this._autoScrollPaused = !0;
  }
  _scrollToBottom() {
    const t = this.renderRoot.querySelector("uui-scroll-container");
    t && (t.scrollTop = t.scrollHeight, this._autoScrollPaused = !1, this._hasNewMessages = !1);
  }
  _onInput(t) {
    this._inputValue = t.target.value;
  }
  _onKeydown(t) {
    t.key === "Enter" && !t.shiftKey && (t.preventDefault(), this._onSend());
  }
  _onSend() {
    const t = this._inputValue.trim();
    !t || !this.inputEnabled || (this._inputValue = "", this.dispatchEvent(new CustomEvent("send-message", {
      detail: { message: t },
      bubbles: !0,
      composed: !0
    })));
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
      return r`<div class="empty-state">Start this workflow to begin.</div>`;
    const t = this.messages.length - 1;
    return r`
      <uui-scroll-container @scroll=${this._onScroll}>
        <div class="message-list" role="log" aria-live="polite">
          ${this.messages.map(
      (e, s) => r`
              <agentrun-chat-message
                .role=${e.role}
                .content=${e.content}
                .timestamp=${e.timestamp}
                .toolCalls=${e.toolCalls ?? []}
                ?is-streaming=${s === t && e.role === "agent" && this.isStreaming}
              ></agentrun-chat-message>
            `
    )}
        </div>
      </uui-scroll-container>

      ${this._autoScrollPaused && this._hasNewMessages ? r`
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

      <div class="input-area">
        <uui-textarea
          aria-label="Message to agent"
          placeholder=${this.inputEnabled ? "Message the agent..." : this.inputPlaceholder}
          ?disabled=${!this.inputEnabled}
          .value=${this._inputValue}
          @input=${this._onInput}
          @keydown=${this._onKeydown}
        ></uui-textarea>
        <uui-button
          label="Send"
          look="primary"
          ?disabled=${!this.inputEnabled || !this._inputValue.trim()}
          @click=${this._onSend}
        >
          Send
        </uui-button>
      </div>

      <div class="sr-only" aria-live="assertive">
        ${this.isStreaming ? "Agent is responding..." : ""}
        ${!this.isStreaming && this._wasStreaming ? "Response complete" : ""}
      </div>
    `;
  }
};
f.styles = I`
    :host {
      display: block;
      position: relative;
    }

    uui-scroll-container {
      height: 600px;
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
      display: flex;
      justify-content: center;
      padding: var(--uui-size-space-2) 0;
    }

    .input-area {
      display: flex;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-3);
      border-top: 1px solid var(--uui-color-border);
      align-items: flex-end;
      flex-shrink: 0;
    }

    .input-area uui-button {
      flex-shrink: 0;
    }

    .input-area uui-textarea {
      flex: 1;
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
  d({ attribute: !1 })
], f.prototype, "messages", 2);
w([
  d({ type: Boolean, attribute: "is-streaming" })
], f.prototype, "isStreaming", 2);
w([
  d({ type: Boolean, attribute: "input-enabled" })
], f.prototype, "inputEnabled", 2);
w([
  d({ type: String, attribute: "input-placeholder" })
], f.prototype, "inputPlaceholder", 2);
w([
  c()
], f.prototype, "_inputValue", 2);
w([
  c()
], f.prototype, "_autoScrollPaused", 2);
w([
  c()
], f.prototype, "_hasNewMessages", 2);
w([
  c()
], f.prototype, "_wasStreaming", 2);
f = w([
  z("agentrun-chat-panel")
], f);
function at(t) {
  const e = t.split("/");
  return decodeURIComponent(e[e.length - 1]);
}
function it(t, e) {
  const s = t.split("/");
  return s.length >= 2 && s.splice(-2, 2, "workflows", encodeURIComponent(e)), s.join("/");
}
function A(t) {
  switch (t.status) {
    case "Pending":
      return "Pending";
    case "Active":
      return "In progress";
    case "Complete":
      return t.writesTo?.[0] ?? "Complete";
    case "Error":
      return "Error";
    default:
      return t.status;
  }
}
function nt(t) {
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
function ot(t, e) {
  return t === "Active" && e;
}
var rt = Object.defineProperty, lt = Object.getOwnPropertyDescriptor, E = (t) => {
  throw TypeError(t);
}, h = (t, e, s, i) => {
  for (var a = i > 1 ? void 0 : i ? lt(e, s) : e, o = t.length - 1, n; o >= 0; o--)
    (n = t[o]) && (a = (i ? n(e, s, a) : n(a)) || a);
  return i && a && rt(e, s, a), a;
}, N = (t, e, s) => e.has(t) || E("Cannot " + s), C = (t, e, s) => (N(t, e, "read from private field"), s ? s.call(t) : e.get(t)), ct = (t, e, s) => e.has(t) ? E("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, s), ut = (t, e, s, i) => (N(t, e, "write to private field"), e.set(t, s), s), v;
let u = class extends M {
  constructor() {
    super(), ct(this, v), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this._streaming = !1, this._providerError = !1, this._chatMessages = [], this._streamingText = "", this._viewingStepId = null, this._historyMessages = [], this._stepCompletable = !1, this._agentResponding = !1, this._retrying = !1, this._popoverOpen = !1, this._popoverArtifactPath = null, this._toolBatchOpen = !1, this.consumeContext(F, (t) => {
      ut(this, v, t);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const t = at(window.location.pathname), e = await C(this, v)?.getLatestToken(), s = await j(t, e);
      this._instance = s;
      const i = await V(s.workflowAlias, e), o = X(i).find((n) => n.id === s.id);
      if (this._runNumber = o?.runNumber ?? 0, !this._streaming && this._chatMessages.length === 0) {
        const n = s.steps.find(
          (l) => l.status === "Active" || l.status === "Error"
        ) ?? s.steps.findLast((l) => l.status === "Complete");
        if (n)
          try {
            const l = await T(s.id, n.id, e);
            this._chatMessages = P(l);
          } catch {
          }
      }
      this._error = !1;
    } catch {
      this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateBack() {
    if (!this._instance) return;
    const t = it(
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
        const e = await C(this, v)?.getLatestToken(), s = this._instance.id, i = await T(s, t.id, e);
        this._historyMessages = P(i);
      } catch {
        this._historyMessages = [];
      }
    }
  }
  disconnectedCallback() {
    super.disconnectedCallback(), this._streaming = !1, this._agentResponding = !1;
  }
  async _onStartClick() {
    if (this._streaming || !this._instance) return;
    const t = await C(this, v)?.getLatestToken(), e = await q(this._instance.id, t);
    if (!e.ok) {
      if (e.status === 400) {
        this._providerError = !0;
        return;
      }
      await this._loadData();
      return;
    }
    await this._streamSseResponse(e);
  }
  async _onRetryClick() {
    if (this._retrying || this._streaming || !this._instance) return;
    this._retrying = !0, this._chatMessages = [];
    const t = await C(this, v)?.getLatestToken(), e = await K(this._instance.id, t);
    if (!e.ok) {
      this._retrying = !1, e.status === 409 && (this._chatMessages = [
        {
          role: "system",
          content: "Cannot retry — instance is no longer in a failed state.",
          timestamp: (/* @__PURE__ */ new Date()).toISOString()
        }
      ]), await this._loadData();
      return;
    }
    await this._streamSseResponse(e), this._retrying = !1;
  }
  async _streamSseResponse(t) {
    this._streaming = !0, this._providerError = !1, this._viewingStepId = null, this._streamingText = "";
    try {
      if (!t.body) return;
      const e = t.body.getReader(), s = new TextDecoder();
      let i = "";
      for (; ; ) {
        const { done: a, value: o } = await e.read();
        if (a) break;
        i += s.decode(o, { stream: !0 });
        const n = i.split(`

`);
        i = n.pop();
        for (const l of n) {
          const S = l.split(`
`), m = S.find((_) => _.startsWith("event:"))?.slice(7), g = S.find((_) => _.startsWith("data:"))?.slice(5);
          m && g && this._handleSseEvent(m, JSON.parse(g));
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
      this._streaming = !1, this._agentResponding = !1, await this._loadData(), this._checkStepCompletable();
    }
  }
  _checkStepCompletable() {
    if (!this._instance) return;
    if (!(this._instance.workflowMode !== "autonomous")) {
      this._stepCompletable = !1;
      return;
    }
    const s = !this._instance.steps.find((a) => a.status === "Active") && this._instance.steps.some((a) => a.status === "Complete"), i = this._instance.steps.some((a) => a.status === "Pending");
    this._stepCompletable = s && i;
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
    switch (["text.delta", "tool.start"].includes(t) && (this._agentResponding = !0), t) {
      case "text.delta": {
        const s = e.content;
        if (!s) break;
        this._toolBatchOpen = !1, this._streamingText += s;
        const i = this._chatMessages.length - 1, a = this._chatMessages[i];
        a && a.role === "agent" && a.isStreaming ? this._chatMessages = [
          ...this._chatMessages.slice(0, i),
          { ...a, content: this._streamingText }
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
        const s = e.toolCallId, i = e.toolName, a = {
          toolCallId: s,
          toolName: i,
          summary: i,
          arguments: null,
          result: null,
          status: "running"
        }, o = this._chatMessages[this._chatMessages.length - 1];
        if (this._toolBatchOpen && o && o.role === "agent") {
          const n = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, n),
            { ...o, toolCalls: [...o.toolCalls ?? [], a] },
            ...this._chatMessages.slice(n + 1)
          ];
        } else if (o && o.role === "agent" && !o.toolCalls?.length) {
          const n = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, n),
            { ...o, toolCalls: [a] },
            ...this._chatMessages.slice(n + 1)
          ];
        } else
          this._chatMessages = [
            ...this._chatMessages,
            {
              role: "agent",
              content: "",
              timestamp: (/* @__PURE__ */ new Date()).toISOString(),
              toolCalls: [a]
            }
          ];
        this._toolBatchOpen = !0;
        break;
      }
      case "tool.args": {
        const s = e.toolCallId, i = e.arguments, a = this._chatMessages.findLastIndex(
          (l) => l.role === "agent" && l.toolCalls?.some((S) => S.toolCallId === s)
        );
        if (a === -1) break;
        const o = this._chatMessages[a], n = o.toolCalls.map(
          (l) => l.toolCallId === s ? { ...l, arguments: i, summary: J(l.toolName, i) } : l
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, a),
          { ...o, toolCalls: n },
          ...this._chatMessages.slice(a + 1)
        ];
        break;
      }
      case "tool.end": {
        const s = e.toolCallId, i = this._chatMessages.findLastIndex(
          (n) => n.role === "agent" && n.toolCalls?.some((l) => l.toolCallId === s)
        );
        if (i === -1) break;
        const a = this._chatMessages[i], o = a.toolCalls.map(
          (n) => n.toolCallId === s && n.status === "running" ? { ...n, status: "complete" } : n
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, i),
          { ...a, toolCalls: o },
          ...this._chatMessages.slice(i + 1)
        ];
        break;
      }
      case "tool.result": {
        const s = e.toolCallId, i = e.result, a = typeof i == "string" ? i : JSON.stringify(i), o = typeof a == "string" && a.startsWith("Tool '") && (a.includes("error") || a.includes("failed")), n = this._chatMessages.findLastIndex(
          (m) => m.role === "agent" && m.toolCalls?.some((g) => g.toolCallId === s)
        );
        if (n === -1) break;
        const l = this._chatMessages[n], S = l.toolCalls.map(
          (m) => m.toolCallId === s ? { ...m, result: a, status: o ? "error" : "complete" } : m
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, n),
          { ...l, toolCalls: S },
          ...this._chatMessages.slice(n + 1)
        ];
        break;
      }
      case "step.started":
        if (this._finaliseStreamingMessage(), this._toolBatchOpen = !1, this._instance) {
          const s = this._instance.steps.find((i) => i.id === e.stepId);
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
          const a = this._instance.steps.find((o) => o.id === e.stepId);
          a && (a.status = e.status), this.requestUpdate();
        }
        const s = e.stepName || e.stepId, i = e.status === "Error" ? "failed" : "completed";
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `${s} ${i}`,
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
        break;
      }
      case "run.finished":
        if (this._finaliseStreamingMessage(), this._instance) {
          this._instance.status = "Completed", this.requestUpdate();
          const s = [...this._instance.steps].reverse().find((i) => i.status === "Complete" && i.writesTo && i.writesTo.length > 0);
          s && s.writesTo && (this._popoverArtifactPath = s.writesTo[s.writesTo.length - 1], this._popoverOpen = !0), this._chatMessages = [
            ...this._chatMessages,
            {
              role: "system",
              content: "Workflow complete.",
              timestamp: (/* @__PURE__ */ new Date()).toISOString()
            }
          ];
        }
        break;
      case "user.message":
        break;
      case "input.wait":
        this._finaliseStreamingMessage(), this._agentResponding = !1;
        break;
      case "run.error":
        this._finaliseStreamingMessage(), this._instance && (this._instance.status = "Failed", this.requestUpdate()), this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: e.message || "An error occurred.",
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
        break;
    }
  }
  async _onSendMessage(t) {
    if (!this._instance) return;
    const e = t.detail.message;
    this._chatMessages = [
      ...this._chatMessages,
      {
        role: "user",
        content: e,
        timestamp: (/* @__PURE__ */ new Date()).toISOString()
      }
    ];
    try {
      const s = await C(this, v)?.getLatestToken();
      await O(this._instance.id, e, s);
    } catch {
      this._chatMessages = [
        ...this._chatMessages,
        {
          role: "system",
          content: "Failed to send message. Try again.",
          timestamp: (/* @__PURE__ */ new Date()).toISOString()
        }
      ];
    }
  }
  async _onSendAndStream(t) {
    if (!this._instance) return;
    const e = t.detail.message;
    this._chatMessages = [
      ...this._chatMessages,
      {
        role: "user",
        content: e,
        timestamp: (/* @__PURE__ */ new Date()).toISOString()
      }
    ];
    try {
      const s = await C(this, v)?.getLatestToken();
      await O(this._instance.id, e, s);
    } catch {
      this._chatMessages = [
        ...this._chatMessages,
        {
          role: "system",
          content: "Failed to send message. Try again.",
          timestamp: (/* @__PURE__ */ new Date()).toISOString()
        }
      ];
      return;
    }
    this._stepCompletable = !1, await this._onStartClick();
  }
  async _onAdvanceStep() {
    !this._instance || this._streaming || (this._stepCompletable = !1, this._chatMessages = [], await this._onStartClick());
  }
  async _onCancelClick() {
    if (!(this._cancelling || !this._instance)) {
      try {
        await U(this, {
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
        const t = await C(this, v)?.getLatestToken();
        await G(this._instance.id, t), await this._loadData();
      } catch {
        console.warn("Failed to cancel instance");
      } finally {
        this._cancelling = !1;
      }
    }
  }
  _renderStepProgress() {
    return this._instance ? r`
      <div class="step-sidebar">
        <ul role="list">
          ${this._instance.steps.map(
      (t, e) => r`
              <li
                role="listitem"
                class="step-item
                  ${t.status === "Pending" ? "pending" : ""}
                  ${t.status === "Active" ? "active" : ""}
                  ${t.status === "Complete" ? "complete" : ""}
                  ${t.status === "Error" ? "error" : ""}
                  ${t.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === t.id ? "selected" : ""}"
                aria-label="Step ${e + 1}: ${t.name} — ${A(t)}"
                @click=${t.status !== "Pending" ? () => this._onStepClick(t) : p}
                aria-current=${t.status === "Active" ? "step" : p}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${ot(t.status, this._agentResponding) ? "step-icon-spin" : ""}"
                    name=${nt(t.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${t.name}</span>
                  <span class="step-subtitle">${A(t)}</span>
                </div>
              </li>
            `
    )}
        </ul>
        <hr class="sidebar-divider" />
        <agentrun-artifact-list
          .steps=${this._instance.steps}
          @artifact-selected=${this._onArtifactSelected}
        ></agentrun-artifact-list>
      </div>
    ` : p;
  }
  _onArtifactSelected(t) {
    this._popoverArtifactPath = t.detail.path, this._popoverOpen = !0;
  }
  _onPopoverClosed() {
    this._popoverOpen = !1;
  }
  render() {
    if (this._loading)
      return r`<uui-loader></uui-loader>`;
    if (this._error || !this._instance)
      return r`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    const t = this._instance, e = t.workflowMode !== "autonomous", s = t.status === "Completed" || t.status === "Failed" || t.status === "Cancelled", i = t.steps.find((y) => y.status === "Active"), a = !!i, o = t.status === "Pending" && !this._streaming, n = "Start", l = t.status === "Failed" && !this._streaming, S = !e && t.status === "Running" && !a && !this._streaming && t.steps.some((y) => y.status === "Pending"), m = t.status === "Running" || t.status === "Pending" || t.status === "Failed" && !this._streaming;
    let g, _;
    e ? s ? (g = !1, _ = "Workflow complete") : this._viewingStepId ? (g = !1, _ = "Viewing step history") : this._agentResponding ? (g = !1, _ = "Agent is responding...") : this._streaming || i || t.status === "Running" ? (g = !0, _ = "Message the agent...") : (g = !1, _ = "Send a message to start.") : (g = this._streaming && !this._viewingStepId, _ = s ? "Workflow complete" : i ? "Step complete" : "Click 'Start' to begin the workflow.");
    const R = e && !this._streaming ? (y) => this._onSendAndStream(y) : (y) => this._onSendMessage(y), D = e && this._stepCompletable && !this._agentResponding, L = t.steps.every((y) => y.status === "Complete"), B = e && s && L, H = this._providerError ? r`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : r`
          <div class="main-panel">
            ${D ? r`
                  <div class="completion-banner">
                    <span>Step complete — review the output, then advance when ready</span>
                    <uui-button label="Continue to next step" look="primary" @click=${this._onAdvanceStep}>
                      Continue to next step
                    </uui-button>
                  </div>
                ` : p}
            ${B ? r`
                  <div class="completion-banner">
                    <span>Workflow complete — all steps finished</span>
                  </div>
                ` : p}
            <agentrun-chat-panel
              .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
              ?is-streaming=${this._streaming && !this._viewingStepId}
              ?input-enabled=${g}
              input-placeholder=${_}
              @send-message=${R}
            ></agentrun-chat-panel>
          </div>`;
    return r`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${t.workflowName || t.workflowAlias} — Run #${this._runNumber}</h2>
        ${o ? r`
              <uui-button label=${n} look="primary" @click=${this._onStartClick}>
                ${n}
              </uui-button>
            ` : p}
        ${l ? r`
              <uui-button label="Retry" look="primary" ?disabled=${this._retrying} @click=${this._onRetryClick}>
                ${this._retrying ? r`<uui-loader></uui-loader>` : "Retry"}
              </uui-button>
            ` : p}
        ${S ? r`
              <uui-button label="Continue" look="primary" @click=${this._onStartClick}>
                Continue
              </uui-button>
            ` : p}
        ${m ? r`
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
        ${H}
      </div>
      <agentrun-artifact-popover
        .instanceId=${t.id}
        .artifactPath=${this._popoverArtifactPath ?? ""}
        ?open=${this._popoverOpen}
        @popover-closed=${this._onPopoverClosed}
      ></agentrun-artifact-popover>
    `;
  }
};
v = /* @__PURE__ */ new WeakMap();
u.styles = I`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      margin-bottom: var(--uui-size-space-4);
      flex-shrink: 0;
    }

    .header h2 {
      flex: 1;
      margin: 0;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      min-width: 0;
    }

    .detail-grid {
      display: grid;
      grid-template-columns: 240px 1fr;
      gap: var(--uui-size-layout-1);
    }

    .step-sidebar {
      border-right: 1px solid var(--uui-color-border);
      padding-right: var(--uui-size-layout-1);
      overflow-y: auto;
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

    .sidebar-divider {
      border: none;
      border-top: 1px solid var(--uui-color-border);
      margin: var(--uui-size-space-4) 0;
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

    .main-panel {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .completion-banner {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-3) var(--uui-size-space-4);
      background: var(--uui-color-positive-standalone);
      color: var(--uui-color-surface);
      border-radius: var(--uui-border-radius);
      margin-bottom: var(--uui-size-space-3);
      font-size: var(--uui-type-small-size);
    }

    .completion-banner uui-button {
      flex-shrink: 0;
    }
  `;
h([
  c()
], u.prototype, "_instance", 2);
h([
  c()
], u.prototype, "_loading", 2);
h([
  c()
], u.prototype, "_error", 2);
h([
  c()
], u.prototype, "_selectedStepId", 2);
h([
  c()
], u.prototype, "_cancelling", 2);
h([
  c()
], u.prototype, "_runNumber", 2);
h([
  c()
], u.prototype, "_streaming", 2);
h([
  c()
], u.prototype, "_providerError", 2);
h([
  c()
], u.prototype, "_chatMessages", 2);
h([
  c()
], u.prototype, "_streamingText", 2);
h([
  c()
], u.prototype, "_viewingStepId", 2);
h([
  c()
], u.prototype, "_historyMessages", 2);
h([
  c()
], u.prototype, "_stepCompletable", 2);
h([
  c()
], u.prototype, "_agentResponding", 2);
h([
  c()
], u.prototype, "_retrying", 2);
h([
  c()
], u.prototype, "_popoverOpen", 2);
h([
  c()
], u.prototype, "_popoverArtifactPath", 2);
u = h([
  z("agentrun-instance-detail")
], u);
const ft = u;
export {
  u as AgentRunInstanceDetailElement,
  ft as default
};
//# sourceMappingURL=agentrun-instance-detail.element-BrboE38p.js.map
