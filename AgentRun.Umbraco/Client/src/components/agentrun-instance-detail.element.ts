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
import { getInstance, getInstances, cancelInstance, startInstance, retryInstance, sendMessage, getConversation, mapConversationToChat } from "../api/api-client.js";
import type {
  InstanceDetailResponse,
  StepDetailResponse,
  ChatMessage,
  ToolCallData,
} from "../api/types.js";
import "./agentrun-chat-panel.element.js";
import "./agentrun-artifact-list.element.js";
import "./agentrun-artifact-popover.element.js";
import {
  extractInstanceId,
  buildInstanceListPath,
  stepSubtitle,
  stepIconName,
  shouldAnimateStepIcon,
  shouldShowContinueButton,
  computeChatInputGate,
} from "../utils/instance-detail-helpers.js";
import { extractToolSummary } from "../utils/tool-helpers.js";
import { numberAndSortInstances } from "../utils/instance-list-helpers.js";

@customElement("agentrun-instance-detail")
export class AgentRunInstanceDetailElement extends UmbLitElement {
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

  @state()
  private _stepCompletable = false;

  @state()
  private _agentResponding = false;

  @state()
  private _retrying = false;

  @state()
  private _popoverOpen = false;

  @state()
  private _popoverArtifactPath: string | null = null;

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
      margin-bottom: var(--uui-size-space-4);
      flex-shrink: 0;
    }

    .header h2 {
      flex: 1;
      margin: 0;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      min-width: 0;
    }

    .detail-grid {
      display: grid;
      grid-template-columns: 240px 1fr;
      gap: var(--uui-size-layout-1);
    }

    .step-sidebar {
      border-right: 1px solid var(--uui-color-border);
      padding-right: var(--uui-size-layout-1);
      overflow-y: auto;
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

    .sidebar-divider {
      border: none;
      border-top: 1px solid var(--uui-color-border);
      margin: var(--uui-size-space-4) 0;
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

    .main-panel {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .completion-banner {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--uui-size-space-3);
      padding: var(--uui-size-space-3) var(--uui-size-space-4);
      background: var(--uui-color-positive-standalone);
      color: var(--uui-color-surface);
      border-radius: var(--uui-border-radius);
      margin-bottom: var(--uui-size-space-3);
      font-size: var(--uui-type-small-size);
    }

    .completion-banner uui-button {
      flex-shrink: 0;
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

      // Load conversation history for the current step when not streaming
      // (e.g. returning to an in-progress, errored, or completed instance)
      if (!this._streaming && this._chatMessages.length === 0) {
        const currentStep = instance.steps.find(
          (s) => s.status === "Active" || s.status === "Error",
        ) ?? instance.steps.findLast((s) => s.status === "Complete");
        if (currentStep) {
          try {
            const entries = await getConversation(instance.id, currentStep.id, token);
            this._chatMessages = mapConversationToChat(entries);
          } catch {
            // Non-critical — display empty chat rather than failing
          }
        }
      }

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
    this._agentResponding = false;
  }

  private async _onStartClick(): Promise<void> {
    if (this._streaming || !this._instance) return;

    const token = await this.#authContext?.getLatestToken();
    const response = await startInstance(this._instance.id, token);

    if (!response.ok) {
      if (response.status === 400) {
        this._providerError = true;
        return;
      }
      await this._loadData();
      return;
    }

    await this._streamSseResponse(response);
  }

  private async _onRetryClick(): Promise<void> {
    if (this._retrying || this._streaming || !this._instance) return;
    if (
      this._instance.status !== "Failed" &&
      this._instance.status !== "Interrupted"
    )
      return;

    this._retrying = true;
    this._chatMessages = [];

    const token = await this.#authContext?.getLatestToken();
    const response = await retryInstance(this._instance.id, token);

    if (!response.ok) {
      this._retrying = false;
      if (response.status === 409) {
        this._chatMessages = [
          {
            role: "system",
            content: "Cannot retry — instance is no longer in a failed state.",
            timestamp: new Date().toISOString(),
          },
        ];
      }
      await this._loadData();
      return;
    }

    await this._streamSseResponse(response);
    this._retrying = false;
  }

  private async _streamSseResponse(response: Response): Promise<void> {
    this._streaming = true;
    this._providerError = false;
    this._viewingStepId = null;
    this._streamingText = "";

    try {
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
      this._agentResponding = false;
      await this._loadData();
      this._checkStepCompletable();
    }
  }

  private _checkStepCompletable(): void {
    if (!this._instance) return;
    const isInteractive = this._instance.workflowMode !== "autonomous";
    if (!isInteractive) {
      this._stepCompletable = false;
      return;
    }
    const activeStep = this._instance.steps.find(s => s.status === "Active");
    const hasCompletedCurrent = !activeStep && this._instance.steps.some(s => s.status === "Complete");
    const hasPendingSteps = this._instance.steps.some(s => s.status === "Pending");
    this._stepCompletable = hasCompletedCurrent && hasPendingSteps;
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
    // Track agent activity — set responding on agent events, cleared by input.wait
    if (["text.delta", "tool.start"].includes(eventType)) {
      this._agentResponding = true;
    }

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

          // Auto-open popover for the final step's last artifact (SSE-only, not on _loadData)
          const lastCompleteStep = [...this._instance.steps]
            .reverse()
            .find(s => s.status === "Complete" && s.writesTo && s.writesTo.length > 0);
          if (lastCompleteStep && lastCompleteStep.writesTo) {
            this._popoverArtifactPath = lastCompleteStep.writesTo[lastCompleteStep.writesTo.length - 1];
            this._popoverOpen = true;
          }

          // Add "Workflow complete." system message
          this._chatMessages = [
            ...this._chatMessages,
            {
              role: "system",
              content: "Workflow complete.",
              timestamp: new Date().toISOString(),
            },
          ];
        }
        break;
      case "user.message":
        // Server confirms user message was injected — already added optimistically, ignore echo
        break;
      case "input.wait":
        // Agent is idle, waiting for user input — enable the chat input
        this._finaliseStreamingMessage();
        this._agentResponding = false;
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
            content: (data.message as string) || "An error occurred.",
            timestamp: new Date().toISOString(),
          },
        ];
        break;
    }
  }

  private async _onSendMessage(e: CustomEvent<{ message: string }>): Promise<void> {
    if (!this._instance) return;
    const message = e.detail.message;

    // Optimistically add user message to chat
    this._chatMessages = [
      ...this._chatMessages,
      {
        role: "user" as const,
        content: message,
        timestamp: new Date().toISOString(),
      },
    ];

    try {
      const token = await this.#authContext?.getLatestToken();
      await sendMessage(this._instance.id, message, token);
    } catch {
      // Message failed — add system error message
      this._chatMessages = [
        ...this._chatMessages,
        {
          role: "system",
          content: "Failed to send message. Try again.",
          timestamp: new Date().toISOString(),
        },
      ];
    }
  }

  private async _onSendAndStream(e: CustomEvent<{ message: string }>): Promise<void> {
    if (!this._instance) return;
    const message = e.detail.message;

    // Optimistically add user message to chat
    this._chatMessages = [
      ...this._chatMessages,
      {
        role: "user" as const,
        content: message,
        timestamp: new Date().toISOString(),
      },
    ];

    try {
      const token = await this.#authContext?.getLatestToken();
      await sendMessage(this._instance.id, message, token);
    } catch {
      this._chatMessages = [
        ...this._chatMessages,
        {
          role: "system",
          content: "Failed to send message. Try again.",
          timestamp: new Date().toISOString(),
        },
      ];
      return;
    }

    // Start SSE stream to get the agent's response
    this._stepCompletable = false;
    await this._onStartClick();
  }

  private async _onAdvanceStep(): Promise<void> {
    if (!this._instance || this._streaming) return;
    this._stepCompletable = false;
    this._chatMessages = [];
    await this._onStartClick();
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
      try {
        await cancelInstance(this._instance.id, token);
      } catch (err) {
        // A 409 means the run is already in a non-cancellable state (Completed /
        // Failed / Cancelled) — treat as idempotent. Any other error is preserved.
        const message = err instanceof Error ? err.message : String(err);
        if (!message.includes("409")) {
          console.warn("Failed to cancel instance");
        }
      }
      // Always reload so the view reflects the current server state regardless of
      // whether the POST won the race, lost the race, or was a no-op.
      await this._loadData();
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
                    class="step-icon ${shouldAnimateStepIcon(step.status, this._agentResponding) ? "step-icon-spin" : ""}"
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
        <hr class="sidebar-divider" />
        <agentrun-artifact-list
          .steps=${this._instance.steps}
          @artifact-selected=${this._onArtifactSelected}
        ></agentrun-artifact-list>
      </div>
    `;
  }

  private _onArtifactSelected(e: CustomEvent<{ path: string; stepName: string }>): void {
    this._popoverArtifactPath = e.detail.path;
    this._popoverOpen = true;
  }

  private _onPopoverClosed(): void {
    this._popoverOpen = false;
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
    const isInteractive = inst.workflowMode !== "autonomous";
    const isTerminal = inst.status === "Completed" || inst.status === "Failed" || inst.status === "Cancelled";
    const activeStep = inst.steps.find(s => s.status === "Active");
    const hasActiveStep = !!activeStep;

    // Start button: shows for Pending instances
    const showStart = inst.status === "Pending" && !this._streaming;
    const startLabel = "Start";

    // Retry button: shows for Failed or Interrupted instances (Story 10.9 —
    // Interrupted is a recoverable state left by a dropped SSE connection;
    // Retry is the correct resume affordance, same as for Failed).
    const showRetry = (inst.status === "Failed" || inst.status === "Interrupted") && !this._streaming;

    // Completion banner: interactive mode, step finished, agent not actively
    // responding. This is the in-session affordance shown right after a step
    // streams to completion ("Step complete — review the output, then advance
    // when ready"). `_stepCompletable` is in-memory only — it is set by the
    // SSE step.finished event and is false on a fresh page load.
    const showCompletionBanner = isInteractive && this._stepCompletable && !this._agentResponding;

    // Between-steps state — the structural condition behind both the Continue
    // header button and the chat-input disable. Decoupled from button
    // visibility so the input gate disables consistently whether the in-session
    // banner or the reopen header button is the visible affordance.
    const isBetweenSteps = shouldShowContinueButton({
      instanceStatus: inst.status,
      hasActiveStep,
      isStreaming: this._streaming,
      hasPendingSteps: inst.steps.some((s) => s.status === "Pending"),
    });

    // Continue button (header): the reopen affordance. Story 10.10 added this
    // to fix the chat-input-rejected-on-resume bug — `_stepCompletable` is
    // false after navigating away, so the banner does not surface and the user
    // had nothing to click. Suppress when the in-session banner is showing so
    // the user does not see two "Continue" affordances simultaneously; the
    // banner has the contextual "Step complete" copy and wins when present.
    // Both end up at _onStartClick.
    const showContinue = isBetweenSteps && !showCompletionBanner;

    // Cancel button (Story 10.8): shown whenever a run is in flight regardless of mode
    // or streaming state — the engine-level CTS wiring makes mid-stream cancel the
    // primary scenario (otherwise tokens burn on a run the user thought was stopped).
    // Failed is not included — the server's CancelInstance rejects Failed with 409, and
    // Retry is the correct action for failed runs.
    const showCancel = inst.status === "Running" || inst.status === "Pending";

    // Chat input gate: delegated to shared helper so tests exercise the same
    // function as production (Story 10.10 — closes the predicate-mirror drift
    // risk flagged in code review). Gate uses `isBetweenSteps`, not the button
    // visibility, so input stays disabled correctly when the banner is the
    // visible affordance.
    const { inputEnabled, inputPlaceholder } = computeChatInputGate({
      instanceStatus: inst.status,
      isInteractive,
      isTerminal,
      hasActiveStep,
      isStreaming: this._streaming,
      isViewingStepHistory: this._viewingStepId !== null,
      agentResponding: this._agentResponding,
      isBetweenSteps,
    });

    // Send handler: interactive mode uses send-and-stream when no SSE connection,
    // otherwise sends to Channel via _onSendMessage (agent picks it up from ToolLoop)
    const sendHandler = isInteractive && !this._streaming
      ? (e: CustomEvent<{ message: string }>) => this._onSendAndStream(e)
      : (e: CustomEvent<{ message: string }>) => this._onSendMessage(e);

    // Workflow complete banner: all steps done
    const allStepsComplete = inst.steps.every(s => s.status === "Complete");
    const showWorkflowComplete = isInteractive && isTerminal && allStepsComplete;

    const mainContent = this._providerError
      ? html`<div class="main-placeholder">Configure an AI provider in Umbraco.AI before workflows can run.</div>`
      : html`
          <div class="main-panel">
            ${showCompletionBanner
              ? html`
                  <div class="completion-banner">
                    <span>Step complete — review the output, then advance when ready</span>
                    <uui-button label="Continue to next step" look="primary" @click=${this._onAdvanceStep}>
                      Continue to next step
                    </uui-button>
                  </div>
                `
              : nothing}
            ${showWorkflowComplete
              ? html`
                  <div class="completion-banner">
                    <span>Workflow complete — all steps finished</span>
                  </div>
                `
              : nothing}
            <agentrun-chat-panel
              .messages=${this._viewingStepId ? this._historyMessages : this._chatMessages}
              ?is-streaming=${this._streaming && !this._viewingStepId}
              ?input-enabled=${inputEnabled}
              input-placeholder=${inputPlaceholder}
              @send-message=${sendHandler}
            ></agentrun-chat-panel>
          </div>`;

    return html`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${inst.workflowName || inst.workflowAlias} — Run #${this._runNumber}</h2>
        ${showStart
          ? html`
              <uui-button label=${startLabel} look="primary" @click=${this._onStartClick}>
                ${startLabel}
              </uui-button>
            `
          : nothing}
        ${showRetry
          ? html`
              <uui-button label="Retry" look="primary" ?disabled=${this._retrying} @click=${this._onRetryClick}>
                ${this._retrying ? html`<uui-loader></uui-loader>` : "Retry"}
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
      <agentrun-artifact-popover
        .instanceId=${inst.id}
        .artifactPath=${this._popoverArtifactPath ?? ""}
        ?open=${this._popoverOpen}
        @popover-closed=${this._onPopoverClosed}
      ></agentrun-artifact-popover>
    `;
  }
}

export default AgentRunInstanceDetailElement;
