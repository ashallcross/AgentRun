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
import {
  getInstance,
  getInstances,
  sendMessage,
  getConversation,
  mapConversationToChat,
} from "../api/api-client.js";
import type { StepDetailResponse, ChatMessage } from "../api/types.js";
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
import { numberAndSortInstances } from "../utils/instance-list-helpers.js";
import {
  initialInstanceDetailState,
  type InstanceDetailState,
} from "../utils/instance-detail-store.js";
import { reduceSseEvent, finaliseStreamingMessage } from "../utils/instance-detail-sse-reducer.js";
import {
  startInstanceAction,
  retryInstanceAction,
  cancelInstanceAction,
} from "../utils/instance-detail-actions.js";

@customElement("agentrun-instance-detail")
export class AgentRunInstanceDetailElement extends UmbLitElement {
  #authContext?: typeof UMB_AUTH_CONTEXT.TYPE;

  @state()
  private _state: InstanceDetailState = initialInstanceDetailState();

  // Popover state is UI-only — not driven by SSE — so it stays as standalone
  // @state fields per AC1 rather than being folded into the store.
  @state()
  private _popoverOpen = false;

  @state()
  private _popoverArtifactPath: string | null = null;

  static styles = css`
    :host { display: block; padding: var(--uui-size-layout-1); }
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
    .step-item.clickable { cursor: pointer; }
    .step-item.clickable:hover { background: var(--uui-color-surface-emphasis); }
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
    .step-item.pending .step-icon-wrapper { border-color: var(--uui-color-border); background: var(--uui-color-surface); }
    .step-item.active .step-icon-wrapper { border-color: var(--uui-color-current); background: var(--uui-color-current); }
    .step-item.complete .step-icon-wrapper { border-color: var(--uui-color-positive); background: var(--uui-color-positive); }
    .step-item.error .step-icon-wrapper { border-color: var(--uui-color-danger); background: var(--uui-color-danger); }
    .step-icon { font-size: 14px; }
    .step-item.pending .step-icon { color: var(--uui-color-text-alt); }
    .step-item.active .step-icon,
    .step-item.complete .step-icon,
    .step-item.error .step-icon { color: var(--uui-color-surface); }
    .step-text { display: flex; flex-direction: column; gap: 2px; padding-top: 3px; }
    .step-name { color: var(--uui-color-text); font-weight: 500; }
    .step-item.pending .step-name { color: var(--uui-color-text-alt); }
    .step-subtitle { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); }
    @keyframes spin { to { transform: rotate(360deg); } }
    .step-icon-spin { animation: spin 1.5s linear infinite; }
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
    .completion-banner uui-button { flex-shrink: 0; }
  `;

  constructor() {
    super();
    this.consumeContext(UMB_AUTH_CONTEXT, (context) => {
      this.#authContext = context;
    });
  }

  private _patch(partial: Partial<InstanceDetailState>): void {
    this._state = { ...this._state, ...partial };
  }

  private _appendMessage(message: ChatMessage): void {
    this._patch({ chatMessages: [...this._state.chatMessages, message] });
  }

  private _now(): string {
    return new Date().toISOString();
  }

  connectedCallback(): void {
    super.connectedCallback();
    this._patch({ loading: true, error: false });
    this._loadData();
  }

