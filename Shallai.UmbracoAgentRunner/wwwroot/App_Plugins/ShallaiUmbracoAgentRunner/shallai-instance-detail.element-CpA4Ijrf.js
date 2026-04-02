import { UmbLitElement as M } from "@umbraco-cms/backoffice/lit-element";
import { css as I, property as d, state as c, customElement as z, html as r, nothing as p } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as F } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as U } from "@umbraco-cms/backoffice/modal";
import { b as j, a as V, d as T, m as P, s as W, r as q, e as K, f as E, h as J } from "./api-client-C-llcIxj.js";
import { s as G } from "./index-6tkhMXvN.js";
import { n as X } from "./instance-list-helpers-Cp7Qb08Y.js";
var Y = Object.defineProperty, Q = Object.getOwnPropertyDescriptor, k = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? Q(t, s) : t, o = e.length - 1, n; o >= 0; o--)
    (n = e[o]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && Y(t, s, a), a;
};
let b = class extends M {
  constructor() {
    super(...arguments), this.toolName = "", this.toolCallId = "", this.summary = "", this.arguments = null, this.result = null, this.status = "running", this._expanded = !1;
  }
  _toggle() {
    this.status !== "running" && (this._expanded = !this._expanded);
  }
  _onKeydown(e) {
    (e.key === "Enter" || e.key === " ") && (e.preventDefault(), this._toggle());
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
    const e = this.status !== "running";
    return r`
      <div
        class="tool-call ${this.status}"
        role="group"
        aria-label="Tool call: ${this.toolName}"
        aria-expanded=${e ? String(this._expanded) : p}
        tabindex=${e ? "0" : p}
        @click=${this._toggle}
        @keydown=${this._onKeydown}
      >
        <div class="tool-call-header">
          ${this._renderStatusIcon()}
          <span class="tool-name">${this.toolName}</span>
          <span class="tool-summary">${this.summary ? `— ${this.summary}` : ""}</span>
          ${e ? r`<uui-icon class="chevron ${this._expanded ? "open" : ""}" name="icon-navigation-down"></uui-icon>` : p}
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
  z("shallai-tool-call")
], b);
var Z = Object.defineProperty, ee = Object.getOwnPropertyDescriptor, $ = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? ee(t, s) : t, o = e.length - 1, n; o >= 0; o--)
    (n = e[o]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && Z(t, s, a), a;
};
let x = class extends M {
  constructor() {
    super(...arguments), this.role = "agent", this.content = "", this.timestamp = "", this.isStreaming = !1, this.toolCalls = [];
  }
  _formatTime(e) {
    if (!e) return "";
    try {
      return new Date(e).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    } catch {
      return "";
    }
  }
  _renderMarkdown(e) {
    const t = G(e), s = document.createElement("div");
    return s.className = "agent-content", s.innerHTML = t, r`${this._templateFromNode(s)}`;
  }
  _templateFromNode(e) {
    const t = document.createElement("div");
    return t.className = "agent-content", t.innerHTML = e.innerHTML, r`<div class="agent-content" .innerHTML=${e.innerHTML}></div>`;
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
    const e = this.isStreaming ? r`<span class="agent-content" style="white-space:pre-wrap">${this.content}<span class="cursor">▋</span></span>` : this._renderMarkdown(this.content);
    return r`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        ${e}
        ${this.toolCalls.map((t) => r`
          <shallai-tool-call
            .toolName=${t.toolName}
            .toolCallId=${t.toolCallId}
            .summary=${t.summary}
            .arguments=${t.arguments}
            .result=${t.result}
            .status=${t.status}
          ></shallai-tool-call>
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
  z("shallai-chat-message")
], x);
var te = Object.defineProperty, se = Object.getOwnPropertyDescriptor, w = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? se(t, s) : t, o = e.length - 1, n; o >= 0; o--)
    (n = e[o]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && te(t, s, a), a;
};
let f = class extends M {
  constructor() {
    super(...arguments), this.messages = [], this.isStreaming = !1, this.inputEnabled = !1, this.inputPlaceholder = "Send a message to start.", this._inputValue = "", this._autoScrollPaused = !1, this._hasNewMessages = !1, this._wasStreaming = !1;
  }
  _onScroll(e) {
    const t = e.target;
    if (!t) return;
    const { scrollTop: s, scrollHeight: i, clientHeight: a } = t;
    i - s - a < 50 ? (this._autoScrollPaused = !1, this._hasNewMessages = !1) : this._autoScrollPaused = !0;
  }
  _scrollToBottom() {
    const e = this.renderRoot.querySelector("uui-scroll-container");
    e && (e.scrollTop = e.scrollHeight, this._autoScrollPaused = !1, this._hasNewMessages = !1);
  }
  _onInput(e) {
    this._inputValue = e.target.value;
  }
  _onKeydown(e) {
    e.key === "Enter" && !e.shiftKey && (e.preventDefault(), this._onSend());
  }
  _onSend() {
    const e = this._inputValue.trim();
    !e || !this.inputEnabled || (this._inputValue = "", this.dispatchEvent(new CustomEvent("send-message", {
      detail: { message: e },
      bubbles: !0,
      composed: !0
    })));
  }
  updated() {
    this._wasStreaming && !this.isStreaming ? this._wasStreaming = !1 : this.isStreaming && (this._wasStreaming = !0);
    const e = this.renderRoot.querySelector("uui-scroll-container");
    if (!e) return;
    const t = e;
    t.scrollHeight - t.scrollTop - t.clientHeight < 50 || !this._autoScrollPaused ? t.scrollTop = t.scrollHeight : this.isStreaming && (this._hasNewMessages = !0);
  }
  render() {
    if (this.messages.length === 0)
      return r`<div class="empty-state">Start this workflow to begin.</div>`;
    const e = this.messages.length - 1;
    return r`
      <uui-scroll-container @scroll=${this._onScroll}>
        <div class="message-list" role="log" aria-live="polite">
          ${this.messages.map(
      (t, s) => r`
              <shallai-chat-message
                .role=${t.role}
                .content=${t.content}
                .timestamp=${t.timestamp}
                .toolCalls=${t.toolCalls ?? []}
                ?is-streaming=${s === e && t.role === "agent" && this.isStreaming}
              ></shallai-chat-message>
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
  z("shallai-chat-panel")
], f);
function ae(e) {
  const t = e.split("/");
  return decodeURIComponent(t[t.length - 1]);
}
function ie(e, t) {
  const s = e.split("/");
  return s.length >= 2 && s.splice(-2, 2, "workflows", encodeURIComponent(t)), s.join("/");
}
function N(e) {
  switch (e.status) {
    case "Pending":
      return "Pending";
    case "Active":
      return "In progress";
    case "Complete":
      return e.writesTo?.[0] ?? "Complete";
    case "Error":
      return "Error";
    default:
      return e.status;
  }
}
function ne(e) {
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
function oe(e, t) {
  return e === "Active" && t;
}
var re = Object.defineProperty, le = Object.getOwnPropertyDescriptor, O = (e) => {
  throw TypeError(e);
}, h = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? le(t, s) : t, o = e.length - 1, n; o >= 0; o--)
    (n = e[o]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && re(t, s, a), a;
}, D = (e, t, s) => t.has(e) || O("Cannot " + s), C = (e, t, s) => (D(e, t, "read from private field"), s ? s.call(e) : t.get(e)), ce = (e, t, s) => t.has(e) ? O("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, s), ue = (e, t, s, i) => (D(e, t, "write to private field"), t.set(e, s), s), v;
let u = class extends M {
  constructor() {
    super(), ce(this, v), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this._streaming = !1, this._providerError = !1, this._chatMessages = [], this._streamingText = "", this._viewingStepId = null, this._historyMessages = [], this._stepCompletable = !1, this._agentResponding = !1, this._retrying = !1, this._toolBatchOpen = !1, this.consumeContext(F, (e) => {
      ue(this, v, e);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const e = ae(window.location.pathname), t = await C(this, v)?.getLatestToken(), s = await j(e, t);
      this._instance = s;
      const i = await V(s.workflowAlias, t), o = X(i).find((n) => n.id === s.id);
      if (this._runNumber = o?.runNumber ?? 0, !this._streaming && this._chatMessages.length === 0) {
        const n = s.steps.find(
          (l) => l.status === "Active" || l.status === "Error"
        ) ?? s.steps.findLast((l) => l.status === "Complete");
        if (n)
          try {
            const l = await T(s.id, n.id, t);
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
    const e = ie(
      window.location.pathname,
      this._instance.workflowAlias
    );
    window.history.pushState({}, "", e);
  }
  async _onStepClick(e) {
    if (e.status !== "Pending") {
      if (this._selectedStepId = e.id, this.dispatchEvent(
        new CustomEvent("step-selected", {
          detail: { stepId: e.id, status: e.status },
          bubbles: !0,
          composed: !0
        })
      ), e.status === "Active") {
        this._viewingStepId = null;
        return;
      }
      this._viewingStepId = e.id;
      try {
        const t = await C(this, v)?.getLatestToken(), s = this._instance.id, i = await T(s, e.id, t);
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
    const e = await C(this, v)?.getLatestToken(), t = await W(this._instance.id, e);
    if (!t.ok) {
      if (t.status === 400) {
        this._providerError = !0;
        return;
      }
      await this._loadData();
      return;
    }
    await this._streamSseResponse(t);
  }
  async _onRetryClick() {
    if (this._retrying || this._streaming || !this._instance) return;
    this._retrying = !0, this._chatMessages = [];
    const e = await C(this, v)?.getLatestToken(), t = await q(this._instance.id, e);
    if (!t.ok) {
      this._retrying = !1, t.status === 409 && (this._chatMessages = [
        {
          role: "system",
          content: "Cannot retry — instance is no longer in a failed state.",
          timestamp: (/* @__PURE__ */ new Date()).toISOString()
        }
      ]), await this._loadData();
      return;
    }
    await this._streamSseResponse(t), this._retrying = !1;
  }
  async _streamSseResponse(e) {
    this._streaming = !0, this._providerError = !1, this._viewingStepId = null, this._streamingText = "";
    try {
      if (!e.body) return;
      const t = e.body.getReader(), s = new TextDecoder();
      let i = "";
      for (; ; ) {
        const { done: a, value: o } = await t.read();
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
    const e = this._chatMessages.length - 1, t = this._chatMessages[e];
    t && t.role === "agent" && t.isStreaming && (this._chatMessages = [
      ...this._chatMessages.slice(0, e),
      { ...t, content: this._streamingText, isStreaming: !1 }
    ]), this._streamingText = "";
  }
  _handleSseEvent(e, t) {
    switch (["text.delta", "tool.start"].includes(e) && (this._agentResponding = !0), e) {
      case "text.delta": {
        const s = t.content;
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
        const s = t.toolCallId, i = t.toolName, a = {
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
        const s = t.toolCallId, i = t.arguments, a = this._chatMessages.findLastIndex(
          (l) => l.role === "agent" && l.toolCalls?.some((S) => S.toolCallId === s)
        );
        if (a === -1) break;
        const o = this._chatMessages[a], n = o.toolCalls.map(
          (l) => l.toolCallId === s ? { ...l, arguments: i, summary: K(l.toolName, i) } : l
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, a),
          { ...o, toolCalls: n },
          ...this._chatMessages.slice(a + 1)
        ];
        break;
      }
      case "tool.end": {
        const s = t.toolCallId, i = this._chatMessages.findLastIndex(
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
        const s = t.toolCallId, i = t.result, a = typeof i == "string" ? i : JSON.stringify(i), o = typeof a == "string" && a.startsWith("Tool '") && (a.includes("error") || a.includes("failed")), n = this._chatMessages.findLastIndex(
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
          const s = this._instance.steps.find((i) => i.id === t.stepId);
          s && (s.status = "Active"), this.requestUpdate();
        }
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `Starting ${t.stepName || t.stepId}...`,
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
        break;
      case "step.finished": {
        if (this._finaliseStreamingMessage(), this._instance) {
          const a = this._instance.steps.find((o) => o.id === t.stepId);
          a && (a.status = t.status), this.requestUpdate();
        }
        const s = t.stepName || t.stepId, i = t.status === "Error" ? "failed" : "completed";
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
        this._finaliseStreamingMessage(), this._instance && (this._instance.status = "Completed", this.requestUpdate());
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
            content: t.message || "An error occurred.",
            timestamp: (/* @__PURE__ */ new Date()).toISOString()
          }
        ];
        break;
    }
  }
  async _onSendMessage(e) {
    if (!this._instance) return;
    const t = e.detail.message;
    this._chatMessages = [
      ...this._chatMessages,
      {
        role: "user",
        content: t,
        timestamp: (/* @__PURE__ */ new Date()).toISOString()
      }
    ];
    try {
      const s = await C(this, v)?.getLatestToken();
      await E(this._instance.id, t, s);
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
  async _onSendAndStream(e) {
    if (!this._instance) return;
    const t = e.detail.message;
    this._chatMessages = [
      ...this._chatMessages,
      {
        role: "user",
        content: t,
        timestamp: (/* @__PURE__ */ new Date()).toISOString()
      }
    ];
    try {
      const s = await C(this, v)?.getLatestToken();
      await E(this._instance.id, t, s);
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
        const e = await C(this, v)?.getLatestToken();
        await J(this._instance.id, e), await this._loadData();
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
      (e, t) => r`
              <li
                role="listitem"
                class="step-item
                  ${e.status === "Pending" ? "pending" : ""}
                  ${e.status === "Active" ? "active" : ""}
                  ${e.status === "Complete" ? "complete" : ""}
                  ${e.status === "Error" ? "error" : ""}
                  ${e.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === e.id ? "selected" : ""}"
                aria-label="Step ${t + 1}: ${e.name} — ${N(e)}"
                @click=${e.status !== "Pending" ? () => this._onStepClick(e) : p}
                aria-current=${e.status === "Active" ? "step" : p}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${oe(e.status, this._agentResponding) ? "step-icon-spin" : ""}"
                    name=${ne(e.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${e.name}</span>
                  <span class="step-subtitle">${N(e)}</span>
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
      return r`<uui-loader></uui-loader>`;
    if (this._error || !this._instance)
      return r`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    const e = this._instance, t = e.workflowMode !== "autonomous", s = e.status === "Completed" || e.status === "Failed" || e.status === "Cancelled", i = e.steps.find((y) => y.status === "Active"), a = !!i, o = e.status === "Pending" && !this._streaming, n = "Start", l = e.status === "Failed" && !this._streaming, S = !t && e.status === "Running" && !a && !this._streaming && e.steps.some((y) => y.status === "Pending"), m = !t && (e.status === "Running" || e.status === "Pending") && !this._streaming || e.status === "Failed" && !this._streaming;
    let g, _;
    t ? s ? (g = !1, _ = "Workflow complete") : this._viewingStepId ? (g = !1, _ = "Viewing step history") : this._agentResponding ? (g = !1, _ = "Agent is responding...") : this._streaming || i || e.status === "Running" ? (g = !0, _ = "Message the agent...") : (g = !1, _ = "Send a message to start.") : (g = this._streaming && !this._viewingStepId, _ = s ? "Workflow complete" : i ? "Step complete" : "Click 'Start' to begin the workflow.");
    const R = t && !this._streaming ? (y) => this._onSendAndStream(y) : (y) => this._onSendMessage(y), A = t && this._stepCompletable && !this._agentResponding, L = e.steps.every((y) => y.status === "Complete"), B = t && s && L, H = this._providerError ? r`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : r`
          <div class="main-panel">
            ${A ? r`
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
            <shallai-chat-panel
              .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
              ?is-streaming=${this._streaming && !this._viewingStepId}
              ?input-enabled=${g}
              input-placeholder=${_}
              @send-message=${R}
            ></shallai-chat-panel>
          </div>`;
    return r`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${e.workflowName || e.workflowAlias} — Run #${this._runNumber}</h2>
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
u = h([
  z("shallai-instance-detail")
], u);
const ve = u;
export {
  u as ShallaiInstanceDetailElement,
  ve as default
};
//# sourceMappingURL=shallai-instance-detail.element-CpA4Ijrf.js.map
