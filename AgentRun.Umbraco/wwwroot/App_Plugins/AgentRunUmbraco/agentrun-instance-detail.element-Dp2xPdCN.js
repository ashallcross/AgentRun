import { UmbLitElement as M } from "@umbraco-cms/backoffice/lit-element";
import { css as I, property as h, state as c, customElement as z, html as o, nothing as p } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as V } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as U } from "@umbraco-cms/backoffice/modal";
import { s as W, b as j, a as q, d as P, m as T, e as K, r as J, f as G, h as E, i as X } from "./index-YBxbiT8W.js";
import { n as Y } from "./instance-list-helpers-JWQgi_HM.js";
var Q = Object.defineProperty, Z = Object.getOwnPropertyDescriptor, S = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? Z(t, s) : t, r = e.length - 1, n; r >= 0; r--)
    (n = e[r]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && Q(t, s, a), a;
};
let _ = class extends M {
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
    return this.status === "running" ? o`<uui-loader></uui-loader>` : this.status === "error" ? o`<uui-icon class="status-icon error" name="icon-alert"></uui-icon>` : o`<uui-icon class="status-icon complete" name="icon-check"></uui-icon>`;
  }
  _renderBody() {
    return this._expanded ? o`
      <div class="tool-call-body">
        ${this.arguments ? o`
              <div class="section-label">Arguments</div>
              <div class="mono-block">${JSON.stringify(this.arguments, null, 2)}</div>
            ` : p}
        ${this.result != null ? o`
              <div class="section-label">Result</div>
              <div class="mono-block">${this.result}</div>
            ` : p}
      </div>
    ` : p;
  }
  render() {
    const e = this.status !== "running";
    return o`
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
          ${e ? o`<uui-icon class="chevron ${this._expanded ? "open" : ""}" name="icon-navigation-down"></uui-icon>` : p}
        </div>
        ${this._renderBody()}
      </div>
    `;
  }
};
_.styles = I`
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
S([
  h({ type: String })
], _.prototype, "toolName", 2);
S([
  h({ type: String })
], _.prototype, "toolCallId", 2);
S([
  h({ type: String })
], _.prototype, "summary", 2);
S([
  h({ attribute: !1 })
], _.prototype, "arguments", 2);
S([
  h({ type: String })
], _.prototype, "result", 2);
S([
  h({ type: String })
], _.prototype, "status", 2);
S([
  c()
], _.prototype, "_expanded", 2);
_ = S([
  z("agentrun-tool-call")
], _);
var ee = Object.defineProperty, te = Object.getOwnPropertyDescriptor, C = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? te(t, s) : t, r = e.length - 1, n; r >= 0; r--)
    (n = e[r]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && ee(t, s, a), a;
};
let w = class extends M {
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
    const t = W(e), s = document.createElement("div");
    return s.className = "agent-content", s.innerHTML = t, o`${this._templateFromNode(s)}`;
  }
  _templateFromNode(e) {
    const t = document.createElement("div");
    return t.className = "agent-content", t.innerHTML = e.innerHTML, o`<div class="agent-content" .innerHTML=${e.innerHTML}></div>`;
  }
  render() {
    if (this.role === "system")
      return o`
        <div
          class="system-message"
          role="listitem"
          aria-label="System notification"
        >
          ${this.content}
        </div>
      `;
    if (this.role === "user")
      return o`
        <div class="user-message" role="listitem" aria-label="Your message">
          <span class="user-content">${this.content}</span>
          ${this.timestamp ? o`<span class="user-timestamp">${this._formatTime(this.timestamp)}</span>` : p}
        </div>
      `;
    const e = this.isStreaming ? o`<span class="agent-content" style="white-space:pre-wrap">${this.content}<span class="cursor">▋</span></span>` : this._renderMarkdown(this.content);
    return o`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        ${e}
        ${this.toolCalls.map((t) => o`
          <agentrun-tool-call
            .toolName=${t.toolName}
            .toolCallId=${t.toolCallId}
            .summary=${t.summary}
            .arguments=${t.arguments}
            .result=${t.result}
            .status=${t.status}
          ></agentrun-tool-call>
        `)}
        ${this.timestamp ? o`<span class="agent-timestamp"
              >${this._formatTime(this.timestamp)}</span
            >` : p}
      </div>
    `;
  }
};
w.styles = I`
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
C([
  h({ type: String })
], w.prototype, "role", 2);
C([
  h({ type: String })
], w.prototype, "content", 2);
C([
  h({ type: String })
], w.prototype, "timestamp", 2);
C([
  h({ type: Boolean, attribute: "is-streaming" })
], w.prototype, "isStreaming", 2);
C([
  h({ attribute: !1 })
], w.prototype, "toolCalls", 2);
w = C([
  z("agentrun-chat-message")
], w);
var se = Object.defineProperty, ae = Object.getOwnPropertyDescriptor, y = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? ae(t, s) : t, r = e.length - 1, n; r >= 0; r--)
    (n = e[r]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && se(t, s, a), a;
};
let m = class extends M {
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
      return o`<div class="empty-state">Start this workflow to begin.</div>`;
    const e = this.messages.length - 1;
    return o`
      <uui-scroll-container @scroll=${this._onScroll}>
        <div class="message-list" role="log" aria-live="polite">
          ${this.messages.map(
      (t, s) => o`
              <agentrun-chat-message
                .role=${t.role}
                .content=${t.content}
                .timestamp=${t.timestamp}
                .toolCalls=${t.toolCalls ?? []}
                ?is-streaming=${s === e && t.role === "agent" && this.isStreaming}
              ></agentrun-chat-message>
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
m.styles = I`
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
y([
  h({ attribute: !1 })
], m.prototype, "messages", 2);
y([
  h({ type: Boolean, attribute: "is-streaming" })
], m.prototype, "isStreaming", 2);
y([
  h({ type: Boolean, attribute: "input-enabled" })
], m.prototype, "inputEnabled", 2);
y([
  h({ type: String, attribute: "input-placeholder" })
], m.prototype, "inputPlaceholder", 2);
y([
  c()
], m.prototype, "_inputValue", 2);
y([
  c()
], m.prototype, "_autoScrollPaused", 2);
y([
  c()
], m.prototype, "_hasNewMessages", 2);
y([
  c()
], m.prototype, "_wasStreaming", 2);
m = y([
  z("agentrun-chat-panel")
], m);
function ie(e) {
  const t = e.split("/");
  return decodeURIComponent(t[t.length - 1]);
}
function ne(e, t) {
  const s = e.split("/");
  return s.length >= 2 && s.splice(-2, 2, "workflows", encodeURIComponent(t)), s.join("/");
}
function A(e) {
  switch (e.status) {
    case "Pending":
      return "Pending";
    case "Active":
      return "In progress";
    case "Complete":
      return e.writesTo?.[0] ?? "Complete";
    case "Error":
      return "Error";
    case "Cancelled":
      return "Cancelled";
    default:
      return e.status;
  }
}
function re(e) {
  switch (e) {
    case "Pending":
      return "icon-circle-dotted";
    case "Active":
      return "icon-sync";
    case "Complete":
      return "icon-check";
    case "Error":
      return "icon-wrong";
    case "Cancelled":
      return "icon-block";
    default:
      return "icon-circle-dotted";
  }
}
function oe(e, t) {
  return e === "Active" && t;
}
function le(e) {
  return e.instanceStatus === "Running" && !e.hasActiveStep && !e.isStreaming && e.hasPendingSteps;
}
function ce(e) {
  if (e.instanceStatus === "Interrupted")
    return { inputEnabled: !1, inputPlaceholder: "Run interrupted — click Retry to resume." };
  if (e.isInteractive)
    return e.isTerminal ? { inputEnabled: !1, inputPlaceholder: e.instanceStatus === "Cancelled" ? "Run cancelled." : e.instanceStatus === "Failed" ? "Run failed — click Retry to resume." : "Workflow complete." } : e.isViewingStepHistory ? { inputEnabled: !1, inputPlaceholder: "Viewing step history" } : e.agentResponding ? { inputEnabled: !1, inputPlaceholder: "Agent is responding..." } : e.isBetweenSteps ? { inputEnabled: !1, inputPlaceholder: "Click Continue to run the next step." } : e.isStreaming || e.hasActiveStep ? { inputEnabled: !0, inputPlaceholder: "Message the agent..." } : { inputEnabled: !1, inputPlaceholder: "Send a message to start." };
  const t = e.isStreaming && !e.isViewingStepHistory;
  let s;
  return e.isTerminal ? s = e.instanceStatus === "Cancelled" ? "Run cancelled." : e.instanceStatus === "Failed" ? "Run failed — click Retry to resume." : "Workflow complete." : e.hasActiveStep ? s = "Step complete" : s = "Click 'Start' to begin the workflow.", { inputEnabled: t, inputPlaceholder: s };
}
var ue = Object.defineProperty, pe = Object.getOwnPropertyDescriptor, R = (e) => {
  throw TypeError(e);
}, d = (e, t, s, i) => {
  for (var a = i > 1 ? void 0 : i ? pe(t, s) : t, r = e.length - 1, n; r >= 0; r--)
    (n = e[r]) && (a = (i ? n(t, s, a) : n(a)) || a);
  return i && a && ue(t, s, a), a;
}, O = (e, t, s) => t.has(e) || R("Cannot " + s), k = (e, t, s) => (O(e, t, "read from private field"), s ? s.call(e) : t.get(e)), de = (e, t, s) => t.has(e) ? R("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, s), he = (e, t, s, i) => (O(e, t, "write to private field"), t.set(e, s), s), f;
let u = class extends M {
  constructor() {
    super(), de(this, f), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this._streaming = !1, this._providerError = !1, this._chatMessages = [], this._streamingText = "", this._viewingStepId = null, this._historyMessages = [], this._stepCompletable = !1, this._agentResponding = !1, this._retrying = !1, this._popoverOpen = !1, this._popoverArtifactPath = null, this._toolBatchOpen = !1, this.consumeContext(V, (e) => {
      he(this, f, e);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const e = ie(window.location.pathname), t = await k(this, f)?.getLatestToken(), s = await j(e, t);
      this._instance = s;
      const i = await q(s.workflowAlias, t), r = Y(i).find((n) => n.id === s.id);
      if (this._runNumber = r?.runNumber ?? 0, !this._streaming && this._chatMessages.length === 0) {
        const n = s.steps.find(
          (l) => l.status === "Active" || l.status === "Error"
        ) ?? s.steps.findLast((l) => l.status === "Complete");
        if (n)
          try {
            const l = await P(s.id, n.id, t);
            this._chatMessages = T(l);
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
    const e = ne(
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
        const t = await k(this, f)?.getLatestToken(), s = this._instance.id, i = await P(s, e.id, t);
        this._historyMessages = T(i);
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
    const e = await k(this, f)?.getLatestToken(), t = await K(this._instance.id, e);
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
    if (this._retrying || this._streaming || !this._instance || this._instance.status !== "Failed" && this._instance.status !== "Interrupted")
      return;
    this._retrying = !0, this._chatMessages = [];
    const e = await k(this, f)?.getLatestToken(), t = await J(this._instance.id, e);
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
        const { done: a, value: r } = await t.read();
        if (a) break;
        i += s.decode(r, { stream: !0 });
        const n = i.split(`

`);
        i = n.pop();
        for (const l of n) {
          const v = l.split(`
`), g = v.find(($) => $.startsWith("event:"))?.slice(7), x = v.find(($) => $.startsWith("data:"))?.slice(5);
          g && x && this._handleSseEvent(g, JSON.parse(x));
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
        }, r = this._chatMessages[this._chatMessages.length - 1];
        if (this._toolBatchOpen && r && r.role === "agent") {
          const n = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, n),
            { ...r, toolCalls: [...r.toolCalls ?? [], a] },
            ...this._chatMessages.slice(n + 1)
          ];
        } else if (r && r.role === "agent" && !r.toolCalls?.length) {
          const n = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, n),
            { ...r, toolCalls: [a] },
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
          (l) => l.role === "agent" && l.toolCalls?.some((v) => v.toolCallId === s)
        );
        if (a === -1) break;
        const r = this._chatMessages[a], n = r.toolCalls.map(
          (l) => l.toolCallId === s ? { ...l, arguments: i, summary: G(l.toolName, i) } : l
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, a),
          { ...r, toolCalls: n },
          ...this._chatMessages.slice(a + 1)
        ];
        break;
      }
      case "tool.end": {
        const s = t.toolCallId, i = this._chatMessages.findLastIndex(
          (n) => n.role === "agent" && n.toolCalls?.some((l) => l.toolCallId === s)
        );
        if (i === -1) break;
        const a = this._chatMessages[i], r = a.toolCalls.map(
          (n) => n.toolCallId === s && n.status === "running" ? { ...n, status: "complete" } : n
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, i),
          { ...a, toolCalls: r },
          ...this._chatMessages.slice(i + 1)
        ];
        break;
      }
      case "tool.result": {
        const s = t.toolCallId, i = t.result, a = typeof i == "string" ? i : JSON.stringify(i), r = typeof a == "string" && a.startsWith("Tool '") && (a.includes("error") || a.includes("failed")), n = this._chatMessages.findLastIndex(
          (g) => g.role === "agent" && g.toolCalls?.some((x) => x.toolCallId === s)
        );
        if (n === -1) break;
        const l = this._chatMessages[n], v = l.toolCalls.map(
          (g) => g.toolCallId === s ? { ...g, result: a, status: r ? "error" : "complete" } : g
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, n),
          { ...l, toolCalls: v },
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
          const a = this._instance.steps.find((r) => r.id === t.stepId);
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
      const s = await k(this, f)?.getLatestToken();
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
      const s = await k(this, f)?.getLatestToken();
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
        const e = await k(this, f)?.getLatestToken();
        try {
          await X(this._instance.id, e);
        } catch (t) {
          (t instanceof Error ? t.message : String(t)).includes("409") || console.warn("Failed to cancel instance");
        }
        await this._loadData();
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
      (e, t) => o`
              <li
                role="listitem"
                class="step-item
                  ${e.status === "Pending" ? "pending" : ""}
                  ${e.status === "Active" ? "active" : ""}
                  ${e.status === "Complete" ? "complete" : ""}
                  ${e.status === "Error" ? "error" : ""}
                  ${e.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === e.id ? "selected" : ""}"
                aria-label="Step ${t + 1}: ${e.name} — ${A(e)}"
                @click=${e.status !== "Pending" ? () => this._onStepClick(e) : p}
                aria-current=${e.status === "Active" ? "step" : p}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${oe(e.status, this._agentResponding) ? "step-icon-spin" : ""}"
                    name=${re(e.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${e.name}</span>
                  <span class="step-subtitle">${A(e)}</span>
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
  _onArtifactSelected(e) {
    this._popoverArtifactPath = e.detail.path, this._popoverOpen = !0;
  }
  _onPopoverClosed() {
    this._popoverOpen = !1;
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
    const e = this._instance, t = e.workflowMode !== "autonomous", s = e.status === "Completed" || e.status === "Failed" || e.status === "Cancelled", a = !!e.steps.find((b) => b.status === "Active"), r = e.status === "Pending" && !this._streaming, n = "Start", l = (e.status === "Failed" || e.status === "Interrupted") && !this._streaming, v = t && this._stepCompletable && !this._agentResponding, g = le({
      instanceStatus: e.status,
      hasActiveStep: a,
      isStreaming: this._streaming,
      hasPendingSteps: e.steps.some((b) => b.status === "Pending")
    }), x = g && !v, $ = e.status === "Running" || e.status === "Pending", { inputEnabled: N, inputPlaceholder: D } = ce({
      instanceStatus: e.status,
      isInteractive: t,
      isTerminal: s,
      hasActiveStep: a,
      isStreaming: this._streaming,
      isViewingStepHistory: this._viewingStepId !== null,
      agentResponding: this._agentResponding,
      isBetweenSteps: g
    }), B = t && !this._streaming ? (b) => this._onSendAndStream(b) : (b) => this._onSendMessage(b), L = e.steps.every((b) => b.status === "Complete"), H = t && s && L, F = this._providerError ? o`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : o`
          <div class="main-panel">
            ${v ? o`
                  <div class="completion-banner">
                    <span>Step complete — review the output, then advance when ready</span>
                    <uui-button label="Continue to next step" look="primary" @click=${this._onAdvanceStep}>
                      Continue to next step
                    </uui-button>
                  </div>
                ` : p}
            ${H ? o`
                  <div class="completion-banner">
                    <span>Workflow complete — all steps finished</span>
                  </div>
                ` : p}
            <agentrun-chat-panel
              .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
              ?is-streaming=${this._streaming && !this._viewingStepId}
              ?input-enabled=${N}
              input-placeholder=${D}
              @send-message=${B}
            ></agentrun-chat-panel>
          </div>`;
    return o`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${e.workflowName || e.workflowAlias} — Run #${this._runNumber}</h2>
        ${r ? o`
              <uui-button label=${n} look="primary" @click=${this._onStartClick}>
                ${n}
              </uui-button>
            ` : p}
        ${l ? o`
              <uui-button label="Retry" look="primary" ?disabled=${this._retrying} @click=${this._onRetryClick}>
                ${this._retrying ? o`<uui-loader></uui-loader>` : "Retry"}
              </uui-button>
            ` : p}
        ${x ? o`
              <uui-button label="Continue" look="primary" @click=${this._onStartClick}>
                Continue
              </uui-button>
            ` : p}
        ${$ ? o`
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
        ${F}
      </div>
      <agentrun-artifact-popover
        .instanceId=${e.id}
        .artifactPath=${this._popoverArtifactPath ?? ""}
        ?open=${this._popoverOpen}
        @popover-closed=${this._onPopoverClosed}
      ></agentrun-artifact-popover>
    `;
  }
};
f = /* @__PURE__ */ new WeakMap();
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
d([
  c()
], u.prototype, "_instance", 2);
d([
  c()
], u.prototype, "_loading", 2);
d([
  c()
], u.prototype, "_error", 2);
d([
  c()
], u.prototype, "_selectedStepId", 2);
d([
  c()
], u.prototype, "_cancelling", 2);
d([
  c()
], u.prototype, "_runNumber", 2);
d([
  c()
], u.prototype, "_streaming", 2);
d([
  c()
], u.prototype, "_providerError", 2);
d([
  c()
], u.prototype, "_chatMessages", 2);
d([
  c()
], u.prototype, "_streamingText", 2);
d([
  c()
], u.prototype, "_viewingStepId", 2);
d([
  c()
], u.prototype, "_historyMessages", 2);
d([
  c()
], u.prototype, "_stepCompletable", 2);
d([
  c()
], u.prototype, "_agentResponding", 2);
d([
  c()
], u.prototype, "_retrying", 2);
d([
  c()
], u.prototype, "_popoverOpen", 2);
d([
  c()
], u.prototype, "_popoverArtifactPath", 2);
u = d([
  z("agentrun-instance-detail")
], u);
const ye = u;
export {
  u as AgentRunInstanceDetailElement,
  ye as default
};
//# sourceMappingURL=agentrun-instance-detail.element-Dp2xPdCN.js.map
