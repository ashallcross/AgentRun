import { UmbLitElement as A } from "@umbraco-cms/backoffice/lit-element";
import { css as C, property as g, customElement as z, html as c, state as _, nothing as k } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as D } from "@umbraco-cms/backoffice/auth";
const ot = [
  {
    type: "section",
    alias: "AgentRun.Umbraco.Section",
    name: "Agent Workflows Section",
    meta: {
      label: "Agent Workflows",
      pathname: "agent-workflows"
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionUserPermission",
        match: "AgentRun.Umbraco.Section"
      }
    ]
  },
  {
    type: "menu",
    alias: "AgentRun.Umbraco.Menu",
    name: "Agent Workflows Menu"
  },
  {
    type: "sectionSidebarApp",
    kind: "menu",
    alias: "AgentRun.Umbraco.SectionSidebarApp.Menu",
    name: "Agent Workflows Sidebar Menu",
    meta: {
      label: "Agent Workflows",
      menu: "AgentRun.Umbraco.Menu"
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "AgentRun.Umbraco.Section"
      }
    ]
  },
  {
    type: "sectionView",
    alias: "AgentRun.Umbraco.SectionView.Dashboard",
    name: "Agent Workflows Dashboard View",
    element: () => import("./agentrun-dashboard.element-BaTtHLfe.js"),
    meta: {
      label: "Overview",
      pathname: "overview",
      icon: "icon-dashboard"
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "AgentRun.Umbraco.Section"
      }
    ]
  }
], j = /* @__PURE__ */ new Set([
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
function N(t) {
  const e = L(t);
  return B(e);
}
function L(t) {
  const e = [], o = t.replace(/```[\s\S]*?```/g, (a) => {
    const u = e.length, f = a.replace(/^```[^\n]*\n?/, "").replace(/\n?```$/, "");
    return e.push(`<pre><code>${x(f)}</code></pre>`), `\0CB${u}\0`;
  }).split(`
`), r = [];
  let i = 0;
  for (; i < o.length; ) {
    const a = o[i], u = a.match(/^\x00CB(\d+)\x00$/);
    if (u) {
      r.push(e[parseInt(u[1], 10)]), i++;
      continue;
    }
    if (/^(---+|\*\*\*+|___+)\s*$/.test(a)) {
      r.push("<hr>"), i++;
      continue;
    }
    const f = a.match(/^(#{1,6})\s+(.+)$/);
    if (f) {
      const s = f[1].length;
      r.push(`<h${s}>${h(f[2])}</h${s}>`), i++;
      continue;
    }
    if (a.startsWith("> ")) {
      const s = [];
      for (; i < o.length && o[i].startsWith("> "); )
        s.push(o[i].slice(2)), i++;
      r.push(`<blockquote>${h(s.join("<br>"))}</blockquote>`);
      continue;
    }
    if (/^[-*]\s+/.test(a)) {
      const s = [];
      for (; i < o.length && /^[-*]\s+/.test(o[i]); )
        s.push(o[i].replace(/^[-*]\s+/, "")), i++;
      r.push("<ul>" + s.map(($) => `<li>${h($)}</li>`).join("") + "</ul>");
      continue;
    }
    if (/^\d+\.\s+/.test(a)) {
      const s = [];
      for (; i < o.length && /^\d+\.\s+/.test(o[i]); )
        s.push(o[i].replace(/^\d+\.\s+/, "")), i++;
      r.push("<ol>" + s.map(($) => `<li>${h($)}</li>`).join("") + "</ol>");
      continue;
    }
    if (a.trim() === "") {
      i++;
      continue;
    }
    const b = [];
    for (; i < o.length && o[i].trim() !== "" && !/^(#{1,6}\s|[-*]\s|>\s|\d+\.\s|---+|___+|\*\*\*+|\x00CB)/.test(o[i]); )
      b.push(o[i]), i++;
    b.length > 0 && r.push(`<p>${h(b.join("<br>"))}</p>`);
  }
  return r.join("");
}
function h(t) {
  return t = t.replace(/`([^`]+)`/g, (e, n) => `<code>${x(n)}</code>`), t = t.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>"), t = t.replace(/__(.+?)__/g, "<strong>$1</strong>"), t = t.replace(/\*(.+?)\*/g, "<em>$1</em>"), t = t.replace(/_(.+?)_/g, "<em>$1</em>"), t = t.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (e, n, o) => /^https?:\/\//i.test(o) ? `<a href="${M(o)}" target="_blank" rel="noopener">${x(n)}</a>` : n), t;
}
function x(t) {
  return t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}
function M(t) {
  return t.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/'/g, "&#39;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
function B(t) {
  const e = document.createElement("template");
  e.innerHTML = t, T(e.content);
  const n = document.createElement("div");
  return n.appendChild(e.content.cloneNode(!0)), n.innerHTML;
}
function T(t) {
  const e = Array.from(t.childNodes);
  for (const n of e)
    if (n.nodeType === Node.ELEMENT_NODE) {
      const o = n, r = o.tagName.toLowerCase();
      if (j.has(r)) {
        const i = Array.from(o.attributes);
        for (const a of i)
          r === "a" && (a.name === "href" || a.name === "target" || a.name === "rel") || o.removeAttribute(a.name);
        if (r === "a") {
          const a = o.getAttribute("href") ?? "";
          /^https?:\/\//i.test(a) || o.removeAttribute("href"), o.setAttribute("target", "_blank"), o.setAttribute("rel", "noopener");
        }
        T(o);
      } else {
        const i = document.createTextNode(o.textContent ?? "");
        t.replaceChild(i, n);
      }
    } else n.nodeType !== Node.TEXT_NODE && t.removeChild(n);
}
var W = Object.defineProperty, q = Object.getOwnPropertyDescriptor, S = (t, e, n, o) => {
  for (var r = o > 1 ? void 0 : o ? q(e, n) : e, i = t.length - 1, a; i >= 0; i--)
    (a = t[i]) && (r = (o ? a(e, n, r) : a(r)) || r);
  return o && r && W(e, n, r), r;
};
let v = class extends A {
  constructor() {
    super(...arguments), this.content = "";
  }
  render() {
    const t = N(this.content);
    return c`<div class="markdown-body" .innerHTML=${t}></div>`;
  }
};
v.styles = C`
    :host {
      display: block;
      font-family: var(--uui-font-family);
      font-size: var(--uui-font-size-default);
      color: var(--uui-color-text);
      line-height: 1.6;
    }
    h1 { font-size: var(--uui-font-size-xxl); margin: 0 0 var(--uui-size-space-4); font-weight: 700; }
    h2 { font-size: var(--uui-font-size-xl); margin: var(--uui-size-space-5) 0 var(--uui-size-space-3); font-weight: 700; }
    h3 { font-size: var(--uui-font-size-l); margin: var(--uui-size-space-4) 0 var(--uui-size-space-2); font-weight: 600; }
    h4 { font-size: var(--uui-font-size-m); margin: var(--uui-size-space-3) 0 var(--uui-size-space-2); font-weight: 600; }
    h5, h6 { font-size: var(--uui-font-size-s); margin: var(--uui-size-space-2) 0 var(--uui-size-space-1); font-weight: 600; }
    p { margin: 0 0 var(--uui-size-space-3); }
    a { color: var(--uui-color-interactive); text-decoration: none; }
    a:hover { text-decoration: underline; }
    ul, ol { margin: 0 0 var(--uui-size-space-3); padding-left: var(--uui-size-space-5); }
    li { margin-bottom: var(--uui-size-space-1); }
    pre {
      background: var(--uui-color-surface-emphasis);
      padding: var(--uui-size-space-3);
      border-radius: var(--uui-border-radius);
      overflow-x: auto;
      margin: 0 0 var(--uui-size-space-3);
    }
    pre code { font-family: monospace; font-size: var(--uui-font-size-s); background: none; padding: 0; }
    code { font-family: monospace; font-size: 0.9em; background: var(--uui-color-surface-emphasis); padding: 2px 6px; border-radius: 3px; }
    blockquote {
      border-left: 3px solid var(--uui-color-border);
      margin: 0 0 var(--uui-size-space-3);
      padding: var(--uui-size-space-2) var(--uui-size-space-3);
      color: var(--uui-color-text-alt);
    }
    table { width: 100%; border-collapse: collapse; margin: 0 0 var(--uui-size-space-3); }
    th, td { border: 1px solid var(--uui-color-border); padding: var(--uui-size-space-2) var(--uui-size-space-3); text-align: left; }
    th { background: var(--uui-color-surface-emphasis); font-weight: 600; }
    hr { border: none; border-top: 1px solid var(--uui-color-border); margin: var(--uui-size-space-4) 0; }
    strong { font-weight: 700; }
  `;
S([
  g({ type: String })
], v.prototype, "content", 2);
v = S([
  z("agentrun-markdown-renderer")
], v);
var G = Object.defineProperty, H = Object.getOwnPropertyDescriptor, P = (t, e, n, o) => {
  for (var r = o > 1 ? void 0 : o ? H(e, n) : e, i = t.length - 1, a; i >= 0; i--)
    (a = t[i]) && (r = (o ? a(e, n, r) : a(r)) || r);
  return o && r && G(e, n, r), r;
};
let w = class extends A {
  constructor() {
    super(...arguments), this.steps = [];
  }
  _deriveArtifacts() {
    const t = [];
    for (const e of this.steps)
      if (!(!e.writesTo || e.writesTo.length === 0) && e.status === "Complete")
        for (const n of e.writesTo)
          t.push({ path: n, stepName: e.name, stepStatus: e.status });
    return t;
  }
  _getFilename(t) {
    const e = t.split("/");
    return e[e.length - 1];
  }
  _onArtifactClick(t) {
    this.dispatchEvent(
      new CustomEvent("artifact-selected", {
        detail: { path: t.path, stepName: t.stepName },
        bubbles: !0,
        composed: !0
      })
    );
  }
  _onKeyDown(t, e) {
    (t.key === "Enter" || t.key === " ") && (t.preventDefault(), this._onArtifactClick(e));
  }
  render() {
    const t = this._deriveArtifacts();
    return c`
      <h4 class="heading">Artifacts</h4>
      ${t.length === 0 ? c`<p class="empty-state">No artifacts yet.</p>` : c`
            <div class="artifact-list" role="list">
              ${t.map(
      (e) => c`
                  <div
                    class="artifact-item"
                    role="listitem"
                  >
                    <uui-icon class="file-icon" name="icon-document"></uui-icon>
                    <div class="artifact-text">
                      <span
                        class="artifact-name"
                        role="button"
                        tabindex="0"
                        aria-label="${this._getFilename(e.path)} from ${e.stepName}"
                        @click=${() => this._onArtifactClick(e)}
                        @keydown=${(n) => this._onKeyDown(n, e)}
                      >${this._getFilename(e.path)}</span>
                      <span class="artifact-step">${e.stepName}</span>
                    </div>
                  </div>
                `
    )}
            </div>
          `}
    `;
  }
};
w.styles = C`
    :host {
      display: block;
    }

    .heading {
      margin: 0 0 var(--uui-size-space-3);
      font-size: var(--uui-type-small-size);
      font-weight: 600;
      color: var(--uui-color-text);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .empty-state {
      margin: 0;
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    .artifact-list {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-3);
    }

    .artifact-item {
      display: flex;
      align-items: flex-start;
      gap: var(--uui-size-space-3);
    }

    .file-icon {
      flex-shrink: 0;
      margin-top: 2px;
      color: var(--uui-color-text-alt);
    }

    .artifact-text {
      display: flex;
      flex-direction: column;
      gap: 1px;
      min-width: 0;
    }

    .artifact-name {
      color: var(--uui-color-interactive);
      cursor: pointer;
      font-size: var(--uui-type-small-size);
      word-break: break-all;
    }

    .artifact-name:hover {
      color: var(--uui-color-interactive-emphasis);
    }

    .artifact-name:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 2px;
      border-radius: 2px;
    }

    .artifact-step {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

  `;
P([
  g({ attribute: !1 })
], w.prototype, "steps", 2);
w = P([
  z("agentrun-artifact-list")
], w);
function F(t, e) {
  if (!e) return t;
  if (typeof e.path == "string") {
    const n = e.path.split("/");
    return n[n.length - 1] || t;
  }
  if (typeof e.url == "string")
    return e.url.length > 60 ? e.url.slice(0, 60) + "…" : e.url;
  for (const n of Object.values(e))
    if (typeof n == "string") return n;
  return t;
}
const d = "/umbraco/api/agentrun";
async function y(t, e) {
  const n = { Accept: "application/json" };
  e && (n.Authorization = `Bearer ${e}`);
  const o = await fetch(`${d}${t}`, { headers: n });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
  return o.json();
}
async function J(t, e) {
  const n = {};
  e && (n.Authorization = `Bearer ${e}`);
  const o = await fetch(`${d}${t}`, { headers: n, credentials: "same-origin" });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
  return o.text();
}
async function R(t, e, n) {
  const o = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  n && (o.Authorization = `Bearer ${n}`);
  const r = await fetch(`${d}${t}`, {
    method: "POST",
    headers: o,
    body: JSON.stringify(e)
  });
  if (!r.ok)
    throw new Error(`API error: ${r.status} ${r.statusText}`);
  return r.json();
}
function rt(t) {
  return y("/workflows", t);
}
function it(t, e) {
  return y(`/instances?workflowAlias=${encodeURIComponent(t)}`, e);
}
function at(t, e) {
  return R("/instances", { workflowAlias: t }, e);
}
function st(t, e) {
  return y(`/instances/${encodeURIComponent(t)}`, e);
}
function ct(t, e) {
  return R(`/instances/${encodeURIComponent(t)}/cancel`, {}, e);
}
async function lt(t, e, n) {
  const o = { "Content-Type": "application/json" };
  n && (o.Authorization = `Bearer ${n}`);
  const r = await fetch(
    `${d}/instances/${encodeURIComponent(t)}/message`,
    { method: "POST", headers: o, body: JSON.stringify({ message: e }) }
  );
  if (!r.ok)
    throw new Error(`API error: ${r.status} ${r.statusText}`);
}
async function ut(t, e) {
  const n = {};
  return e && (n.Authorization = `Bearer ${e}`), fetch(`${d}/instances/${encodeURIComponent(t)}/start`, {
    method: "POST",
    headers: n
  });
}
async function pt(t, e) {
  const n = {};
  return e && (n.Authorization = `Bearer ${e}`), fetch(`${d}/instances/${encodeURIComponent(t)}/retry`, {
    method: "POST",
    headers: n
  });
}
function ft(t, e, n) {
  return y(
    `/instances/${encodeURIComponent(t)}/conversation/${encodeURIComponent(e)}`,
    n
  );
}
function dt(t) {
  const e = [];
  for (const n of t)
    if (n.role === "assistant" && n.content != null && !n.toolCallId)
      e.push({
        role: "agent",
        content: n.content,
        timestamp: n.timestamp
      });
    else if (n.role === "assistant" && n.toolCallId) {
      let o = null;
      if (n.toolArguments)
        try {
          o = JSON.parse(n.toolArguments);
        } catch {
          o = null;
        }
      const r = {
        toolCallId: n.toolCallId,
        toolName: n.toolName ?? "unknown",
        summary: F(n.toolName ?? "unknown", o),
        arguments: o,
        result: null,
        status: "complete"
      }, i = V(e);
      i ? i.toolCalls = [...i.toolCalls ?? [], r] : e.push({
        role: "agent",
        content: "",
        timestamp: n.timestamp,
        toolCalls: [r]
      });
    } else if (n.role === "tool" && n.toolCallId) {
      const o = n.toolResult ?? null, r = typeof o == "string" && o.startsWith("Tool '") && (o.includes("error") || o.includes("failed"));
      for (let i = e.length - 1; i >= 0; i--) {
        const u = e[i].toolCalls?.find((f) => f.toolCallId === n.toolCallId);
        if (u) {
          u.result = o, r && (u.status = "error");
          break;
        }
      }
    } else n.role === "user" && n.content != null ? e.push({
      role: "user",
      content: n.content,
      timestamp: n.timestamp
    }) : n.role === "system" && n.content != null && e.push({
      role: "system",
      content: n.content,
      timestamp: n.timestamp
    });
  return e;
}
function V(t) {
  for (let e = t.length - 1; e >= 0; e--)
    if (t[e].role === "agent") return t[e];
  return null;
}
function K(t) {
  return t.split("/").map(encodeURIComponent).join("/");
}
function I(t, e, n) {
  return J(
    `/instances/${encodeURIComponent(t)}/artifacts/${K(e)}`,
    n
  );
}
var X = Object.defineProperty, Q = Object.getOwnPropertyDescriptor, U = (t) => {
  throw TypeError(t);
}, p = (t, e, n, o) => {
  for (var r = o > 1 ? void 0 : o ? Q(e, n) : e, i = t.length - 1, a; i >= 0; i--)
    (a = t[i]) && (r = (o ? a(e, n, r) : a(r)) || r);
  return o && r && X(e, n, r), r;
}, O = (t, e, n) => e.has(t) || U("Cannot " + n), E = (t, e, n) => (O(t, e, "read from private field"), n ? n.call(t) : e.get(t)), Y = (t, e, n) => e.has(t) ? U("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, n), Z = (t, e, n, o) => (O(t, e, "write to private field"), e.set(t, n), n), m;
let l = class extends A {
  constructor() {
    super(), Y(this, m), this.instanceId = "", this.artifactPath = "", this.open = !1, this._content = null, this._loading = !1, this._error = !1, this._downloadError = !1, this._fetchGeneration = 0, this.consumeContext(D, (t) => {
      Z(this, m, t);
    });
  }
  willUpdate(t) {
    const e = t.has("open") && this.open, n = t.has("artifactPath") && this.open && this.artifactPath;
    (e || n) && this._fetchArtifact();
  }
  async _fetchArtifact() {
    if (!this.instanceId || !this.artifactPath) return;
    const t = ++this._fetchGeneration;
    this._loading = !0, this._error = !1, this._content = null;
    try {
      const e = await E(this, m)?.getLatestToken(), n = await I(this.instanceId, this.artifactPath, e);
      if (t !== this._fetchGeneration) return;
      this._content = n;
    } catch {
      if (t !== this._fetchGeneration) return;
      this._error = !0;
    } finally {
      t === this._fetchGeneration && (this._loading = !1);
    }
  }
  _getFilename() {
    const t = this.artifactPath.split("/");
    return t[t.length - 1];
  }
  _onClose() {
    this.dispatchEvent(
      new CustomEvent("popover-closed", {
        bubbles: !0,
        composed: !0
      })
    );
  }
  _onDialogClose() {
    this._onClose();
  }
  async _onDownload() {
    this._downloadError = !1;
    try {
      let t;
      if (this._content !== null)
        t = this._content;
      else {
        const r = await E(this, m)?.getLatestToken();
        t = await I(this.instanceId, this.artifactPath, r);
      }
      const e = new Blob([t], { type: "text/plain" }), n = URL.createObjectURL(e), o = document.createElement("a");
      o.href = n, o.download = this._getFilename(), o.click(), URL.revokeObjectURL(n);
    } catch {
      this._downloadError = !0;
    }
  }
  _onRetry() {
    this._fetchArtifact();
  }
  render() {
    return this.open ? c`
      <uui-dialog @close=${this._onDialogClose}>
        <uui-dialog-layout headline=${this._getFilename()}>
          <div class="dialog-body">
            ${this._loading ? c`<div class="loader-wrapper"><uui-loader></uui-loader></div>` : this._error ? c`
                    <div class="error-state">
                      <p>Could not load artifact.</p>
                      <a href="#" class="retry-link" @click=${(t) => {
      t.preventDefault(), this._onRetry();
    }}>Retry</a>
                    </div>
                  ` : c`
                    <uui-scroll-container class="scroll-container">
                      <agentrun-markdown-renderer .content=${this._content ?? ""}></agentrun-markdown-renderer>
                    </uui-scroll-container>
                  `}
          </div>
          <div slot="actions">
            ${this._downloadError ? c`<span class="download-error">Download failed.</span>` : k}
            <uui-button label="Download" look="secondary" @click=${this._onDownload}>Download</uui-button>
            <uui-button label="Close" look="primary" @click=${this._onClose}>Close</uui-button>
          </div>
        </uui-dialog-layout>
      </uui-dialog>
    ` : k;
  }
};
m = /* @__PURE__ */ new WeakMap();
l.styles = C`
    :host {
      position: fixed;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      z-index: 1000;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(0, 0, 0, 0.4);
    }

    :host(:not([open])) {
      display: none;
    }

    uui-dialog {
      width: min(80vw, 800px);
      height: min(80vh, 600px);
      display: flex;
      flex-direction: column;
      background: var(--uui-color-surface);
      border-radius: var(--uui-border-radius);
      box-shadow: var(--uui-shadow-depth-5, 0 12px 40px rgba(0, 0, 0, 0.3));
    }

    uui-dialog-layout {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .dialog-body {
      flex: 1;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      padding: var(--uui-size-space-5);
    }

    .scroll-container {
      flex: 1;
      overflow-y: auto;
    }

    .loader-wrapper {
      display: flex;
      align-items: center;
      justify-content: center;
      flex: 1;
    }

    .error-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      flex: 1;
      color: var(--uui-color-text-alt);
    }

    .error-state p {
      margin: 0 0 var(--uui-size-space-3);
    }

    .retry-link {
      color: var(--uui-color-interactive);
      cursor: pointer;
      text-decoration: none;
    }

    .retry-link:hover {
      text-decoration: underline;
    }

    div[slot="actions"] {
      display: flex;
      gap: var(--uui-size-space-3);
      justify-content: flex-end;
      align-items: center;
    }

    .download-error {
      color: var(--uui-color-danger);
      font-size: var(--uui-type-small-size);
      margin-right: auto;
    }
  `;
p([
  g({ type: String })
], l.prototype, "instanceId", 2);
p([
  g({ type: String })
], l.prototype, "artifactPath", 2);
p([
  g({ type: Boolean })
], l.prototype, "open", 2);
p([
  _()
], l.prototype, "_content", 2);
p([
  _()
], l.prototype, "_loading", 2);
p([
  _()
], l.prototype, "_error", 2);
p([
  _()
], l.prototype, "_downloadError", 2);
l = p([
  z("agentrun-artifact-popover")
], l);
export {
  it as a,
  ut as b,
  at as c,
  ct as d,
  F as e,
  st as f,
  rt as g,
  ft as h,
  lt as i,
  ot as j,
  dt as m,
  pt as r,
  N as s
};
//# sourceMappingURL=index-YzWDK3IS.js.map
