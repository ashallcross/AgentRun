import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  property,
  state,
  nothing,
} from "@umbraco-cms/backoffice/external/lit";
import type { ChatMessage } from "../api/types.js";
import "./shallai-chat-message.element.js";

@customElement("shallai-chat-panel")
export class ShallaiChatPanelElement extends UmbLitElement {
  @property({ attribute: false })
  messages: ChatMessage[] = [];

  @property({ type: Boolean, attribute: "is-streaming" })
  isStreaming = false;

  @property({ type: Boolean, attribute: "input-enabled" })
  inputEnabled = false;

  @property({ type: String, attribute: "input-placeholder" })
  inputPlaceholder = "Click 'Start' to begin the workflow.";

  @state()
  private _inputValue = "";

  @state()
  private _autoScrollPaused = false;

  @state()
  private _hasNewMessages = false;

  @state()
  private _wasStreaming = false;

  static styles = css`
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

  private _onScroll(e: Event): void {
    const container = e.target as HTMLElement;
    if (!container) return;
    const { scrollTop, scrollHeight, clientHeight } = container;
    const isNearBottom = scrollHeight - scrollTop - clientHeight < 50;

    if (isNearBottom) {
      this._autoScrollPaused = false;
      this._hasNewMessages = false;
    } else {
      this._autoScrollPaused = true;
    }
  }

  private _scrollToBottom(): void {
    const container = this.renderRoot.querySelector("uui-scroll-container");
    if (!container) return;
    (container as HTMLElement).scrollTop = (container as HTMLElement).scrollHeight;
    this._autoScrollPaused = false;
    this._hasNewMessages = false;
  }

  private _onInput(e: Event): void {
    this._inputValue = (e.target as HTMLTextAreaElement).value;
  }

  private _onKeydown(e: KeyboardEvent): void {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      this._onSend();
    }
  }

  private _onSend(): void {
    const msg = this._inputValue.trim();
    if (!msg || !this.inputEnabled) return;
    this._inputValue = "";
    this.dispatchEvent(new CustomEvent("send-message", {
      detail: { message: msg },
      bubbles: true,
      composed: true,
    }));
  }

  protected override updated(): void {
    // Track streaming→not-streaming transition for aria-live announcements
    if (this._wasStreaming && !this.isStreaming) {
      this._wasStreaming = false;
    } else if (this.isStreaming) {
      this._wasStreaming = true;
    }

    const container = this.renderRoot.querySelector("uui-scroll-container");
    if (!container) return;

    const el = container as HTMLElement;
    const isNearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 50;

    if (isNearBottom || !this._autoScrollPaused) {
      el.scrollTop = el.scrollHeight;
    } else if (this.isStreaming) {
      this._hasNewMessages = true;
    }
  }

  render() {
    if (this.messages.length === 0) {
      return html`<div class="empty-state">Send a message to begin.</div>`;
    }

    const lastIndex = this.messages.length - 1;

    return html`
      <uui-scroll-container @scroll=${this._onScroll}>
        <div class="message-list" role="log" aria-live="polite">
          ${this.messages.map(
            (msg, i) => html`
              <shallai-chat-message
                .role=${msg.role}
                .content=${msg.content}
                .timestamp=${msg.timestamp}
                .toolCalls=${msg.toolCalls ?? []}
                ?is-streaming=${i === lastIndex &&
                msg.role === "agent" &&
                this.isStreaming}
              ></shallai-chat-message>
            `,
          )}
        </div>
      </uui-scroll-container>

      ${this._autoScrollPaused && this._hasNewMessages
        ? html`
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
          `
        : nothing}

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
}

export default ShallaiChatPanelElement;
