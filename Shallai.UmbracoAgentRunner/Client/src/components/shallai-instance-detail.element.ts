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
import { getInstance, getInstances, cancelInstance } from "../api/api-client.js";
import type {
  InstanceDetailResponse,
  StepDetailResponse,
} from "../api/types.js";
import {
  extractInstanceId,
  buildInstanceListPath,
  stepSubtitle,
  stepIconName,
} from "../utils/instance-detail-helpers.js";
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

  private _onStepClick(step: StepDetailResponse): void {
    if (step.status === "Pending") return;
    this._selectedStepId = step.id;
    this.dispatchEvent(
      new CustomEvent("step-selected", {
        detail: { stepId: step.id, status: step.status },
        bubbles: true,
        composed: true,
      }),
    );
  }

  private _onStartClick(): void {
    console.log("Start not yet implemented");
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
    const showStart = inst.status === "Pending";
    const showCancel = inst.status === "Running" || inst.status === "Pending";

    return html`
      <div class="header">
        <uui-button look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${inst.workflowName || inst.workflowAlias} — Run #${this._runNumber}</h2>
        ${showStart
          ? html`
              <uui-button look="primary" @click=${this._onStartClick}>
                Start
              </uui-button>
            `
          : nothing}
        ${showCancel
          ? html`
              <uui-button
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
        <div class="main-placeholder">
          Click 'Start' to begin the workflow.
        </div>
      </div>
    `;
  }
}

export default ShallaiInstanceDetailElement;
