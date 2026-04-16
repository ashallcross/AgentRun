import { UmbLitElement as M } from "@umbraco-cms/backoffice/lit-element";
import { css as I, property as d, state as b, customElement as z, html as c, nothing as p } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as W } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as j } from "@umbraco-cms/backoffice/modal";
import { s as U, e as K, b as q, r as J, d as G, f as X, a as Y, h as T, m as E, i as Q } from "./index-QeZdEPr6.js";
import { n as Z } from "./instance-list-helpers-JWQgi_HM.js";
function ee(e) {
  const t = e.split("/");
  return decodeURIComponent(t[t.length - 1]);
}
function te(e, t) {
  const s = e.split("/");
  return s.length >= 2 && s.splice(-2, 2, "workflows", encodeURIComponent(t)), s.join("/");
}
function R(e) {
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
function se(e) {
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
function ae(e, t) {
  return e === "Active" && t;
}
function ne(e) {
  return e.instanceStatus === "Running" && !e.hasActiveStep && !e.isStreaming && e.hasPendingSteps;
}
function ie(e) {
  if (e.instanceStatus === "Interrupted")
    return { inputEnabled: !1, inputPlaceholder: "Run interrupted — click Retry to resume." };
  if (e.isInteractive)
    return e.isTerminal ? { inputEnabled: !1, inputPlaceholder: e.instanceStatus === "Cancelled" ? "Run cancelled." : e.instanceStatus === "Failed" ? "Run failed — click Retry to resume." : "Workflow complete." } : e.isViewingStepHistory ? { inputEnabled: !1, inputPlaceholder: "Viewing step history" } : e.agentResponding ? { inputEnabled: !1, inputPlaceholder: "Agent is responding..." } : e.isBetweenSteps ? { inputEnabled: !1, inputPlaceholder: "Click Continue to run the next step." } : e.isStreaming || e.hasActiveStep ? { inputEnabled: !0, inputPlaceholder: "Message the agent..." } : { inputEnabled: !1, inputPlaceholder: "Send a message to start." };
  const t = e.isStreaming && !e.isViewingStepHistory;
  let s;
  return e.isTerminal ? s = e.instanceStatus === "Cancelled" ? "Run cancelled." : e.instanceStatus === "Failed" ? "Run failed — click Retry to resume." : "Workflow complete." : e.hasActiveStep ? s = "Step complete" : s = "Click 'Start' to begin the workflow.", { inputEnabled: t, inputPlaceholder: s };
}
function re(e, t, s, a) {
  return e === t && s.role === "agent" && a && s.isStreaming === !0;
}
var oe = Object.defineProperty, le = Object.getOwnPropertyDescriptor, w = (e, t, s, a) => {
  for (var n = a > 1 ? void 0 : a ? le(t, s) : t, r = e.length - 1, i; r >= 0; r--)
    (i = e[r]) && (n = (a ? i(t, s, n) : i(n)) || n);
  return a && n && oe(t, s, n), n;
};
let m = class extends M {
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
    return this.status === "running" ? c`<uui-loader></uui-loader>` : this.status === "error" ? c`<uui-icon class="status-icon error" name="icon-alert"></uui-icon>` : c`<uui-icon class="status-icon complete" name="icon-check"></uui-icon>`;
  }
  _renderBody() {
    return this._expanded ? c`
      <div class="tool-call-body">
        ${this.arguments ? c`
              <div class="section-label">Arguments</div>
              <div class="mono-block">${JSON.stringify(this.arguments, null, 2)}</div>
            ` : p}
        ${this.result != null ? c`
              <div class="section-label">Result</div>
              <div class="mono-block">${this.result}</div>
            ` : p}
      </div>
    ` : p;
  }
  render() {
    const e = this.status !== "running";
    return c`
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
          ${e ? c`<uui-icon class="chevron ${this._expanded ? "open" : ""}" name="icon-navigation-down"></uui-icon>` : p}
        </div>
        ${this._renderBody()}
      </div>
    `;
  }
};
m.styles = I`
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
w([
  d({ type: String })
], m.prototype, "toolName", 2);
w([
  d({ type: String })
], m.prototype, "toolCallId", 2);
w([
  d({ type: String })
], m.prototype, "summary", 2);
w([
  d({ attribute: !1 })
], m.prototype, "arguments", 2);
w([
  d({ type: String })
], m.prototype, "result", 2);
w([
  d({ type: String })
], m.prototype, "status", 2);
w([
  b()
], m.prototype, "_expanded", 2);
m = w([
  z("agentrun-tool-call")
], m);
var ce = Object.defineProperty, ue = Object.getOwnPropertyDescriptor, $ = (e, t, s, a) => {
  for (var n = a > 1 ? void 0 : a ? ue(t, s) : t, r = e.length - 1, i; r >= 0; r--)
    (i = e[r]) && (n = (a ? i(t, s, n) : i(n)) || n);
  return a && n && ce(t, s, n), n;
};
let _ = class extends M {
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
    const t = U(e), s = document.createElement("div");
    return s.className = "agent-content", s.innerHTML = t, c`${this._templateFromNode(s)}`;
  }
  _templateFromNode(e) {
    const t = document.createElement("div");
    return t.className = "agent-content", t.innerHTML = e.innerHTML, c`<div class="agent-content" .innerHTML=${e.innerHTML}></div>`;
  }
  render() {
    if (this.role === "system")
      return c`
        <div
          class="system-message"
          role="listitem"
          aria-label="System notification"
        >
          ${this.content}
        </div>
      `;
    if (this.role === "user")
      return c`
        <div class="user-message" role="listitem" aria-label="Your message">
          <span class="user-content">${this.content}</span>
          ${this.timestamp ? c`<span class="user-timestamp">${this._formatTime(this.timestamp)}</span>` : p}
        </div>
      `;
    const e = this.isStreaming ? c`<span class="agent-content" style="white-space:pre-wrap">${this.content}<span class="cursor">▋</span></span>` : this._renderMarkdown(this.content);
    return c`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        ${e}
        ${this.toolCalls.map((t) => c`
          <agentrun-tool-call
            .toolName=${t.toolName}
            .toolCallId=${t.toolCallId}
            .summary=${t.summary}
            .arguments=${t.arguments}
            .result=${t.result}
            .status=${t.status}
          ></agentrun-tool-call>
        `)}
        ${this.timestamp ? c`<span class="agent-timestamp"
              >${this._formatTime(this.timestamp)}</span
            >` : p}
      </div>
    `;
  }
};
_.styles = I`
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
], _.prototype, "role", 2);
$([
  d({ type: String })
], _.prototype, "content", 2);
$([
  d({ type: String })
], _.prototype, "timestamp", 2);
$([
  d({ type: Boolean, attribute: "is-streaming" })
], _.prototype, "isStreaming", 2);
$([
  d({ attribute: !1 })
], _.prototype, "toolCalls", 2);
_ = $([
  z("agentrun-chat-message")
], _);
var pe = Object.defineProperty, de = Object.getOwnPropertyDescriptor, y = (e, t, s, a) => {
  for (var n = a > 1 ? void 0 : a ? de(t, s) : t, r = e.length - 1, i; r >= 0; r--)
    (i = e[r]) && (n = (a ? i(t, s, n) : i(n)) || n);
  return a && n && pe(t, s, n), n;
};
let g = class extends M {
  constructor() {
    super(...arguments), this.messages = [], this.isStreaming = !1, this.inputEnabled = !1, this.inputPlaceholder = "Send a message to start.", this._inputValue = "", this._autoScrollPaused = !1, this._hasNewMessages = !1, this._wasStreaming = !1;
  }
  _onScroll(e) {
    const t = e.target;
    if (!t) return;
    const { scrollTop: s, scrollHeight: a, clientHeight: n } = t;
    a - s - n < 50 ? (this._autoScrollPaused = !1, this._hasNewMessages = !1) : this._autoScrollPaused = !0;
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
      return c`<div class="empty-state">Start this workflow to begin.</div>`;
    const e = this.messages.length - 1;
    return c`
      <uui-scroll-container @scroll=${this._onScroll}>
        <div class="message-list" role="log" aria-live="polite">
          ${this.messages.map(
      (t, s) => c`
              <agentrun-chat-message
                .role=${t.role}
                .content=${t.content}
                .timestamp=${t.timestamp}
                .toolCalls=${t.toolCalls ?? []}
                ?is-streaming=${re(s, e, t, this.isStreaming)}
              ></agentrun-chat-message>
            `
    )}
        </div>
      </uui-scroll-container>

      ${this._autoScrollPaused && this._hasNewMessages ? c`
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
g.styles = I`
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
  d({ attribute: !1 })
], g.prototype, "messages", 2);
y([
  d({ type: Boolean, attribute: "is-streaming" })
], g.prototype, "isStreaming", 2);
y([
  d({ type: Boolean, attribute: "input-enabled" })
], g.prototype, "inputEnabled", 2);
y([
  d({ type: String, attribute: "input-placeholder" })
], g.prototype, "inputPlaceholder", 2);
y([
  b()
], g.prototype, "_inputValue", 2);
y([
  b()
], g.prototype, "_autoScrollPaused", 2);
y([
  b()
], g.prototype, "_hasNewMessages", 2);
y([
  b()
], g.prototype, "_wasStreaming", 2);
g = y([
  z("agentrun-chat-panel")
], g);
function he() {
  return {
    instance: null,
    loading: !0,
    error: !1,
    selectedStepId: null,
    cancelling: !1,
    runNumber: 0,
    streaming: !1,
    providerError: !1,
    chatMessages: [],
    streamingText: "",
    viewingStepId: null,
    historyMessages: [],
    stepCompletable: !1,
    agentResponding: !1,
    retrying: !1,
    toolBatchOpen: !1
  };
}
function ge(e, t, s = {}) {
  const a = s.now ?? (() => (/* @__PURE__ */ new Date()).toISOString()), { event: n, data: r } = t;
  let i = e;
  switch ((n === "text.delta" || n === "tool.start") && (i = i.agentResponding ? i : { ...i, agentResponding: !0 }), (n === "text.delta" || n === "tool.start") && i.instance && (i.instance.status === "Failed" || i.instance.status === "Interrupted") && (i = { ...i, instance: { ...i.instance, status: "Running" } }), n) {
    case "text.delta":
      return me(i, r, a);
    case "tool.start":
      return fe(i, r, a);
    case "tool.args":
      return ve(i, r);
    case "tool.end":
      return ye(i, r);
    case "tool.result":
      return _e(i, r);
    case "step.started":
      return be(i, r, a);
    case "step.finished":
      return we(i, r, a);
    case "run.finished":
      return Se(i, r, a);
    case "input.wait":
      return Ce(i);
    case "run.error":
      return xe(i, r, a);
    case "user.message":
      return i;
    default:
      return i;
  }
}
function x(e, t = {}) {
  if (!e.streamingText) return e;
  const s = t.now ?? (() => (/* @__PURE__ */ new Date()).toISOString()), a = e.chatMessages.length - 1, n = e.chatMessages[a];
  if (!n || n.role !== "agent" || !n.isStreaming)
    return {
      ...e,
      chatMessages: [
        ...e.chatMessages,
        {
          role: "agent",
          content: e.streamingText,
          timestamp: s(),
          isStreaming: !1
        }
      ],
      streamingText: ""
    };
  const r = [
    ...e.chatMessages.slice(0, a),
    { ...n, content: e.streamingText, isStreaming: !1 }
  ];
  return { ...e, chatMessages: r, streamingText: "" };
}
function me(e, t, s) {
  if (typeof t.content != "string" || t.content === "") return e;
  const a = t.content, n = e.streamingText + a, r = e.chatMessages.length - 1, i = e.chatMessages[r];
  let o;
  return i && i.role === "agent" && i.isStreaming ? o = [
    ...e.chatMessages.slice(0, r),
    { ...i, content: n }
  ] : o = [
    ...e.chatMessages,
    {
      role: "agent",
      content: n,
      timestamp: s(),
      isStreaming: !0
    }
  ], { ...e, toolBatchOpen: !1, streamingText: n, chatMessages: o };
}
function fe(e, t, s) {
  if (typeof t.toolCallId != "string" || typeof t.toolName != "string")
    return e;
  const a = x(e, { now: s }), n = t.toolCallId, r = t.toolName, i = {
    toolCallId: n,
    toolName: r,
    summary: r,
    arguments: null,
    result: null,
    status: "running"
  }, o = a.chatMessages.length - 1, l = a.chatMessages[o];
  let u;
  return a.toolBatchOpen && l && l.role === "agent" ? u = [
    ...a.chatMessages.slice(0, o),
    { ...l, toolCalls: [...l.toolCalls ?? [], i] },
    ...a.chatMessages.slice(o + 1)
  ] : l && l.role === "agent" && !l.toolCalls?.length ? u = [
    ...a.chatMessages.slice(0, o),
    { ...l, toolCalls: [i] },
    ...a.chatMessages.slice(o + 1)
  ] : u = [
    ...a.chatMessages,
    {
      role: "agent",
      content: "",
      timestamp: s(),
      toolCalls: [i]
    }
  ], { ...a, chatMessages: u, toolBatchOpen: !0 };
}
function ve(e, t) {
  const s = t.toolCallId, a = t.arguments, n = e.chatMessages.findLastIndex(
    (l) => l.role === "agent" && l.toolCalls?.some((u) => u.toolCallId === s)
  );
  if (n === -1) return e;
  const r = e.chatMessages[n], i = r.toolCalls.map(
    (l) => l.toolCallId === s ? { ...l, arguments: a, summary: K(l.toolName, a) } : l
  ), o = [
    ...e.chatMessages.slice(0, n),
    { ...r, toolCalls: i },
    ...e.chatMessages.slice(n + 1)
  ];
  return { ...e, chatMessages: o };
}
function ye(e, t) {
  const s = t.toolCallId, a = e.chatMessages.findLastIndex(
    (o) => o.role === "agent" && o.toolCalls?.some((l) => l.toolCallId === s)
  );
  if (a === -1) return e;
  const n = e.chatMessages[a], r = n.toolCalls.map(
    (o) => o.toolCallId === s && o.status === "running" ? { ...o, status: "complete" } : o
  ), i = [
    ...e.chatMessages.slice(0, a),
    { ...n, toolCalls: r },
    ...e.chatMessages.slice(a + 1)
  ];
  return { ...e, chatMessages: i };
}
function _e(e, t) {
  if (typeof t.toolCallId != "string") return e;
  const s = t.toolCallId, a = t.result, n = a == null ? "" : typeof a == "string" ? a : JSON.stringify(a), r = n.startsWith("Tool '") && (n.includes("error") || n.includes("failed")), i = e.chatMessages.findLastIndex(
    (h) => h.role === "agent" && h.toolCalls?.some((S) => S.toolCallId === s)
  );
  if (i === -1) return e;
  const o = e.chatMessages[i], l = o.toolCalls.map(
    (h) => h.toolCallId === s ? {
      ...h,
      result: n,
      status: r ? "error" : "complete"
    } : h
  ), u = [
    ...e.chatMessages.slice(0, i),
    { ...o, toolCalls: l },
    ...e.chatMessages.slice(i + 1)
  ];
  return { ...e, chatMessages: u };
}
function be(e, t, s) {
  const a = x(e, { now: s }), n = t.stepId, r = t.stepName || n, i = A(a.instance, n, "Active"), o = [
    ...a.chatMessages,
    {
      role: "system",
      content: `Starting ${r}...`,
      timestamp: s()
    }
  ];
  return { ...a, instance: i, toolBatchOpen: !1, chatMessages: o };
}
function we(e, t, s) {
  const a = x(e, { now: s }), n = t.stepId, r = typeof t.status == "string" ? t.status : "Complete", i = t.stepName || n, o = r === "Error" ? "failed" : "completed", l = A(a.instance, n, r), u = [
    ...a.chatMessages,
    {
      role: "system",
      content: `${i} ${o}`,
      timestamp: s()
    }
  ];
  return { ...a, instance: l, chatMessages: u };
}
function Se(e, t, s) {
  const a = x(e, { now: s });
  if (!a.instance) return a;
  const n = a.instance.status, r = typeof t.status == "string" ? t.status : void 0, i = n === "Failed" || n === "Cancelled" ? n : r ?? "Completed", o = { ...a.instance, status: i };
  if (i !== "Completed")
    return { ...a, instance: o };
  const l = [
    ...a.chatMessages,
    {
      role: "system",
      content: "Workflow complete.",
      timestamp: s()
    }
  ];
  return { ...a, instance: o, chatMessages: l };
}
function Ce(e) {
  return { ...x(e), agentResponding: !1 };
}
function xe(e, t, s) {
  const a = x(e, { now: s }), n = t.message || "An error occurred.", r = a.instance ? { ...a.instance, status: "Failed" } : a.instance, i = [
    ...a.chatMessages,
    {
      role: "system",
      content: n,
      timestamp: s()
    }
  ];
  return { ...a, instance: r, chatMessages: i };
}
function A(e, t, s) {
  if (!e) return e;
  const a = e.steps.map(
    (n) => n.id === t ? { ...n, status: s } : n
  );
  return { ...e, steps: a };
}
async function ke(e, t) {
  let s;
  try {
    s = await q(e, t);
  } catch {
    return { kind: "failed", status: 0 };
  }
  return s.ok ? { kind: "streaming", response: s } : s.status === 400 ? { kind: "providerError" } : { kind: "failed", status: s.status };
}
async function $e(e, t) {
  let s;
  try {
    s = await J(e, t);
  } catch {
    return { kind: "failed", status: 0 };
  }
  return s.ok ? { kind: "streaming", response: s } : s.status === 409 ? { kind: "notRetryable" } : { kind: "failed", status: s.status };
}
async function Me(e, t) {
  try {
    return await G(e, t), { kind: "ok" };
  } catch (s) {
    return (s instanceof Error ? s.message : String(s)).includes("409") ? { kind: "conflict" } : { kind: "failed", error: s };
  }
}
var Ie = Object.defineProperty, ze = Object.getOwnPropertyDescriptor, N = (e) => {
  throw TypeError(e);
}, P = (e, t, s, a) => {
  for (var n = a > 1 ? void 0 : a ? ze(t, s) : t, r = e.length - 1, i; r >= 0; r--)
    (i = e[r]) && (n = (a ? i(t, s, n) : i(n)) || n);
  return a && n && Ie(t, s, n), n;
}, O = (e, t, s) => t.has(e) || N("Cannot " + s), k = (e, t, s) => (O(e, t, "read from private field"), s ? s.call(e) : t.get(e)), Pe = (e, t, s) => t.has(e) ? N("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, s), Te = (e, t, s, a) => (O(e, t, "write to private field"), t.set(e, s), s), v;
let C = class extends M {
  constructor() {
    super(), Pe(this, v), this._state = he(), this._popoverOpen = !1, this._popoverArtifactPath = null, this.consumeContext(W, (e) => {
      Te(this, v, e);
    });
  }
  _patch(e) {
    this._state = { ...this._state, ...e };
  }
  _appendMessage(e) {
    this._patch({ chatMessages: [...this._state.chatMessages, e] });
  }
  _now() {
    return (/* @__PURE__ */ new Date()).toISOString();
  }
  connectedCallback() {
    super.connectedCallback(), this._patch({ loading: !0, error: !1 }), this._loadData();
  }
  async _loadData() {
    try {
      const e = ee(window.location.pathname), t = await k(this, v)?.getLatestToken(), s = await X(e, t), a = await Y(s.workflowAlias, t), r = Z(a).find((l) => l.id === s.id)?.runNumber ?? 0;
      let i = null;
      if (!this._state.streaming && this._state.chatMessages.length === 0) {
        const l = s.steps.find((u) => u.status === "Active" || u.status === "Error") ?? s.steps.findLast((u) => u.status === "Complete");
        if (l)
          try {
            const u = await T(s.id, l.id, t);
            i = E(u);
          } catch {
          }
      }
      const o = { instance: s, runNumber: r, error: !1 };
      i !== null && !this._state.streaming && this._state.chatMessages.length === 0 && (o.chatMessages = i), this._patch(o);
    } catch {
      this._patch({ error: !0 });
    } finally {
      this._patch({ loading: !1 });
    }
  }
  _navigateBack() {
    const e = this._state.instance;
    if (!e) return;
    const t = te(window.location.pathname, e.workflowAlias);
    window.history.pushState({}, "", t);
  }
  async _onStepClick(e) {
    if (e.status !== "Pending") {
      if (this._patch({ selectedStepId: e.id }), this.dispatchEvent(
        new CustomEvent("step-selected", {
          detail: { stepId: e.id, status: e.status },
          bubbles: !0,
          composed: !0
        })
      ), e.status === "Active") {
        this._patch({ viewingStepId: null });
        return;
      }
      this._patch({ viewingStepId: e.id });
      try {
        const t = await k(this, v)?.getLatestToken(), s = await T(this._state.instance.id, e.id, t);
        this._patch({ historyMessages: E(s) });
      } catch {
        this._patch({ historyMessages: [] });
      }
    }
  }
  disconnectedCallback() {
    super.disconnectedCallback(), this._patch({ streaming: !1, agentResponding: !1 });
  }
  async _onStartClick() {
    if (this._state.streaming || !this._state.instance) return;
    const e = await k(this, v)?.getLatestToken(), t = await ke(this._state.instance.id, e);
    if (t.kind === "providerError") {
      this._patch({ providerError: !0 });
      return;
    }
    if (t.kind !== "streaming") {
      await this._loadData();
      return;
    }
    await this._streamSseResponse(t.response);
  }
  async _onRetryClick() {
    const e = this._state.instance;
    if (!(this._state.retrying || this._state.streaming || !e) && !(e.status !== "Failed" && e.status !== "Interrupted")) {
      this._patch({ retrying: !0, chatMessages: [] });
      try {
        const t = await k(this, v)?.getLatestToken(), s = await $e(e.id, t);
        if (s.kind !== "streaming") {
          s.kind === "notRetryable" && this._patch({
            chatMessages: [{
              role: "system",
              content: "Cannot retry — instance is no longer in a failed state.",
              timestamp: this._now()
            }]
          }), await this._loadData();
          return;
        }
        await this._streamSseResponse(s.response);
      } finally {
        this._patch({ retrying: !1 });
      }
    }
  }
  async _streamSseResponse(e) {
    this._patch({
      streaming: !0,
      providerError: !1,
      viewingStepId: null,
      streamingText: ""
    });
    try {
      if (!e.body) return;
      const t = e.body.getReader(), s = new TextDecoder();
      let a = "";
      for (; ; ) {
        const { done: n, value: r } = await t.read();
        if (n) break;
        a += s.decode(r, { stream: !0 });
        const i = a.split(`

`);
        a = i.pop();
        for (const o of i) {
          const l = o.split(`
`), u = l.find((S) => S.startsWith("event:"))?.slice(7), h = l.find((S) => S.startsWith("data:"))?.slice(5);
          u && h && this._handleSseEvent(u, JSON.parse(h));
        }
      }
    } catch {
      this._state = x(this._state), this._appendMessage({ role: "system", content: "Connection lost.", timestamp: this._now() });
    } finally {
      this._patch({ streaming: !1, agentResponding: !1 }), await this._loadData(), this._checkStepCompletable();
    }
  }
  _checkStepCompletable() {
    const e = this._state.instance;
    if (!e) return;
    if (e.workflowMode === "autonomous") {
      this._patch({ stepCompletable: !1 });
      return;
    }
    const t = e.status;
    if (t === "Cancelled" || t === "Failed" || t === "Completed") {
      this._patch({ stepCompletable: !1 });
      return;
    }
    const s = e.steps.some((r) => r.status === "Active"), a = e.steps.some((r) => r.status === "Complete"), n = e.steps.some((r) => r.status === "Pending");
    this._patch({ stepCompletable: !s && a && n });
  }
  _handleSseEvent(e, t) {
    if (this._state = ge(this._state, { event: e, data: t }), e === "run.finished" && this._state.instance && this._state.instance.status === "Completed") {
      const s = [...this._state.instance.steps].reverse().find((a) => a.status === "Complete" && a.writesTo && a.writesTo.length > 0);
      s && s.writesTo && (this._popoverArtifactPath = s.writesTo[s.writesTo.length - 1], this._popoverOpen = !0);
    }
  }
  async _submitUserMessage(e) {
    const t = this._state.instance;
    if (!t) return !1;
    this._appendMessage({ role: "user", content: e, timestamp: this._now() });
    try {
      const s = await k(this, v)?.getLatestToken();
      return await Q(t.id, e, s), !0;
    } catch {
      return this._appendMessage({
        role: "system",
        content: "Failed to send message. Try again.",
        timestamp: this._now()
      }), !1;
    }
  }
  async _onSendMessage(e) {
    await this._submitUserMessage(e.detail.message);
  }
  async _onSendAndStream(e) {
    await this._submitUserMessage(e.detail.message) && (this._patch({ stepCompletable: !1 }), await this._onStartClick());
  }
  async _onAdvanceStep() {
    !this._state.instance || this._state.streaming || (this._patch({ stepCompletable: !1, chatMessages: [] }), await this._onStartClick());
  }
  async _onCancelClick() {
    const e = this._state.instance;
    if (!(this._state.cancelling || !e)) {
      try {
        await j(this, {
          headline: "Cancel workflow run",
          content: "Cancel this workflow run?",
          color: "danger",
          confirmLabel: "Cancel Run"
        });
      } catch {
        return;
      }
      this._patch({ cancelling: !0 });
      try {
        const t = await k(this, v)?.getLatestToken();
        (await Me(e.id, t)).kind === "failed" && console.warn("Failed to cancel instance"), await this._loadData();
      } finally {
        this._patch({ cancelling: !1 });
      }
    }
  }
  _renderStepProgress() {
    const e = this._state.instance;
    return e ? c`
      <div class="step-sidebar">
        <ul role="list">
          ${e.steps.map(
      (t, s) => c`
              <li
                role="listitem"
                class="step-item
                  ${t.status === "Pending" ? "pending" : ""}
                  ${t.status === "Active" ? "active" : ""}
                  ${t.status === "Complete" ? "complete" : ""}
                  ${t.status === "Error" ? "error" : ""}
                  ${t.status !== "Pending" ? "clickable" : ""}
                  ${this._state.selectedStepId === t.id ? "selected" : ""}"
                aria-label="Step ${s + 1}: ${t.name} — ${R(t)}"
                @click=${t.status !== "Pending" ? () => this._onStepClick(t) : p}
                aria-current=${t.status === "Active" ? "step" : p}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${ae(t.status, this._state.agentResponding) ? "step-icon-spin" : ""}"
                    name=${se(t.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${t.name}</span>
                  <span class="step-subtitle">${R(t)}</span>
                </div>
              </li>
            `
    )}
        </ul>
        <hr class="sidebar-divider" />
        <agentrun-artifact-list
          .steps=${e.steps}
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
    const e = this._state;
    if (e.loading) return c`<uui-loader></uui-loader>`;
    if (e.error || !e.instance)
      return c`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    const t = e.instance, s = t.workflowMode !== "autonomous", a = t.status === "Completed" || t.status === "Failed" || t.status === "Cancelled", n = t.steps.some((f) => f.status === "Active"), r = t.status === "Pending" && !e.streaming, i = (t.status === "Failed" || t.status === "Interrupted") && !e.streaming, o = s && e.stepCompletable && !e.agentResponding, l = ne({
      instanceStatus: t.status,
      hasActiveStep: n,
      isStreaming: e.streaming,
      hasPendingSteps: t.steps.some((f) => f.status === "Pending")
    }), u = l && !o, h = t.status === "Running" || t.status === "Pending", { inputEnabled: S, inputPlaceholder: D } = ie({
      instanceStatus: t.status,
      isInteractive: s,
      isTerminal: a,
      hasActiveStep: n,
      isStreaming: e.streaming,
      isViewingStepHistory: e.viewingStepId !== null,
      agentResponding: e.agentResponding,
      isBetweenSteps: l
    }), B = s && !e.streaming ? (f) => this._onSendAndStream(f) : (f) => this._onSendMessage(f), L = t.steps.every((f) => f.status === "Complete"), F = s && a && L, H = e.viewingStepId ? e.historyMessages : e.chatMessages, V = e.providerError ? c`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : c`
          <div class="main-panel">
            ${o ? c`
                  <div class="completion-banner">
                    <span>Step complete — review the output, then advance when ready</span>
                    <uui-button label="Continue to next step" look="primary" @click=${this._onAdvanceStep}>
                      Continue to next step
                    </uui-button>
                  </div>
                ` : p}
            ${F ? c`
                  <div class="completion-banner">
                    <span>Workflow complete — all steps finished</span>
                  </div>
                ` : p}
            <agentrun-chat-panel
              .messages=${H}
              ?is-streaming=${e.streaming && !e.viewingStepId}
              ?input-enabled=${S}
              input-placeholder=${D}
              @send-message=${B}
            ></agentrun-chat-panel>
          </div>`;
    return c`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${t.workflowName || t.workflowAlias} — Run #${e.runNumber}</h2>
        ${r ? c`<uui-button label="Start" look="primary" @click=${this._onStartClick}>Start</uui-button>` : p}
        ${i ? c`
              <uui-button label="Retry" look="primary" ?disabled=${e.retrying} @click=${this._onRetryClick}>
                ${e.retrying ? c`<uui-loader></uui-loader>` : "Retry"}
              </uui-button>
            ` : p}
        ${u ? c`<uui-button label="Continue" look="primary" @click=${this._onStartClick}>Continue</uui-button>` : p}
        ${h ? c`
              <uui-button
                label="Cancel"
                look="secondary"
                color="danger"
                ?disabled=${e.cancelling}
                @click=${this._onCancelClick}
              >
                Cancel
              </uui-button>
            ` : p}
      </div>

      <div class="detail-grid">
        ${this._renderStepProgress()}
        ${V}
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
C.styles = I`
    :host { display: block; padding: var(--uui-size-layout-1); }
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
    .step-item.clickable { cursor: pointer; }
    .step-item.clickable:hover { background: var(--uui-color-surface-emphasis); }
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
    .step-item.pending .step-icon-wrapper { border-color: var(--uui-color-border); background: var(--uui-color-surface); }
    .step-item.active .step-icon-wrapper { border-color: var(--uui-color-current); background: var(--uui-color-current); }
    .step-item.complete .step-icon-wrapper { border-color: var(--uui-color-positive); background: var(--uui-color-positive); }
    .step-item.error .step-icon-wrapper { border-color: var(--uui-color-danger); background: var(--uui-color-danger); }
    .step-icon { font-size: 14px; }
    .step-item.pending .step-icon { color: var(--uui-color-text-alt); }
    .step-item.active .step-icon,
    .step-item.complete .step-icon,
    .step-item.error .step-icon { color: var(--uui-color-surface); }
    .step-text { display: flex; flex-direction: column; gap: 2px; padding-top: 3px; }
    .step-name { color: var(--uui-color-text); font-weight: 500; }
    .step-item.pending .step-name { color: var(--uui-color-text-alt); }
    .step-subtitle { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); }
    @keyframes spin { to { transform: rotate(360deg); } }
    .step-icon-spin { animation: spin 1.5s linear infinite; }
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
    .completion-banner uui-button { flex-shrink: 0; }
  `;
P([
  b()
], C.prototype, "_state", 2);
P([
  b()
], C.prototype, "_popoverOpen", 2);
P([
  b()
], C.prototype, "_popoverArtifactPath", 2);
C = P([
  z("agentrun-instance-detail")
], C);
const Be = C;
export {
  C as AgentRunInstanceDetailElement,
  Be as default
};
//# sourceMappingURL=agentrun-instance-detail.element-B1z9OojA.js.map
