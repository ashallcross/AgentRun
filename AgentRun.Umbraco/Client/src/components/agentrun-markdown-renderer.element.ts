import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  property,
} from "@umbraco-cms/backoffice/external/lit";
import { sanitiseMarkdown } from "../utils/markdown-sanitiser.js";

@customElement("agentrun-markdown-renderer")
export class AgentRunMarkdownRendererElement extends UmbLitElement {
  @property({ type: String })
  content = "";

  render() {
    const sanitised = sanitiseMarkdown(this.content);
    return html`<div class="markdown-body" .innerHTML=${sanitised}></div>`;
  }

  static styles = css`
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
}

declare global {
  interface HTMLElementTagNameMap {
    "agentrun-markdown-renderer": AgentRunMarkdownRendererElement;
  }
}
