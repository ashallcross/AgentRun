import { UmbLitElement as b } from "@umbraco-cms/backoffice/lit-element";
import { css as v, property as z, customElement as w, html as A } from "@umbraco-cms/backoffice/external/lit";
const E = [
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
    element: () => import("./shallai-dashboard.element-Dfy0CuM6.js"),
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
], S = /* @__PURE__ */ new Set([
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
function _(t) {
  const o = $(t);
  return y(o);
}
function $(t) {
  const o = [], e = t.replace(/```[\s\S]*?```/g, (r) => {
    const u = o.length, c = r.replace(/^```[^\n]*\n?/, "").replace(/\n?```$/, "");
    return o.push(`<pre><code>${d(c)}</code></pre>`), `\0CB${u}\0`;
  }).split(`
`), i = [];
  let n = 0;
  for (; n < e.length; ) {
    const r = e[n], u = r.match(/^\x00CB(\d+)\x00$/);
    if (u) {
      i.push(o[parseInt(u[1], 10)]), n++;
      continue;
    }
    if (/^(---+|\*\*\*+|___+)\s*$/.test(r)) {
      i.push("<hr>"), n++;
      continue;
    }
    const c = r.match(/^(#{1,6})\s+(.+)$/);
    if (c) {
      const s = c[1].length;
      i.push(`<h${s}>${l(c[2])}</h${s}>`), n++;
      continue;
    }
    if (r.startsWith("> ")) {
      const s = [];
      for (; n < e.length && e[n].startsWith("> "); )
        s.push(e[n].slice(2)), n++;
      i.push(`<blockquote>${l(s.join("<br>"))}</blockquote>`);
      continue;
    }
    if (/^[-*]\s+/.test(r)) {
      const s = [];
      for (; n < e.length && /^[-*]\s+/.test(e[n]); )
        s.push(e[n].replace(/^[-*]\s+/, "")), n++;
      i.push("<ul>" + s.map((h) => `<li>${l(h)}</li>`).join("") + "</ul>");
      continue;
    }
    if (/^\d+\.\s+/.test(r)) {
      const s = [];
      for (; n < e.length && /^\d+\.\s+/.test(e[n]); )
        s.push(e[n].replace(/^\d+\.\s+/, "")), n++;
      i.push("<ol>" + s.map((h) => `<li>${l(h)}</li>`).join("") + "</ol>");
      continue;
    }
    if (r.trim() === "") {
      n++;
      continue;
    }
    const m = [];
    for (; n < e.length && e[n].trim() !== "" && !/^(#{1,6}\s|[-*]\s|>\s|\d+\.\s|---+|___+|\*\*\*+|\x00CB)/.test(e[n]); )
      m.push(e[n]), n++;
    m.length > 0 && i.push(`<p>${l(m.join("<br>"))}</p>`);
  }
  return i.join("");
}
function l(t) {
  return t = t.replace(/`([^`]+)`/g, (o, a) => `<code>${d(a)}</code>`), t = t.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>"), t = t.replace(/__(.+?)__/g, "<strong>$1</strong>"), t = t.replace(/\*(.+?)\*/g, "<em>$1</em>"), t = t.replace(/_(.+?)_/g, "<em>$1</em>"), t = t.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (o, a, e) => /^https?:\/\//i.test(e) ? `<a href="${k(e)}" target="_blank" rel="noopener">${d(a)}</a>` : a), t;
}
function d(t) {
  return t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}
function k(t) {
  return t.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/'/g, "&#39;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
function y(t) {
  const o = document.createElement("template");
  o.innerHTML = t, g(o.content);
  const a = document.createElement("div");
  return a.appendChild(o.content.cloneNode(!0)), a.innerHTML;
}
function g(t) {
  const o = Array.from(t.childNodes);
  for (const a of o)
    if (a.nodeType === Node.ELEMENT_NODE) {
      const e = a, i = e.tagName.toLowerCase();
      if (S.has(i)) {
        const n = Array.from(e.attributes);
        for (const r of n)
          i === "a" && (r.name === "href" || r.name === "target" || r.name === "rel") || e.removeAttribute(r.name);
        if (i === "a") {
          const r = e.getAttribute("href") ?? "";
          /^https?:\/\//i.test(r) || e.removeAttribute("href"), e.setAttribute("target", "_blank"), e.setAttribute("rel", "noopener");
        }
        g(e);
      } else {
        const n = document.createTextNode(e.textContent ?? "");
        t.replaceChild(n, a);
      }
    } else a.nodeType !== Node.TEXT_NODE && t.removeChild(a);
}
var x = Object.defineProperty, M = Object.getOwnPropertyDescriptor, f = (t, o, a, e) => {
  for (var i = e > 1 ? void 0 : e ? M(o, a) : o, n = t.length - 1, r; n >= 0; n--)
    (r = t[n]) && (i = (e ? r(o, a, i) : r(i)) || i);
  return e && i && x(o, a, i), i;
};
let p = class extends b {
  constructor() {
    super(...arguments), this.content = "";
  }
  render() {
    const t = _(this.content);
    return A`<div class="markdown-body" .innerHTML=${t}></div>`;
  }
};
p.styles = v`
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
f([
  z({ type: String })
], p.prototype, "content", 2);
p = f([
  w("shallai-markdown-renderer")
], p);
export {
  E as m,
  _ as s
};
//# sourceMappingURL=index-6tkhMXvN.js.map
