import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  property,
  nothing,
} from "@umbraco-cms/backoffice/external/lit";
import type { ToolCallData } from "../api/types.js";
import { sanitiseMarkdown } from "../utils/markdown-sanitiser.js";
import "./shallai-tool-call.element.js";

@customElement("shallai-chat-message")
export class ShallaiChatMessageElement extends UmbLitElement {
  @property({ type: String })
  role: "agent" | "system" | "user" = "agent";

  @property({ type: String })
  content = "";

  @property({ type: String })
  timestamp = "";

  @property({ type: Boolean, attribute: "is-streaming" })
  isStreaming = false;

  @property({ attribute: false })
  toolCalls: ToolCallData[] = [];

  static styles = css`
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

  private _formatTime(iso: string): string {
    if (!iso) return "";
    try {
      const d = new Date(iso);
      return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    } catch {
      return "";
    }
  }

  private _renderMarkdown(content: string): unknown {
    const sanitised = sanitiseMarkdown(content);
    const div = document.createElement("div");
    div.className = "agent-content";
    div.innerHTML = sanitised;
    return html`${this._templateFromNode(div)}`;
  }

  private _templateFromNode(node: Node): unknown {
    // Create a container and clone content into it for rendering
    const container = document.createElement("div");
    container.className = "agent-content";
    container.innerHTML = (node as HTMLElement).innerHTML;
    // We render the container directly using Lit's html with innerHTML set
    // by returning the node itself for adoption into the shadow DOM
    return html`<div class="agent-content" .innerHTML=${(node as HTMLElement).innerHTML}></div>`;
  }

  render() {
    if (this.role === "system") {
      return html`
        <div
          class="system-message"
          role="listitem"
          aria-label="System notification"
        >
          ${this.content}
        </div>
      `;
    }

    if (this.role === "user") {
      return html`
        <div class="user-message" role="listitem" aria-label="Your message">
          <span class="user-content">${this.content}</span>
          ${this.timestamp
            ? html`<span class="user-timestamp">${this._formatTime(this.timestamp)}</span>`
            : nothing}
        </div>
      `;
    }

    // Agent message
    const agentContent = this.isStreaming
      ? html`<span class="agent-content" style="white-space:pre-wrap">${this.content}<span class="cursor">▋</span></span>`
      : this._renderMarkdown(this.content);

    return html`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        ${agentContent}
        ${this.toolCalls.map(tc => html`
          <shallai-tool-call
            .toolName=${tc.toolName}
            .toolCallId=${tc.toolCallId}
            .summary=${tc.summary}
            .arguments=${tc.arguments}
            .result=${tc.result}
            .status=${tc.status}
          ></shallai-tool-call>
        `)}
        ${this.timestamp
          ? html`<span class="agent-timestamp"
              >${this._formatTime(this.timestamp)}</span
            >`
          : nothing}
      </div>
    `;
  }
}

export default ShallaiChatMessageElement;
