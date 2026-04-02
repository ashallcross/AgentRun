import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  property,
  state,
  nothing,
} from "@umbraco-cms/backoffice/external/lit";

@customElement("agentrun-tool-call")
export class AgentRunToolCallElement extends UmbLitElement {
  @property({ type: String })
  toolName = "";

  @property({ type: String })
  toolCallId = "";

  @property({ type: String })
  summary = "";

  @property({ attribute: false })
  arguments: Record<string, unknown> | null = null;

  @property({ type: String })
  result: string | null = null;

  @property({ type: String })
  status: "running" | "complete" | "error" = "running";

  @state()
  private _expanded = false;

  static styles = css`
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

  private _toggle(): void {
    if (this.status === "running") return;
    this._expanded = !this._expanded;
  }

  private _onKeydown(e: KeyboardEvent): void {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      this._toggle();
    }
  }

  private _renderStatusIcon() {
    if (this.status === "running") {
      return html`<uui-loader></uui-loader>`;
    }
    if (this.status === "error") {
      return html`<uui-icon class="status-icon error" name="icon-alert"></uui-icon>`;
    }
    return html`<uui-icon class="status-icon complete" name="icon-check"></uui-icon>`;
  }

  private _renderBody() {
    if (!this._expanded) return nothing;

    return html`
      <div class="tool-call-body">
        ${this.arguments
          ? html`
              <div class="section-label">Arguments</div>
              <div class="mono-block">${JSON.stringify(this.arguments, null, 2)}</div>
            `
          : nothing}
        ${this.result != null
          ? html`
              <div class="section-label">Result</div>
              <div class="mono-block">${this.result}</div>
            `
          : nothing}
      </div>
    `;
  }

  render() {
    const canExpand = this.status !== "running";

    return html`
      <div
        class="tool-call ${this.status}"
        role="group"
        aria-label="Tool call: ${this.toolName}"
        aria-expanded=${canExpand ? String(this._expanded) : nothing}
        tabindex=${canExpand ? "0" : nothing}
        @click=${this._toggle}
        @keydown=${this._onKeydown}
      >
        <div class="tool-call-header">
          ${this._renderStatusIcon()}
          <span class="tool-name">${this.toolName}</span>
          <span class="tool-summary">${this.summary ? `— ${this.summary}` : ""}</span>
          ${canExpand
            ? html`<uui-icon class="chevron ${this._expanded ? "open" : ""}" name="icon-navigation-down"></uui-icon>`
            : nothing}
        </div>
        ${this._renderBody()}
      </div>
    `;
  }
}

export default AgentRunToolCallElement;
