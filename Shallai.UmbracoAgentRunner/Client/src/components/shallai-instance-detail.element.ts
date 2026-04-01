import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  state,
  nothing,
} from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import { umbConfirmModal } from "@umbraco-cms/backoffice/modal";
import { getInstance, getInstances, cancelInstance, startInstance, getConversation, mapConversationToChat } from "../api/api-client.js";
import type {
  InstanceDetailResponse,
  StepDetailResponse,
  ChatMessage,
  ToolCallData,
} from "../api/types.js";
import "./shallai-chat-panel.element.js";
import {
  extractInstanceId,
  buildInstanceListPath,
  stepSubtitle,
  stepIconName,
} from "../utils/instance-detail-helpers.js";
import { extractToolSummary } from "../utils/tool-helpers.js";
import { numberAndSortInstances } from "../utils/instance-list-helpers.js";

@customElement("shallai-instance-detail")
export class ShallaiInstanceDetailElement extends UmbLitElement {
  #authContext?: typeof UMB_AUTH_CONTEXT.TYPE;

  @state()
  private _instance: InstanceDetailResponse | null = null;

  @state()
  private _loading = true;

  @state()
  private _error = false;

  @state()
  private _selectedStepId: string | null = null;

  @state()
  private _cancelling = false;

  @state()
  private _runNumber = 0;

  @state()
  private _streaming = false;

  @state()
  private _providerError = false;

  @state()
  private _chatMessages: ChatMessage[] = [];

  @state()
  private _streamingText = "";

  @state()
  private _viewingStepId: string | null = null;

  @state()
  private _historyMessages: ChatMessage[] = [];

