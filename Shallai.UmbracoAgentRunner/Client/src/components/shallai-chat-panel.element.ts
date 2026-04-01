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

  @state()
  private _autoScrollPaused = false;

  @state()
  private _hasNewMessages = false;

  @state()
  private _wasStreaming = false;

  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      position: relative;
    }

    uui-scroll-container {
      flex: 1;
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
      position: absolute;
      bottom: var(--uui-size-space-4);
      left: 50%;
      transform: translateX(-50%);
      z-index: 10;
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
      return html`<div class="empty-state">Start the workflow to begin</div>`;
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

      <div class="sr-only" aria-live="assertive">
        ${this.isStreaming ? "Agent is responding..." : ""}
        ${!this.isStreaming && this._wasStreaming ? "Response complete" : ""}
      </div>
    `;
  }
}

export default ShallaiChatPanelElement;
