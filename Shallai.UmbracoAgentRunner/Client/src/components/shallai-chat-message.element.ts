import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  property,
  nothing,
} from "@umbraco-cms/backoffice/external/lit";
import type { ToolCallData } from "../api/types.js";
import "./shallai-tool-call.element.js";

@customElement("shallai-chat-message")
export class ShallaiChatMessageElement extends UmbLitElement {
  @property({ type: String })
  role: "agent" | "system" = "agent";

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
      white-space: pre-wrap;
      word-break: break-word;
    }

    .agent-timestamp {
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

    return html`
      <div class="agent-message" role="listitem" aria-label="Agent message">
        <span class="agent-content"
          >${this.content}${this.isStreaming
            ? html`<span class="cursor">▋</span>`
            : nothing}</span
        >
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