  private _toolBatchOpen = false;

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    .header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-3);
      margin-bottom: var(--uui-size-layout-1);
    }

    .header h2 {
      flex: 1;
      margin: 0;
    }

    .detail-grid {
      display: grid;
      grid-template-columns: 240px 1fr;
      gap: var(--uui-size-layout-1);
      height: 100%;
    }

    .step-sidebar {
      border-right: 1px solid var(--uui-color-border);
      padding-right: var(--uui-size-layout-1);
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
  `;

  constructor() {
    super();
    this.consumeContext(UMB_AUTH_CONTEXT, (context) => {
      this.#authContext = context;
    });
  }

  connectedCallback(): void {
    super.connectedCallback();
    this._loading = true;
    this._error = false;
    this._loadData();
  }

  private async _loadData(): Promise<void> {
    try {
      const instanceId = extractInstanceId(window.location.pathname);
      const token = await this.#authContext?.getLatestToken();
      const instance = await getInstance(instanceId, token);
      this._instance = instance;

      const allInstances = await getInstances(instance.workflowAlias, token);
      const numbered = numberAndSortInstances(allInstances);
      const match = numbered.find((n) => n.id === instance.id);
      this._runNumber = match?.runNumber ?? 0;

      this._error = false;
    } catch {
      this._error = true;
    } finally {
      this._loading = false;
    }
  }

  private _navigateBack(): void {
    if (!this._instance) return;
    const path = buildInstanceListPath(
      window.location.pathname,
      this._instance.workflowAlias,
    );
    window.history.pushState({}, "", path);
  }

  private async _onStepClick(step: StepDetailResponse): Promise<void> {
    if (step.status === "Pending") return;
    this._selectedStepId = step.id;
    this.dispatchEvent(
      new CustomEvent("step-selected", {
        detail: { stepId: step.id, status: step.status },
        bubbles: true,
        composed: true,
      }),
    );

    // If clicking the active step during streaming, show live messages
    if (step.status === "Active") {
      this._viewingStepId = null;
      return;
    }

    // Load conversation history for completed/error steps
    this._viewingStepId = step.id;
    try {
      const token = await this.#authContext?.getLatestToken();
      const instanceId = this._instance!.id;
      const entries = await getConversation(instanceId, step.id, token);
      this._historyMessages = mapConversationToChat(entries);
    } catch {
      this._historyMessages = [];
    }
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    this._streaming = false;
  }

  private async _onStartClick(): Promise<void> {
    if (this._streaming || !this._instance) return;

    this._streaming = true;
    this._providerError = false;
    this._viewingStepId = null;
    this._streamingText = "";

    try {
      const token = await this.#authContext?.getLatestToken();
      const response = await startInstance(this._instance.id, token);

      if (!response.ok) {
        if (response.status === 400) {
          // Provider prerequisite error
          this._providerError = true;
          return;
        }
        // 409 or other error — reload data to get fresh state
        await this._loadData();
        return;
      }

      if (!response.body) return;

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        const events = buffer.split("\n\n");
        buffer = events.pop()!;
        for (const event of events) {
          const lines = event.split("\n");
          const eventType = lines.find((l) => l.startsWith("event:"))?.slice(7);
          const data = lines.find((l) => l.startsWith("data:"))?.slice(5);
          if (eventType && data) {
            this._handleSseEvent(eventType, JSON.parse(data));
          }
        }
      }
    } catch {
      this._finaliseStreamingMessage();
      this._chatMessages = [
        ...this._chatMessages,
        {
          role: "system",
          content: "Connection lost.",
          timestamp: new Date().toISOString(),
        },
      ];
    } finally {
      this._streaming = false;
      await this._loadData();
    }
  }

  private _finaliseStreamingMessage(): void {
    if (!this._streamingText) return;
    const lastIndex = this._chatMessages.length - 1;
    const last = this._chatMessages[lastIndex];
    if (last && last.role === "agent" && last.isStreaming) {
      this._chatMessages = [
        ...this._chatMessages.slice(0, lastIndex),
        { ...last, content: this._streamingText, isStreaming: false },
      ];
    }
    this._streamingText = "";
  }

  private _handleSseEvent(eventType: string, data: Record<string, unknown>): void {
    switch (eventType) {
      case "text.delta": {
        const content = data.content as string;
        if (!content) break;
        this._toolBatchOpen = false;
        this._streamingText += content;
        const lastIndex = this._chatMessages.length - 1;
        const last = this._chatMessages[lastIndex];
        if (last && last.role === "agent" && last.isStreaming) {
          this._chatMessages = [
            ...this._chatMessages.slice(0, lastIndex),
            { ...last, content: this._streamingText },
          ];
        } else {
          this._chatMessages = [
            ...this._chatMessages,
            {
              role: "agent",
              content: this._streamingText,
              timestamp: new Date().toISOString(),
              isStreaming: true,
            },
          ];
        }
        break;
      }
      case "tool.start": {
        this._finaliseStreamingMessage();
        const toolCallId = data.toolCallId as string;
        const toolName = data.toolName as string;
        const newToolCall: ToolCallData = {
          toolCallId,
          toolName,
          summary: toolName,
          arguments: null,
          result: null,
          status: "running",
        };
        const lastMsg = this._chatMessages[this._chatMessages.length - 1];
        if (this._toolBatchOpen && lastMsg && lastMsg.role === "agent") {
          // Same LLM turn — attach to existing agent message
          const idx = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, idx),
            { ...lastMsg, toolCalls: [...(lastMsg.toolCalls ?? []), newToolCall] },
            ...this._chatMessages.slice(idx + 1),
          ];
        } else if (lastMsg && lastMsg.role === "agent" && !lastMsg.toolCalls?.length) {
          // Agent message with text only — attach first tool call to it
          const idx = this._chatMessages.length - 1;
          this._chatMessages = [
            ...this._chatMessages.slice(0, idx),
            { ...lastMsg, toolCalls: [newToolCall] },
            ...this._chatMessages.slice(idx + 1),
          ];
        } else {
          // New LLM turn starting with tool calls, or no agent message — create new message
          this._chatMessages = [
            ...this._chatMessages,
            {
              role: "agent",
              content: "",
              timestamp: new Date().toISOString(),
              toolCalls: [newToolCall],
            },
          ];
        }
        this._toolBatchOpen = true;
        break;
      }
      case "tool.args": {
        const tcId = data.toolCallId as string;
        const args = data.arguments as Record<string, unknown>;
        const msgIndex = this._chatMessages.findLastIndex(
          m => m.role === "agent" && m.toolCalls?.some(tc => tc.toolCallId === tcId)
        );
        if (msgIndex === -1) break;
        const msg = this._chatMessages[msgIndex];
        const updatedToolCalls = msg.toolCalls!.map(tc =>
          tc.toolCallId === tcId
            ? { ...tc, arguments: args, summary: extractToolSummary(tc.toolName, args) }
            : tc
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, msgIndex),
          { ...msg, toolCalls: updatedToolCalls },
          ...this._chatMessages.slice(msgIndex + 1),
        ];
        break;
      }
      case "tool.end": {
        const tcId = data.toolCallId as string;
        const msgIndex = this._chatMessages.findLastIndex(
          m => m.role === "agent" && m.toolCalls?.some(tc => tc.toolCallId === tcId)
        );
        if (msgIndex === -1) break;
        const msg = this._chatMessages[msgIndex];
        const updatedToolCalls = msg.toolCalls!.map(tc =>
          tc.toolCallId === tcId && tc.status === "running"
            ? { ...tc, status: "complete" as const }
            : tc
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, msgIndex),
          { ...msg, toolCalls: updatedToolCalls },
          ...this._chatMessages.slice(msgIndex + 1),
        ];
        break;
      }
      case "tool.result": {
        const tcId = data.toolCallId as string;
        const rawResult = data.result;
        const resultStr = typeof rawResult === "string" ? rawResult : JSON.stringify(rawResult);
        const isError = typeof resultStr === "string"
          && resultStr.startsWith("Tool '") && (resultStr.includes("error") || resultStr.includes("failed"));
        const msgIndex = this._chatMessages.findLastIndex(
          m => m.role === "agent" && m.toolCalls?.some(tc => tc.toolCallId === tcId)
        );
        if (msgIndex === -1) break;
        const msg = this._chatMessages[msgIndex];
        const updatedToolCalls = msg.toolCalls!.map(tc =>
          tc.toolCallId === tcId
            ? { ...tc, result: resultStr, status: (isError ? "error" : "complete") as ToolCallData["status"] }
            : tc
        );
        this._chatMessages = [
          ...this._chatMessages.slice(0, msgIndex),
          { ...msg, toolCalls: updatedToolCalls },
          ...this._chatMessages.slice(msgIndex + 1),
        ];
        break;
      }
      case "step.started":
        this._finaliseStreamingMessage();
        this._toolBatchOpen = false;
        if (this._instance) {
          const step = this._instance.steps.find((s) => s.id === data.stepId);
          if (step) step.status = "Active";
          this.requestUpdate();
        }
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `Starting ${(data.stepName as string) || (data.stepId as string)}...`,
            timestamp: new Date().toISOString(),
          },
        ];
        break;
      case "step.finished": {
        this._finaliseStreamingMessage();
        if (this._instance) {
          const step = this._instance.steps.find((s) => s.id === data.stepId);
          if (step) step.status = data.status as string;
          this.requestUpdate();
        }
        const stepLabel = (data.stepName as string) || (data.stepId as string);
        const outcome = (data.status as string) === "Error" ? "failed" : "completed";
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `${stepLabel} ${outcome}`,
            timestamp: new Date().toISOString(),
          },
        ];
        break;
      }
      case "run.finished":
        this._finaliseStreamingMessage();
        if (this._instance) {
          this._instance.status = "Completed";
          this.requestUpdate();
        }
        break;
      case "run.error":
        this._finaliseStreamingMessage();
        if (this._instance) {
          this._instance.status = "Failed";
          this.requestUpdate();
        }
        this._chatMessages = [
          ...this._chatMessages,
          {
            role: "system",
            content: `Error: ${(data.message as string) || "Workflow failed"}`,
            timestamp: new Date().toISOString(),
          },
        ];
        break;
    }
  }

  private async _onCancelClick(): Promise<void> {
    if (this._cancelling || !this._instance) return;

    try {
      await umbConfirmModal(this, {
        headline: "Cancel workflow run",
        content: "Cancel this workflow run?",
        color: "danger",
        confirmLabel: "Cancel Run",
      });
    } catch {
      return;
    }

    this._cancelling = true;
    try {
      const token = await this.#authContext?.getLatestToken();
      await cancelInstance(this._instance.id, token);
      await this._loadData();
    } catch {
      // Don't set _error — preserve the existing valid view (Story 3.3 review learning)
      console.warn("Failed to cancel instance");
    } finally {
      this._cancelling = false;
    }
  }

  private _renderStepProgress() {
    if (!this._instance) return nothing;

    return html`
      <div class="step-sidebar">
        <ul role="list">
          ${this._instance.steps.map(
            (step, index) => html`
              <li
                role="listitem"
                class="step-item
                  ${step.status === "Pending" ? "pending" : ""}
                  ${step.status === "Active" ? "active" : ""}
                  ${step.status === "Complete" ? "complete" : ""}
                  ${step.status === "Error" ? "error" : ""}
                  ${step.status !== "Pending" ? "clickable" : ""}
                  ${this._selectedStepId === step.id ? "selected" : ""}"
                aria-label="Step ${index + 1}: ${step.name} — ${stepSubtitle(step)}"
                @click=${step.status !== "Pending" ? () => this._onStepClick(step) : nothing}
                aria-current=${step.status === "Active" ? "step" : nothing}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${step.status === "Active" ? "step-icon-spin" : ""}"
                    name=${stepIconName(step.status)}
                  ></uui-icon>
                </div>
                <div class="step-text">
                  <span class="step-name">${step.name}</span>
                  <span class="step-subtitle">${stepSubtitle(step)}</span>
                </div>
              </li>
            `,
          )}
        </ul>
      </div>
    `;
  }

  render() {
    if (this._loading) {
      return html`<uui-loader></uui-loader>`;
    }

    if (this._error || !this._instance) {
      return html`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    }

    const inst = this._instance;
    const showStart = inst.status === "Pending" && !this._streaming;
    const hasActiveStep = inst.steps.some((s) => s.status === "Active");
    const showContinue = inst.status === "Running" && !hasActiveStep && !this._streaming
      && inst.steps.some((s) => s.status === "Pending");
    const showCancel = (inst.status === "Running" || inst.status === "Pending") && !this._streaming;

    const mainContent = this._providerError
      ? html`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>`
      : html`<shallai-chat-panel
          .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
          ?is-streaming=${this._streaming && !this._viewingStepId}
        ></shallai-chat-panel>`;

    return html`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${inst.workflowName || inst.workflowAlias} — Run #${this._runNumber}</h2>
        ${showStart
          ? html`
              <uui-button label="Start" look="primary" @click=${this._onStartClick}>
                Start
              </uui-button>
            `
          : nothing}
        ${showContinue
          ? html`
              <uui-button label="Continue" look="primary" @click=${this._onStartClick}>
                Continue
              </uui-button>
            `
          : nothing}
        ${this._streaming
          ? html`<uui-loader-bar></uui-loader-bar>`
          : nothing}
        ${showCancel
          ? html`
              <uui-button
                label="Cancel"
                look="secondary"
                color="danger"
                ?disabled=${this._cancelling}
                @click=${this._onCancelClick}
              >
                Cancel
              </uui-button>
            `
          : nothing}
      </div>

      <div class="detail-grid">
        ${this._renderStepProgress()}
        ${mainContent}
      </div>
    `;
  }
}

export default ShallaiInstanceDetailElement;
