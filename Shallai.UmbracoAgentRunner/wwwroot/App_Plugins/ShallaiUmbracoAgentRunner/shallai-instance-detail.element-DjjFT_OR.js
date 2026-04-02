import { UmbLitElement as z } from "@umbraco-cms/backoffice/lit-element";
import { css as T, property as g, state as p, customElement as E, html as r, nothing as d } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as j } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal as q } from "@umbraco-cms/backoffice/modal";
import { b as W, a as U, d as V, m as F, s as K, e as J, f as P, h as G } from "./api-client-NjdX6lGK.js";
import { n as X } from "./instance-list-helpers-Cp7Qb08Y.js";
const Y = /* @__PURE__ */ new Set([
  "p",
  "h1",
  "h2",
  "h3",
  "h4",
  "h5",
  "h6",
  "ul",
  "ol",
  "li",
  "a",
  "strong",
  "em",
  "code",
  "pre",
  "blockquote",
  "hr",
  "br",
  "table",
  "tr",
  "td",
  "th",
  "thead",
  "tbody"
]);
function Q(e) {
  const t = Z(e);
  return te(t);
}
function Z(e) {
  const t = [], a = e.replace(/```[\s\S]*?```/g, (o) => {
    const c = t.length, f = o.replace(/^```[^\n]*\n?/, "").replace(/\n?```$/, "");
    return t.push(`<pre><code>${N(f)}</code></pre>`), `\0CB${c}\0`;
  }).split(`
`), i = [];
  let n = 0;
  for (; n < a.length; ) {
    const o = a[n], c = o.match(/^\x00CB(\d+)\x00$/);
    if (c) {
      i.push(t[parseInt(c[1], 10)]), n++;
      continue;
    }
    if (/^(---+|\*\*\*+|___+)\s*$/.test(o)) {
      i.push("<hr>"), n++;
      continue;
    }
    const f = o.match(/^(#{1,6})\s+(.+)$/);
    if (f) {
      const l = f[1].length;
      i.push(`<h${l}>${I(f[2])}</h${l}>`), n++;
      continue;
    }
    if (o.startsWith("> ")) {
      const l = [];
      for (; n < a.length && a[n].startsWith("> "); )
        l.push(a[n].slice(2)), n++;
      i.push(`<blockquote>${I(l.join("<br>"))}</blockquote>`);
      continue;
    }
    if (/^[-*]\s+/.test(o)) {
      const l = [];
      for (; n < a.length && /^[-*]\s+/.test(a[n]); )
        l.push(a[n].replace(/^[-*]\s+/, "")), n++;
      i.push("<ul>" + l.map((S) => `<li>${I(S)}</li>`).join("") + "</ul>");
      continue;
    }
    if (/^\d+\.\s+/.test(o)) {
      const l = [];
      for (; n < a.length && /^\d+\.\s+/.test(a[n]); )
        l.push(a[n].replace(/^\d+\.\s+/, "")), n++;
      i.push("<ol>" + l.map((S) => `<li>${I(S)}</li>`).join("") + "</ol>");
      continue;
    }
    if (o.trim() === "") {
      n++;
      continue;
    }
    const u = [];
    for (; n < a.length && a[n].trim() !== "" && !/^(#{1,6}\s|[-*]\s|>\s|\d+\.\s|---+|___+|\*\*\*+|\x00CB)/.test(a[n]); )
      u.push(a[n]), n++;
    u.length > 0 && i.push(`<p>${I(u.join("<br>"))}</p>`);
  }
  return i.join("");
}
function I(e) {
  return e = e.replace(/`([^`]+)`/g, (t, s) => `<code>${N(s)}</code>`), e = e.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>"), e = e.replace(/__(.+?)__/g, "<strong>$1</strong>"), e = e.replace(/\*(.+?)\*/g, "<em>$1</em>"), e = e.replace(/_(.+?)_/g, "<em>$1</em>"), e = e.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (t, s, a) => /^https?:\/\//i.test(a) ? `<a href="${ee(a)}" target="_blank" rel="noopener">${N(s)}</a>` : s), e;
}
function N(e) {
  return e.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}
function ee(e) {
  return e.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/'/g, "&#39;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
function te(e) {
  const t = document.createElement("template");
  t.innerHTML = e, O(t.content);
  const s = document.createElement("div");
  return s.appendChild(t.content.cloneNode(!0)), s.innerHTML;
}
function O(e) {
  const t = Array.from(e.childNodes);
  for (const s of t)
    if (s.nodeType === Node.ELEMENT_NODE) {
      const a = s, i = a.tagName.toLowerCase();
      if (Y.has(i)) {
        const n = Array.from(a.attributes);
        for (const o of n)
          i === "a" && (o.name === "href" || o.name === "target" || o.name === "rel") || a.removeAttribute(o.name);
        if (i === "a") {
          const o = a.getAttribute("href") ?? "";
          /^https?:\/\//i.test(o) || a.removeAttribute("href"), a.setAttribute("target", "_blank"), a.setAttribute("rel", "noopener");
        }
        O(a);
      } else {
        const n = document.createTextNode(a.textContent ?? "");
        e.replaceChild(n, s);
      }
    } else s.nodeType !== Node.TEXT_NODE && e.removeChild(s);
}
var se = Object.defineProperty, ae = Object.getOwnPropertyDescriptor, k = (e, t, s, a) => {
  for (var i = a > 1 ? void 0 : a ? ae(t, s) : t, n = e.length - 1, o; n >= 0; n--)
    (o = e[n]) && (i = (a ? o(t, s, i) : o(i)) || i);
  return a && i && se(t, s, i), i;
};
let v = class extends z {
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
            ` : d}
        ${this.result != null ? r`
              <div class="section-label">Result</div>
              <div class="mono-block">${this.result}</div>
            ` : d}
      </div>
    ` : d;
  }
  render() {
    const e = this.status !== "running";
    return r`
      <div
        class="tool-call ${this.status}"
        role="group"
        aria-label="Tool call: ${this.toolName}"
        aria-expanded=${e ? String(this._expanded) : d}
        tabindex=${e ? "0" : d}
        @click=${this._toggle}
        @keydown=${this._onKeydown}
      >
        <div class="tool-call-header">
          ${this._renderStatusIcon()}
          <span class="tool-name">${this.toolName}</span>
          <span class="tool-summary">${this.summary ? `— ${this.summary}` : ""}</span>
          ${e ? r`<uui-icon class="chevron ${this._expanded ? "open" : ""}" name="icon-navigation-down"></uui-icon>` : d}
        </div>
        ${this._renderBody()}
      </div>
    `;
  }
};
v.styles = T`
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
  g({ type: String })
], v.prototype, "toolName", 2);
k([
  g({ type: String })
], v.prototype, "toolCallId", 2);
k([
  g({ type: String })
], v.prototype, "summary", 2);
k([
  g({ attribute: !1 })
], v.prototype, "arguments", 2);
k([
  g({ type: String })
], v.prototype, "result", 2);
k([
  g({ type: String })
], v.prototype, "status", 2);
k([
  p()
], v.prototype, "_expanded", 2);
v = k([
  E("shallai-tool-call")
], v);
var ie = Object.defineProperty, ne = Object.getOwnPropertyDescriptor, $ = (e, t, s, a) => {
  for (var i = a > 1 ? void 0 : a ? ne(t, s) : t, n = e.length - 1, o; n >= 0; n--)
    (o = e[n]) && (i = (a ? o(t, s, i) : o(i)) || i);
  return a && i && ie(t, s, i), i;
};
let x = class extends z {
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
    const t = Q(e), s = document.createElement("div");
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
          ${this.timestamp ? r`<span class="user-timestamp">${this._formatTime(this.timestamp)}</span>` : d}
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
            >` : d}
      </div>
    `;
  }
};
x.styles = T`
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
  g({ type: String })
], x.prototype, "role", 2);
$([
  g({ type: String })
], x.prototype, "content", 2);
$([
  g({ type: String })
], x.prototype, "timestamp", 2);
$([
  g({ type: Boolean, attribute: "is-streaming" })
], x.prototype, "isStreaming", 2);
$([
  g({ attribute: !1 })
], x.prototype, "toolCalls", 2);
x = $([
  E("shallai-chat-message")
], x);
var oe = Object.defineProperty, re = Object.getOwnPropertyDescriptor, w = (e, t, s, a) => {
  for (var i = a > 1 ? void 0 : a ? re(t, s) : t, n = e.length - 1, o; n >= 0; n--)
    (o = e[n]) && (i = (a ? o(t, s, i) : o(i)) || i);
  return a && i && oe(t, s, i), i;
};
let _ = class extends z {
  constructor() {
    super(...arguments), this.messages = [], this.isStreaming = !1, this.inputEnabled = !1, this.inputPlaceholder = "Send a message to start.", this._inputValue = "", this._autoScrollPaused = !1, this._hasNewMessages = !1, this._wasStreaming = !1;
  }
  _onScroll(e) {
    const t = e.target;
    if (!t) return;
    const { scrollTop: s, scrollHeight: a, clientHeight: i } = t;
    a - s - i < 50 ? (this._autoScrollPaused = !1, this._hasNewMessages = !1) : this._autoScrollPaused = !0;
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
      return r`<div class="empty-state">Send a message to begin.</div>`;
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
          ` : d}

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
_.styles = T`
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
  g({ attribute: !1 })
], _.prototype, "messages", 2);
w([
  g({ type: Boolean, attribute: "is-streaming" })
], _.prototype, "isStreaming", 2);
w([
  g({ type: Boolean, attribute: "input-enabled" })
], _.prototype, "inputEnabled", 2);
w([
  g({ type: String, attribute: "input-placeholder" })
], _.prototype, "inputPlaceholder", 2);
w([
  p()
], _.prototype, "_inputValue", 2);
w([
  p()
], _.prototype, "_autoScrollPaused", 2);
w([
  p()
], _.prototype, "_hasNewMessages", 2);
w([
  p()
], _.prototype, "_wasStreaming", 2);
_ = w([
  E("shallai-chat-panel")
], _);
function le(e) {
  const t = e.split("/");
  return decodeURIComponent(t[t.length - 1]);
}
function ce(e, t) {
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
    default:
      return e.status;
  }
}
function ue(e) {
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
function pe(e, t) {
  return e === "Active" && t;
}
var he = Object.defineProperty, de = Object.getOwnPropertyDescriptor, D = (e) => {
  throw TypeError(e);
}, m = (e, t, s, a) => {
  for (var i = a > 1 ? void 0 : a ? de(t, s) : t, n = e.length - 1, o; n >= 0; n--)
    (o = e[n]) && (i = (a ? o(t, s, i) : o(i)) || i);
  return a && i && he(t, s, i), i;
}, L = (e, t, s) => t.has(e) || D("Cannot " + s), C = (e, t, s) => (L(e, t, "read from private field"), s ? s.call(e) : t.get(e)), ge = (e, t, s) => t.has(e) ? D("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(e) : t.set(e, s), me = (e, t, s, a) => (L(e, t, "write to private field"), t.set(e, s), s), y;
let h = class extends z {
  constructor() {
    super(), ge(this, y), this._instance = null, this._loading = !0, this._error = !1, this._selectedStepId = null, this._cancelling = !1, this._runNumber = 0, this._streaming = !1, this._providerError = !1, this._chatMessages = [], this._streamingText = "", this._viewingStepId = null, this._historyMessages = [], this._stepCompletable = !1, this._agentResponding = !1, this._toolBatchOpen = !1, this.consumeContext(j, (e) => {
      me(this, y, e);
    });
  }
  connectedCallback() {
    super.connectedCallback(), this._loading = !0, this._error = !1, this._loadData();
  }
  async _loadData() {
    try {
      const e = le(window.location.pathname), t = await C(this, y)?.getLatestToken(), s = await W(e, t);
      this._instance = s;
      const a = await U(s.workflowAlias, t), n = X(a).find((o) => o.id === s.id);
      this._runNumber = n?.runNumber ?? 0, this._error = !1;
    } catch {
      this._error = !0;
    } finally {
      this._loading = !1;
    }
  }
  _navigateBack() {
    if (!this._instance) return;
    const e = ce(
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
        const t = await C(this, y)?.getLatestToken(), s = this._instance.id, a = await V(s, e.id, t);
        this._historyMessages = F(a);
      } catch {
        this._historyMessages = [];
      }
    }
  }
  disconnectedCallback() {
    super.disconnectedCallback(), this._streaming = !1, this._agentResponding = !1;
  }
  async _onStartClick() {
    if (!(this._streaming || !this._instance)) {
      this._streaming = !0, this._providerError = !1, this._viewingStepId = null, this._streamingText = "";
      try {
        const e = await C(this, y)?.getLatestToken(), t = await K(this._instance.id, e);
        if (!t.ok) {
          if (t.status === 400) {
            this._providerError = !0;
            return;
          }
          await this._loadData();
          return;
        }
        if (!t.body) return;
        const s = t.body.getReader(), a = new TextDecoder();
        let i = "";
        for (; ; ) {
          const { done: n, value: o } = await s.read();
          if (n) break;
          i += a.decode(o, { stream: !0 });
          const c = i.split(`

