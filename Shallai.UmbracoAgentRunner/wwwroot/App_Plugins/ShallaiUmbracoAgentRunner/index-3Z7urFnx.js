import { UmbLitElement as C } from "@umbraco-cms/backoffice/lit-element";
import { css as A, property as g, customElement as z, html as l, state as w, nothing as S } from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT as D } from "@umbraco-cms/backoffice/auth";
const nt = [
  {
    type: "section",
    alias: "Shallai.UmbracoAgentRunner.Section",
    name: "Agent Workflows Section",
    meta: {
      label: "Agent Workflows",
      pathname: "agent-workflows"
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionUserPermission",
        match: "Shallai.UmbracoAgentRunner.Section"
      }
    ]
  },
  {
    type: "menu",
    alias: "Shallai.UmbracoAgentRunner.Menu",
    name: "Agent Workflows Menu"
  },
  {
    type: "sectionSidebarApp",
    kind: "menu",
    alias: "Shallai.UmbracoAgentRunner.SectionSidebarApp.Menu",
    name: "Agent Workflows Sidebar Menu",
    meta: {
      label: "Agent Workflows",
      menu: "Shallai.UmbracoAgentRunner.Menu"
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "Shallai.UmbracoAgentRunner.Section"
      }
    ]
  },
  {
    type: "sectionView",
    alias: "Shallai.UmbracoAgentRunner.SectionView.Dashboard",
    name: "Agent Workflows Dashboard View",
    element: () => import("./shallai-dashboard.element-CF6xY6Sb.js"),
    meta: {
      label: "Overview",
      pathname: "overview",
      icon: "icon-dashboard"
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "Shallai.UmbracoAgentRunner.Section"
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
  const e = [], n = t.replace(/```[\s\S]*?```/g, (r) => {
    const u = e.length, d = r.replace(/^```[^\n]*\n?/, "").replace(/\n?```$/, "");
    return e.push(`<pre><code>${x(d)}</code></pre>`), `\0CB${u}\0`;
  }).split(`
`), a = [];
  let i = 0;
  for (; i < n.length; ) {
    const r = n[i], u = r.match(/^\x00CB(\d+)\x00$/);
    if (u) {
      a.push(e[parseInt(u[1], 10)]), i++;
      continue;
    }
    if (/^(---+|\*\*\*+|___+)\s*$/.test(r)) {
      a.push("<hr>"), i++;
      continue;
    }
    const d = r.match(/^(#{1,6})\s+(.+)$/);
    if (d) {
      const s = d[1].length;
      a.push(`<h${s}>${h(d[2])}</h${s}>`), i++;
      continue;
    }
    if (r.startsWith("> ")) {
      const s = [];
      for (; i < n.length && n[i].startsWith("> "); )
        s.push(n[i].slice(2)), i++;
      a.push(`<blockquote>${h(s.join("<br>"))}</blockquote>`);
      continue;
    }
    if (/^[-*]\s+/.test(r)) {
      const s = [];
      for (; i < n.length && /^[-*]\s+/.test(n[i]); )
        s.push(n[i].replace(/^[-*]\s+/, "")), i++;
      a.push("<ul>" + s.map(($) => `<li>${h($)}</li>`).join("") + "</ul>");
      continue;
    }
    if (/^\d+\.\s+/.test(r)) {
      const s = [];
      for (; i < n.length && /^\d+\.\s+/.test(n[i]); )
        s.push(n[i].replace(/^\d+\.\s+/, "")), i++;
      a.push("<ol>" + s.map(($) => `<li>${h($)}</li>`).join("") + "</ol>");
      continue;
    }
    if (r.trim() === "") {
      i++;
      continue;
    }
    const y = [];
    for (; i < n.length && n[i].trim() !== "" && !/^(#{1,6}\s|[-*]\s|>\s|\d+\.\s|---+|___+|\*\*\*+|\x00CB)/.test(n[i]); )
      y.push(n[i]), i++;
    y.length > 0 && a.push(`<p>${h(y.join("<br>"))}</p>`);
  }
  return a.join("");
}
function h(t) {
  return t = t.replace(/`([^`]+)`/g, (e, o) => `<code>${x(o)}</code>`), t = t.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>"), t = t.replace(/__(.+?)__/g, "<strong>$1</strong>"), t = t.replace(/\*(.+?)\*/g, "<em>$1</em>"), t = t.replace(/_(.+?)_/g, "<em>$1</em>"), t = t.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (e, o, n) => /^https?:\/\//i.test(n) ? `<a href="${M(n)}" target="_blank" rel="noopener">${x(o)}</a>` : o), t;
}
function x(t) {
  return t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}
function M(t) {
  return t.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/'/g, "&#39;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
function B(t) {
  const e = document.createElement("template");
  e.innerHTML = t, E(e.content);
  const o = document.createElement("div");
  return o.appendChild(e.content.cloneNode(!0)), o.innerHTML;
}
function E(t) {
  const e = Array.from(t.childNodes);
  for (const o of e)
    if (o.nodeType === Node.ELEMENT_NODE) {
      const n = o, a = n.tagName.toLowerCase();
      if (j.has(a)) {
        const i = Array.from(n.attributes);
        for (const r of i)
          a === "a" && (r.name === "href" || r.name === "target" || r.name === "rel") || n.removeAttribute(r.name);
        if (a === "a") {
          const r = n.getAttribute("href") ?? "";
          /^https?:\/\//i.test(r) || n.removeAttribute("href"), n.setAttribute("target", "_blank"), n.setAttribute("rel", "noopener");
        }
        E(n);
      } else {
        const i = document.createTextNode(n.textContent ?? "");
        t.replaceChild(i, o);
      }
    } else o.nodeType !== Node.TEXT_NODE && t.removeChild(o);
}
var W = Object.defineProperty, q = Object.getOwnPropertyDescriptor, T = (t, e, o, n) => {
  for (var a = n > 1 ? void 0 : n ? q(e, o) : e, i = t.length - 1, r; i >= 0; i--)
    (r = t[i]) && (a = (n ? r(e, o, a) : r(a)) || a);
  return n && a && W(e, o, a), a;
};
let v = class extends C {
  constructor() {
    super(...arguments), this.content = "";
  }
  render() {
    const t = N(this.content);
    return l`<div class="markdown-body" .innerHTML=${t}></div>`;
  }
};
v.styles = A`
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
T([
  g({ type: String })
], v.prototype, "content", 2);
v = T([
  z("shallai-markdown-renderer")
], v);
var G = Object.defineProperty, H = Object.getOwnPropertyDescriptor, P = (t, e, o, n) => {
  for (var a = n > 1 ? void 0 : n ? H(e, o) : e, i = t.length - 1, r; i >= 0; i--)
    (r = t[i]) && (a = (n ? r(e, o, a) : r(a)) || a);
  return n && a && G(e, o, a), a;
};
let b = class extends C {
  constructor() {
    super(...arguments), this.steps = [];
  }
  _deriveArtifacts() {
    const t = [];
    for (const e of this.steps)
      if (!(!e.writesTo || e.writesTo.length === 0))
        for (const o of e.writesTo)
          t.push({ path: o, stepName: e.name, stepStatus: e.status });
    return t;
  }
  _getFilename(t) {
    const e = t.split("/");
    return e[e.length - 1];
  }
  _onArtifactClick(t) {
    t.stepStatus === "Complete" && this.dispatchEvent(
      new CustomEvent("artifact-selected", {
        detail: { path: t.path, stepName: t.stepName },
        bubbles: !0,
        composed: !0
      })
    );
  }
  _onKeyDown(t, e) {
    e.stepStatus === "Complete" && (t.key === "Enter" || t.key === " ") && (t.preventDefault(), this._onArtifactClick(e));
  }
  render() {
    const t = this._deriveArtifacts();
    return l`
      <h4 class="heading">Artifacts</h4>
      ${t.length === 0 ? l`<p class="empty-state">No artifacts yet.</p>` : l`
            <div class="artifact-list" role="list">
              ${t.map(
      (e) => l`
                  <div
                    class="artifact-item ${e.stepStatus !== "Complete" ? "disabled" : ""}"
                    role="listitem"
                  >
                    <uui-icon class="file-icon" name="icon-document"></uui-icon>
                    <div class="artifact-text">
                      <span
                        class="artifact-name"
                        role="${e.stepStatus === "Complete" ? "button" : "text"}"
                        tabindex="${e.stepStatus === "Complete" ? "0" : "-1"}"
                        aria-label="${this._getFilename(e.path)} from ${e.stepName}"
                        aria-disabled="${e.stepStatus !== "Complete"}"
                        @click=${() => this._onArtifactClick(e)}
                        @keydown=${(o) => this._onKeyDown(o, e)}
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
b.styles = A`
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

    .artifact-item.disabled {
      color: var(--uui-color-disabled);
    }

    .file-icon {
      flex-shrink: 0;
      margin-top: 2px;
      color: var(--uui-color-text-alt);
    }

    .artifact-item.disabled .file-icon {
      color: var(--uui-color-disabled);
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

    .artifact-item.disabled .artifact-name {
      color: var(--uui-color-disabled);
      cursor: default;
    }

    .artifact-step {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    .artifact-item.disabled .artifact-step {
      color: var(--uui-color-disabled);
    }
  `;
P([
  g({ attribute: !1 })
], b.prototype, "steps", 2);
b = P([
  z("shallai-artifact-list")
], b);
function F(t, e) {
  if (!e) return t;
  if (typeof e.path == "string") {
    const o = e.path.split("/");
    return o[o.length - 1] || t;
  }
  if (typeof e.url == "string")
    return e.url.length > 60 ? e.url.slice(0, 60) + "…" : e.url;
  for (const o of Object.values(e))
    if (typeof o == "string") return o;
  return t;
}
const f = "/umbraco/api/shallai";
async function _(t, e) {
  const o = { Accept: "application/json" };
  e && (o.Authorization = `Bearer ${e}`);
  const n = await fetch(`${f}${t}`, { headers: o });
  if (!n.ok)
    throw new Error(`API error: ${n.status} ${n.statusText}`);
  return n.json();
}
async function J(t, e) {
  const o = {};
  e && (o.Authorization = `Bearer ${e}`);
  const n = await fetch(`${f}${t}`, { headers: o, credentials: "same-origin" });
  if (!n.ok)
    throw new Error(`API error: ${n.status} ${n.statusText}`);
  return n.text();
}
async function U(t, e, o) {
  const n = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  o && (n.Authorization = `Bearer ${o}`);
  const a = await fetch(`${f}${t}`, {
    method: "POST",
    headers: n,
    body: JSON.stringify(e)
  });
  if (!a.ok)
    throw new Error(`API error: ${a.status} ${a.statusText}`);
  return a.json();
}
function at(t) {
  return _("/workflows", t);
}
function it(t, e) {
  return _(`/instances?workflowAlias=${encodeURIComponent(t)}`, e);
}
function rt(t, e) {
  return U("/instances", { workflowAlias: t }, e);
}
function st(t, e) {
  return _(`/instances/${encodeURIComponent(t)}`, e);
}
function lt(t, e) {
  return U(`/instances/${encodeURIComponent(t)}/cancel`, {}, e);
}
async function ct(t, e, o) {
  const n = { "Content-Type": "application/json" };
  o && (n.Authorization = `Bearer ${o}`);
  const a = await fetch(
    `${f}/instances/${encodeURIComponent(t)}/message`,
    { method: "POST", headers: n, body: JSON.stringify({ message: e }) }
  );
  if (!a.ok)
    throw new Error(`API error: ${a.status} ${a.statusText}`);
}
async function ut(t, e) {
  const o = {};
  return e && (o.Authorization = `Bearer ${e}`), fetch(`${f}/instances/${encodeURIComponent(t)}/start`, {
    method: "POST",
    headers: o
  });
}
async function pt(t, e) {
  const o = {};
  return e && (o.Authorization = `Bearer ${e}`), fetch(`${f}/instances/${encodeURIComponent(t)}/retry`, {
    method: "POST",
    headers: o
  });
}
function dt(t, e, o) {
  return _(
    `/instances/${encodeURIComponent(t)}/conversation/${encodeURIComponent(e)}`,
    o
  );
}
function ft(t) {
  const e = [];
  for (const o of t)
    if (o.role === "assistant" && o.content != null && !o.toolCallId)
      e.push({
        role: "agent",
        content: o.content,
        timestamp: o.timestamp
      });
    else if (o.role === "assistant" && o.toolCallId) {
      let n = null;
      if (o.toolArguments)
        try {
          n = JSON.parse(o.toolArguments);
        } catch {
          n = null;
        }
      const a = {
        toolCallId: o.toolCallId,
        toolName: o.toolName ?? "unknown",
        summary: F(o.toolName ?? "unknown", n),
        arguments: n,
        result: null,
        status: "complete"
      }, i = V(e);
      i ? i.toolCalls = [...i.toolCalls ?? [], a] : e.push({
        role: "agent",
        content: "",
        timestamp: o.timestamp,
        toolCalls: [a]
      });
    } else if (o.role === "tool" && o.toolCallId) {
      const n = o.toolResult ?? null, a = typeof n == "string" && n.startsWith("Tool '") && (n.includes("error") || n.includes("failed"));
      for (let i = e.length - 1; i >= 0; i--) {
        const u = e[i].toolCalls?.find((d) => d.toolCallId === o.toolCallId);
        if (u) {
          u.result = n, a && (u.status = "error");
          break;
        }
      }
    } else o.role === "user" && o.content != null ? e.push({
      role: "user",
      content: o.content,
      timestamp: o.timestamp
    }) : o.role === "system" && o.content != null && e.push({
      role: "system",
      content: o.content,
      timestamp: o.timestamp
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
function k(t, e, o) {
  return J(
    `/instances/${encodeURIComponent(t)}/artifacts/${K(e)}`,
    o
  );
}
var X = Object.defineProperty, Q = Object.getOwnPropertyDescriptor, O = (t) => {
  throw TypeError(t);
}, p = (t, e, o, n) => {
  for (var a = n > 1 ? void 0 : n ? Q(e, o) : e, i = t.length - 1, r; i >= 0; i--)
    (r = t[i]) && (a = (n ? r(e, o, a) : r(a)) || a);
  return n && a && X(e, o, a), a;
}, R = (t, e, o) => e.has(t) || O("Cannot " + o), I = (t, e, o) => (R(t, e, "read from private field"), o ? o.call(t) : e.get(t)), Y = (t, e, o) => e.has(t) ? O("Cannot add the same private member more than once") : e instanceof WeakSet ? e.add(t) : e.set(t, o), Z = (t, e, o, n) => (R(t, e, "write to private field"), e.set(t, o), o), m;
let c = class extends C {
  constructor() {
    super(), Y(this, m), this.instanceId = "", this.artifactPath = "", this.open = !1, this._content = null, this._loading = !1, this._error = !1, this._downloadError = !1, this._fetchGeneration = 0, this.consumeContext(D, (t) => {
      Z(this, m, t);
    });
  }
  willUpdate(t) {
    const e = t.has("open") && this.open, o = t.has("artifactPath") && this.open && this.artifactPath;
    (e || o) && this._fetchArtifact();
  }
  async _fetchArtifact() {
    if (!this.instanceId || !this.artifactPath) return;
    const t = ++this._fetchGeneration;
    this._loading = !0, this._error = !1, this._content = null;
    try {
      const e = await I(this, m)?.getLatestToken(), o = await k(this.instanceId, this.artifactPath, e);
      if (t !== this._fetchGeneration) return;
      this._content = o;
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
        const a = await I(this, m)?.getLatestToken();
        t = await k(this.instanceId, this.artifactPath, a);
      }
      const e = new Blob([t], { type: "text/plain" }), o = URL.createObjectURL(e), n = document.createElement("a");
      n.href = o, n.download = this._getFilename(), n.click(), URL.revokeObjectURL(o);
    } catch {
      this._downloadError = !0;
    }
  }
  _onRetry() {
    this._fetchArtifact();
  }
  render() {
    return this.open ? l`
      <uui-dialog @close=${this._onDialogClose}>
        <uui-dialog-layout headline=${this._getFilename()}>
          <div class="dialog-body">
            ${this._loading ? l`<div class="loader-wrapper"><uui-loader></uui-loader></div>` : this._error ? l`
                    <div class="error-state">
                      <p>Could not load artifact.</p>
                      <a href="#" class="retry-link" @click=${(t) => {
      t.preventDefault(), this._onRetry();
    }}>Retry</a>
                    </div>
                  ` : l`
                    <uui-scroll-container class="scroll-container">
                      <shallai-markdown-renderer .content=${this._content ?? ""}></shallai-markdown-renderer>
                    </uui-scroll-container>
                  `}
          </div>
          <div slot="actions">
            ${this._downloadError ? l`<span class="download-error">Download failed.</span>` : S}
            <uui-button label="Download" look="secondary" @click=${this._onDownload}>Download</uui-button>
            <uui-button label="Close" look="primary" @click=${this._onClose}>Close</uui-button>
          </div>
        </uui-dialog-layout>
      </uui-dialog>
    ` : S;
  }
};
m = /* @__PURE__ */ new WeakMap();
c.styles = A`
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
], c.prototype, "instanceId", 2);
p([
  g({ type: String })
], c.prototype, "artifactPath", 2);
p([
  g({ type: Boolean })
], c.prototype, "open", 2);
p([
  w()
], c.prototype, "_content", 2);
p([
  w()
], c.prototype, "_loading", 2);
p([
  w()
], c.prototype, "_error", 2);
p([
  w()
], c.prototype, "_downloadError", 2);
c = p([
  z("shallai-artifact-popover")
], c);
export {
  it as a,
  st as b,
  rt as c,
  dt as d,
  ut as e,
  F as f,
  at as g,
  ct as h,
  lt as i,
  nt as j,
  ft as m,
  pt as r,
  N as s
};
//# sourceMappingURL=index-3Z7urFnx.js.map