  private async _loadData(): Promise<void> {
    try {
      const instanceId = extractInstanceId(window.location.pathname);
      const token = await this.#authContext?.getLatestToken();
      const instance = await getInstance(instanceId, token);

      const allInstances = await getInstances(instance.workflowAlias, token);
      const numbered = numberAndSortInstances(allInstances);
      const runNumber = numbered.find((n) => n.id === instance.id)?.runNumber ?? 0;

      // History bootstrap — only fires on first load (chat empty + not
      // streaming). Snapshotting state mid-stream and writing it back here
      // would clobber any chatMessages a concurrent SSE consumer appended
      // during the awaits above (manual E2E 2026-04-15 — Test 3 silent-drop
      // root cause). Read state inside the branch so the snapshot is fresh.
      let bootstrappedHistory: ChatMessage[] | null = null;
      if (!this._state.streaming && this._state.chatMessages.length === 0) {
        const currentStep =
          instance.steps.find((s) => s.status === "Active" || s.status === "Error") ??
          instance.steps.findLast((s) => s.status === "Complete");
        if (currentStep) {
          try {
            const entries = await getConversation(instance.id, currentStep.id, token);
            bootstrappedHistory = mapConversationToChat(entries);
          } catch {
            // Non-critical — display empty chat rather than failing
          }
        }
      }

      // Re-check streaming/empty AFTER the awaits. If a concurrent SSE consumer
      // started appending while getConversation was in flight, the bootstrap
      // is stale — discard it and leave the live chatMessages alone.
      const patch: Partial<InstanceDetailState> = { instance, runNumber, error: false };
      if (
        bootstrappedHistory !== null &&
        !this._state.streaming &&
        this._state.chatMessages.length === 0
      ) {
        patch.chatMessages = bootstrappedHistory;
      }
      this._patch(patch);
    } catch {
      this._patch({ error: true });
    } finally {
      this._patch({ loading: false });
    }
  }

  private _navigateBack(): void {
    const instance = this._state.instance;
    if (!instance) return;
    const path = buildInstanceListPath(window.location.pathname, instance.workflowAlias);
    window.history.pushState({}, "", path);
  }

  private async _onStepClick(step: StepDetailResponse): Promise<void> {
    if (step.status === "Pending") return;
    this._patch({ selectedStepId: step.id });
    this.dispatchEvent(
      new CustomEvent("step-selected", {
        detail: { stepId: step.id, status: step.status },
        bubbles: true,
        composed: true,
      }),
    );
    if (step.status === "Active") {
      this._patch({ viewingStepId: null });
      return;
    }
    this._patch({ viewingStepId: step.id });
    try {
      const token = await this.#authContext?.getLatestToken();
      const entries = await getConversation(this._state.instance!.id, step.id, token);
      this._patch({ historyMessages: mapConversationToChat(entries) });
    } catch {
      this._patch({ historyMessages: [] });
    }
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    this._patch({ streaming: false, agentResponding: false });
  }

  private async _onStartClick(): Promise<void> {
    if (this._state.streaming || !this._state.instance) return;
    const token = await this.#authContext?.getLatestToken();
    const result = await startInstanceAction(this._state.instance.id, token);
    if (result.kind === "providerError") {
      this._patch({ providerError: true });
      return;
    }
    if (result.kind !== "streaming") {
      await this._loadData();
      return;
    }
    await this._streamSseResponse(result.response);
  }

  private async _onRetryClick(): Promise<void> {
    const instance = this._state.instance;
    if (this._state.retrying || this._state.streaming || !instance) return;
    if (instance.status !== "Failed" && instance.status !== "Interrupted") return;

    this._patch({ retrying: true, chatMessages: [] });

    try {
      const token = await this.#authContext?.getLatestToken();
      const result = await retryInstanceAction(instance.id, token);

      if (result.kind !== "streaming") {
        if (result.kind === "notRetryable") {
          this._patch({
            chatMessages: [{
              role: "system",
              content: "Cannot retry — instance is no longer in a failed state.",
              timestamp: this._now(),
            }],
          });
        }
        await this._loadData();
        return;
      }

      await this._streamSseResponse(result.response);
    } finally {
      // Always clear retrying — a synchronous throw inside _streamSseResponse
      // (reducer side-effect, parse error) must not leave the Retry button
      // stuck disabled until page reload.
      this._patch({ retrying: false });
    }
  }