`);
          i = c.pop();
          for (const f of c) {
            const u = f.split(`
`), l = u.find((M) => M.startsWith("event:"))?.slice(7), S = u.find((M) => M.startsWith("data:"))?.slice(5);
            l && S && this._handleSseEvent(l, JSON.parse(S));
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
  }
  _checkStepCompletable() {
    if (!this._instance) return;
    if (!(this._instance.workflowMode !== "autonomous")) {
      this._stepCompletable = !1;
      return;
    }
    const s = !this._instance.steps.find((i) => i.status === "Active") && this._instance.steps.some((i) => i.status === "Complete"), a = this._instance.steps.some((i) => i.status === "Pending");
    this._stepCompletable = s && a;
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
      case "tool.start": {
        this._finaliseStreamingMessage();
        const s = t.toolCallId, a = t.toolName, i = {
          toolCallId: s,
          toolName: a,
          summary: a,
          arguments: null,
          result: null,
          status: "running"
        }, n = this._chatMessages[this._chatMessages.length - 1];
        if (this._toolBatchOpen && n && n.role === "agent") {
          const o = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, o),
            { ...n, toolCalls: [...n.toolCalls ?? [], i] },
            ...this._chatMessages.slice(o + 1)
          ];
        } else if (n && n.role === "agent" && !n.toolCalls?.length) {
          const o = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, o),
            { ...n, toolCalls: [i] },
            ...this._chatMessages.slice(o + 1)
          ];
        } else
          this._chatMessages = [
            ...this._chatMessages,
            {
              role: "agent",
              content: "",
              timestamp: (/* @__PURE__ */ new Date()).toISOString(),
              toolCalls: [i]
            }
          ];
        this._toolBatchOpen = !0;
        break;
      }
      case "tool.args": {
        const s = t.toolCallId, a = t.arguments, i = this._chatMessages.findLastIndex(
          (c) => c.role === "agent" && c.toolCalls?.some((f) => f.toolCallId === s)
        );
        if (i === -1) break;
        const n = this._chatMessages[i], o = n.toolCalls.map(
          (c) => c.toolCallId === s ? { ...c, arguments: a, summary: J(c.toolName, a) } : c
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, i),
          { ...n, toolCalls: o },
          ...this._chatMessages.slice(i + 1)
        ];
        break;
      }
      case "tool.end": {
        const s = t.toolCallId, a = this._chatMessages.findLastIndex(
          (o) => o.role === "agent" && o.toolCalls?.some((c) => c.toolCallId === s)
        );
        if (a === -1) break;
        const i = this._chatMessages[a], n = i.toolCalls.map(
          (o) => o.toolCallId === s && o.status === "running" ? { ...o, status: "complete" } : o
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, a),
          { ...i, toolCalls: n },
          ...this._chatMessages.slice(a + 1)
        ];
        break;
      }
      case "tool.result": {
        const s = t.toolCallId, a = t.result, i = typeof a == "string" ? a : JSON.stringify(a), n = typeof i == "string" && i.startsWith("Tool '") && (i.includes("error") || i.includes("failed")), o = this._chatMessages.findLastIndex(
          (u) => u.role === "agent" && u.toolCalls?.some((l) => l.toolCallId === s)
        );
        if (o === -1) break;
        const c = this._chatMessages[o], f = c.toolCalls.map(
          (u) => u.toolCallId === s ? { ...u, result: i, status: n ? "error" : "complete" } : u
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, o),
          { ...c, toolCalls: f },
          ...this._chatMessages.slice(o + 1)
        ];
        break;
      }
      case "step.started":
        if (this._finaliseStreamingMessage(), this._toolBatchOpen = !1, this._instance) {
          const s = this._instance.steps.find((a) => a.id === t.stepId);
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
          const i = this._instance.steps.find((n) => n.id === t.stepId);
          i && (i.status = t.status), this.requestUpdate();
        }
        const s = t.stepName || t.stepId, a = t.status === "Error" ? "failed" : "completed";
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
      const s = await C(this, y)?.getLatestToken();
      await P(this._instance.id, t, s);
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
      const s = await C(this, y)?.getLatestToken();
      await P(this._instance.id, t, s);
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
        await q(this, {
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
        const e = await C(this, y)?.getLatestToken();
        await G(this._instance.id, e), await this._loadData();
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
                aria-label="Step ${t + 1}: ${e.name} — ${A(e)}"
                @click=${e.status !== "Pending" ? () => this._onStepClick(e) : d}
                aria-current=${e.status === "Active" ? "step" : d}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${pe(e.status, this._agentResponding) ? "step-icon-spin" : ""}"
                    name=${ue(e.status)}
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
      </div>
    ` : d;
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
    const e = this._instance, t = e.workflowMode !== "autonomous", s = e.status === "Completed" || e.status === "Failed" || e.status === "Cancelled", a = e.steps.find((b) => b.status === "Active"), i = !!a, n = e.status === "Pending" && !this._streaming, o = "Start", c = !t && e.status === "Running" && !i && !this._streaming && e.steps.some((b) => b.status === "Pending"), f = !t && (e.status === "Running" || e.status === "Pending") && !this._streaming;
    let u, l;
    t ? s ? (u = !1, l = "Workflow complete") : this._viewingStepId ? (u = !1, l = "Viewing step history") : this._agentResponding ? (u = !1, l = "Agent is responding...") : this._streaming || a || e.status === "Running" ? (u = !0, l = "Message the agent...") : (u = !1, l = "Send a message to start.") : (u = this._streaming && !this._viewingStepId, l = s ? "Workflow complete" : a ? "Step complete" : "Click 'Start' to begin the workflow.");
    const S = t && !this._streaming ? (b) => this._onSendAndStream(b) : (b) => this._onSendMessage(b), M = t && this._stepCompletable && !this._agentResponding, B = e.steps.every((b) => b.status === "Complete"), R = t && s && B, H = this._providerError ? r`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>` : r`
          <div class="main-panel">
            ${M ? r`
                  <div class="completion-banner">
                    <span>Step complete — review the output, then advance when ready</span>
                    <uui-button label="Continue to next step" look="primary" @click=${this._onAdvanceStep}>
                      Continue to next step
                    </uui-button>
                  </div>
                ` : d}
            ${R ? r`
                  <div class="completion-banner">
                    <span>Workflow complete — all steps finished</span>
                  </div>
                ` : d}
            <shallai-chat-panel
              .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
              ?is-streaming=${this._streaming && !this._viewingStepId}
              ?input-enabled=${u}
              input-placeholder=${l}
              @send-message=${S}
            ></shallai-chat-panel>
          </div>`;
    return r`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${e.workflowName || e.workflowAlias} — Run #${this._runNumber}</h2>
        ${n ? r`
              <uui-button label=${o} look="primary" @click=${this._onStartClick}>
                ${o}
              </uui-button>
            ` : d}
        ${c ? r`
              <uui-button label="Continue" look="primary" @click=${this._onStartClick}>
                Continue
              </uui-button>
            ` : d}
        ${f ? r`
              <uui-button
                label="Cancel"
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
        ${H}
      </div>
    `;
  }
};
y = /* @__PURE__ */ new WeakMap();
h.styles = T`
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
m([
  p()
], h.prototype, "_instance", 2);
m([
  p()
], h.prototype, "_loading", 2);
m([
  p()
], h.prototype, "_error", 2);
m([
  p()
], h.prototype, "_selectedStepId", 2);
m([
  p()
], h.prototype, "_cancelling", 2);
m([
  p()
], h.prototype, "_runNumber", 2);
m([
  p()
], h.prototype, "_streaming", 2);
m([
  p()
], h.prototype, "_providerError", 2);
m([
  p()
], h.prototype, "_chatMessages", 2);
m([
  p()
], h.prototype, "_streamingText", 2);
m([
  p()
], h.prototype, "_viewingStepId", 2);
m([
  p()
], h.prototype, "_historyMessages", 2);
m([
  p()
], h.prototype, "_stepCompletable", 2);
m([
  p()
], h.prototype, "_agentResponding", 2);
h = m([
  E("shallai-instance-detail")
], h);
const Se = h;
export {
  h as ShallaiInstanceDetailElement,
  Se as default
};
//# sourceMappingURL=shallai-instance-detail.element-DjjFT_OR.js.map