  private async _streamSseResponse(response: Response): Promise<void> {
    this._patch({
      streaming: true,
      providerError: false,
      viewingStepId: null,
      streamingText: "",
    });
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
      this._state = finaliseStreamingMessage(this._state);
      this._appendMessage({ role: "system", content: "Connection lost.", timestamp: this._now() });
    } finally {
      this._patch({ streaming: false, agentResponding: false });
      await this._loadData();
      this._checkStepCompletable();
    }
  }

  private _checkStepCompletable(): void {
    const instance = this._state.instance;
    if (!instance) return;
    if (instance.workflowMode === "autonomous") {
      this._patch({ stepCompletable: false });
      return;
    }
    // Terminal instance states must never surface the "Continue to next step"
    // button — a Cancelled run with Complete/Cancelled/Pending steps would
    // otherwise satisfy the Pending-remaining check and let the user advance
    // past a deliberate cancel (manual E2E 2026-04-15).
    const status = instance.status;
    if (status === "Cancelled" || status === "Failed" || status === "Completed") {
      this._patch({ stepCompletable: false });
      return;
    }
    const hasActive = instance.steps.some((s) => s.status === "Active");
    const hasComplete = instance.steps.some((s) => s.status === "Complete");
    const hasPending = instance.steps.some((s) => s.status === "Pending");
    this._patch({ stepCompletable: !hasActive && hasComplete && hasPending });
  }

  private _handleSseEvent(eventType: string, data: Record<string, unknown>): void {
    this._state = reduceSseEvent(this._state, { event: eventType, data });

    // Side effects that don't belong in the pure reducer live here, triggered
    // after the reducer produces new state. Auto-open the artifact popover
    // only on a genuine Completed outcome — firing on Cancelled / Failed
    // (manual E2E 2026-04-15) surfaced a stale prior-step artifact when the
    // user cancelled a later step.
    if (
      eventType === "run.finished" &&
      this._state.instance &&
      this._state.instance.status === "Completed"
    ) {
      const lastCompleteStep = [...this._state.instance.steps]
        .reverse()
        .find((s) => s.status === "Complete" && s.writesTo && s.writesTo.length > 0);
      if (lastCompleteStep && lastCompleteStep.writesTo) {
        this._popoverArtifactPath = lastCompleteStep.writesTo[lastCompleteStep.writesTo.length - 1];
        this._popoverOpen = true;
      }
    }
  }

  private async _submitUserMessage(message: string): Promise<boolean> {
    const instance = this._state.instance;
    if (!instance) return false;
    this._appendMessage({ role: "user", content: message, timestamp: this._now() });
    try {
      const token = await this.#authContext?.getLatestToken();
      await sendMessage(instance.id, message, token);
      return true;
    } catch {
      this._appendMessage({
        role: "system",
        content: "Failed to send message. Try again.",
        timestamp: this._now(),
      });
      return false;
    }
  }

  private async _onSendMessage(e: CustomEvent<{ message: string }>): Promise<void> {
    await this._submitUserMessage(e.detail.message);
  }

  private async _onSendAndStream(e: CustomEvent<{ message: string }>): Promise<void> {
    if (!(await this._submitUserMessage(e.detail.message))) return;
    this._patch({ stepCompletable: false });
    await this._onStartClick();
  }

  private async _onAdvanceStep(): Promise<void> {
    if (!this._state.instance || this._state.streaming) return;
    this._patch({ stepCompletable: false, chatMessages: [] });
    await this._onStartClick();
  }

  private async _onCancelClick(): Promise<void> {
    const instance = this._state.instance;
    if (this._state.cancelling || !instance) return;

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

    this._patch({ cancelling: true });
    try {
      const token = await this.#authContext?.getLatestToken();
      const result = await cancelInstanceAction(instance.id, token);
      if (result.kind === "failed") {
        console.warn("Failed to cancel instance");
      }
      // Always reload so the view reflects the current server state regardless
      // of whether the POST won the race, lost the race, or was a no-op.
      await this._loadData();
    } finally {
      this._patch({ cancelling: false });
    }
  }

  private _renderStepProgress() {
    const instance = this._state.instance;
    if (!instance) return nothing;

    return html`
      <div class="step-sidebar">
        <ul role="list">
          ${instance.steps.map(
            (step, index) => html`
              <li
                role="listitem"
                class="step-item
                  ${step.status === "Pending" ? "pending" : ""}
                  ${step.status === "Active" ? "active" : ""}
                  ${step.status === "Complete" ? "complete" : ""}
                  ${step.status === "Error" ? "error" : ""}
                  ${step.status !== "Pending" ? "clickable" : ""}
                  ${this._state.selectedStepId === step.id ? "selected" : ""}"
                aria-label="Step ${index + 1}: ${step.name} — ${stepSubtitle(step)}"
                @click=${step.status !== "Pending" ? () => this._onStepClick(step) : nothing}
                aria-current=${step.status === "Active" ? "step" : nothing}
              >
                <div class="step-icon-wrapper">
                  <uui-icon
                    class="step-icon ${shouldAnimateStepIcon(step.status, this._state.agentResponding) ? "step-icon-spin" : ""}"
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
          .steps=${instance.steps}
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
    const s = this._state;
    if (s.loading) return html`<uui-loader></uui-loader>`;
    if (s.error || !s.instance) {
      return html`
        <div class="error-state">
          Failed to load instance detail. Check that you have backoffice access
          and try refreshing the page.
        </div>
      `;
    }

    const inst = s.instance;
    const isInteractive = inst.workflowMode !== "autonomous";
    const isTerminal = inst.status === "Completed" || inst.status === "Failed" || inst.status === "Cancelled";
    const hasActiveStep = inst.steps.some((step) => step.status === "Active");

    const showStart = inst.status === "Pending" && !s.streaming;
    // Retry button: shows for Failed or Interrupted instances (Story 10.9 —
    // Interrupted is a recoverable state left by a dropped SSE connection;
    // Retry is the correct resume affordance, same as for Failed).
    const showRetry = (inst.status === "Failed" || inst.status === "Interrupted") && !s.streaming;
    // Completion banner: interactive mode, step finished, agent not actively
    // responding. `stepCompletable` is in-memory only — set by SSE step.finished.
    const showCompletionBanner = isInteractive && s.stepCompletable && !s.agentResponding;

    const isBetweenSteps = shouldShowContinueButton({
      instanceStatus: inst.status,
      hasActiveStep,
      isStreaming: s.streaming,
      hasPendingSteps: inst.steps.some((step) => step.status === "Pending"),
    });

    // Continue button (header): Story 10.10 reopen affordance. Suppressed when
    // the in-session banner is showing so the user does not see two Continue
    // affordances simultaneously; both end up at _onStartClick.
    const showContinue = isBetweenSteps && !showCompletionBanner;

    // Cancel button (Story 10.8): shown whenever a run is in flight regardless
    // of mode or streaming. Failed is excluded — Retry is the correct action.
    const showCancel = inst.status === "Running" || inst.status === "Pending";

    const { inputEnabled, inputPlaceholder } = computeChatInputGate({
      instanceStatus: inst.status,
      isInteractive,
      isTerminal,
      hasActiveStep,
      isStreaming: s.streaming,
      isViewingStepHistory: s.viewingStepId !== null,
      agentResponding: s.agentResponding,
      isBetweenSteps,
    });

    // Send handler: interactive mode uses send-and-stream when no SSE connection,
    // otherwise sends to the Channel (ToolLoop picks it up).
    const sendHandler = isInteractive && !s.streaming
      ? (e: CustomEvent<{ message: string }>) => this._onSendAndStream(e)
      : (e: CustomEvent<{ message: string }>) => this._onSendMessage(e);

    const allStepsComplete = inst.steps.every((step) => step.status === "Complete");
    const showWorkflowComplete = isInteractive && isTerminal && allStepsComplete;

    const messagesForPanel: ChatMessage[] = s.viewingStepId ? s.historyMessages : s.chatMessages;

    const mainContent = s.providerError
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
              .messages=${messagesForPanel}
              ?is-streaming=${s.streaming && !s.viewingStepId}
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
        <h2>${inst.workflowName || inst.workflowAlias} — Run #${s.runNumber}</h2>
        ${showStart
          ? html`<uui-button label="Start" look="primary" @click=${this._onStartClick}>Start</uui-button>`
          : nothing}
        ${showRetry
          ? html`
              <uui-button label="Retry" look="primary" ?disabled=${s.retrying} @click=${this._onRetryClick}>
                ${s.retrying ? html`<uui-loader></uui-loader>` : "Retry"}
              </uui-button>
            `
          : nothing}
        ${showContinue
          ? html`<uui-button label="Continue" look="primary" @click=${this._onStartClick}>Continue</uui-button>`
          : nothing}
        ${showCancel
          ? html`
              <uui-button
                label="Cancel"
                look="secondary"
                color="danger"
                ?disabled=${s.cancelling}
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
