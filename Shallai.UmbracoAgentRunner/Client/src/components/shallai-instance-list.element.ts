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
  getWorkflows,
  getInstances,
  createInstance,
  deleteInstance,
} from "../api/api-client.js";
import {
  type NumberedInstance,
  relativeTime,
  extractWorkflowAlias,
  buildInstancePath,
  buildWorkflowListPath,
  statusColor,
  isTerminalStatus,
  numberAndSortInstances,
} from "../utils/instance-list-helpers.js";

@customElement("shallai-instance-list")
export class ShallaiInstanceListElement extends UmbLitElement {
  #authContext?: typeof UMB_AUTH_CONTEXT.TYPE;

  @state()
  private _instances: NumberedInstance[] = [];

  @state()
  private _workflowName = "";

  @state()
  private _workflowAlias = "";

  @state()
  private _stepCount = 0;

  @state()
  private _loading = true;

  @state()
  private _error = false;

  @state()
  private _creating = false;

  @state()
  private _deleting: string | null = null;

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

    uui-table-row {
      cursor: pointer;
    }

    uui-table-row:hover {
      background-color: var(--uui-color-surface-emphasis);
    }

    .empty-state,
    .error-state {
      padding: var(--uui-size-layout-1);
      color: var(--uui-color-text);
      text-align: center;
    }

    .actions-cell {
      width: 80px;
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
    this._workflowAlias = extractWorkflowAlias(window.location.pathname);
    this._loadData();
  }

  private async _loadData(): Promise<void> {
    try {
      const token = await this.#authContext?.getLatestToken();
      const [workflows, instances] = await Promise.all([
        getWorkflows(token),
        getInstances(this._workflowAlias, token),
      ]);

      const workflow = workflows.find((w) => w.alias === this._workflowAlias);
      this._workflowName = workflow?.name ?? this._workflowAlias;
      this._stepCount = workflow?.stepCount ?? 0;

      this._instances = numberAndSortInstances(instances);
      this._error = false;
    } catch {
      this._instances = [];
      this._error = true;
    } finally {
      this._loading = false;
    }
  }

  private _navigateToInstance(id: string): void {
    window.history.pushState({}, "", buildInstancePath(id, window.location.pathname));
  }

  private _navigateBack(): void {
    window.history.pushState({}, "", buildWorkflowListPath(window.location.pathname));
  }

  private async _createNewRun(): Promise<void> {
    if (this._creating) return;
    this._creating = true;
    try {
      const token = await this.#authContext?.getLatestToken();
      const instance = await createInstance(this._workflowAlias, token);
      this._navigateToInstance(instance.id);
    } finally {
      this._creating = false;
    }
  }

  private async _deleteInstance(id: string, e: Event): Promise<void> {
    e.stopPropagation();
    if (this._deleting) return;

    try {
      await umbConfirmModal(this, {
        headline: "Delete Run",
        content: "Delete this run? This cannot be undone.",
        color: "danger",
        confirmLabel: "Delete",
      });
    } catch {
      return;
    }

    this._deleting = id;
    try {
      const token = await this.#authContext?.getLatestToken();
      await deleteInstance(id, token);
      this._instances = this._instances.filter((inst) => inst.id !== id);
    } finally {
      this._deleting = null;
    }
  }

  private _onActionsClick(e: Event): void {
    e.stopPropagation();
  }

  private _formatStep(inst: NumberedInstance): string {
    if (inst.status === "Completed") return "Complete";
    if (this._stepCount > 0) return `${inst.currentStepIndex + 1} of ${this._stepCount}`;
    return `Step ${inst.currentStepIndex + 1}`;
  }

  render() {
    if (this._loading) {
      return html`<uui-loader></uui-loader>`;
    }

    if (this._error) {
      return html`
        <div class="error-state">
          Failed to load instances. Check that you have backoffice access and
          try refreshing the page.
        </div>
      `;
    }

    return html`
      <div class="header">
        <uui-button label="Back" look="secondary" compact @click=${this._navigateBack}>
          <uui-icon name="icon-arrow-left"></uui-icon>
        </uui-button>
        <h2>${this._workflowName}</h2>
        <uui-button
          label="New Run"
          look="primary"
          color="positive"
          ?disabled=${this._creating}
          @click=${this._createNewRun}
        >
          New Run
        </uui-button>
      </div>

      ${this._instances.length === 0
        ? html`<div class="empty-state">No runs yet. Click 'New Run' to start.</div>`
        : html`
            <uui-table>
              <uui-table-head>
                <uui-table-head-cell style="width: 80px;">Run</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Status</uui-table-head-cell>
                <uui-table-head-cell style="width: 120px;">Step</uui-table-head-cell>
                <uui-table-head-cell>Started</uui-table-head-cell>
                <uui-table-head-cell class="actions-cell">Actions</uui-table-head-cell>
              </uui-table-head>
              ${this._instances.map(
                (inst) => html`
                  <uui-table-row @click=${() => this._navigateToInstance(inst.id)}>
                    <uui-table-cell>#${inst.runNumber}</uui-table-cell>
                    <uui-table-cell>
                      <uui-tag .color=${statusColor(inst.status) ?? nothing}>
                        ${inst.status}
                      </uui-tag>
                    </uui-table-cell>
                    <uui-table-cell>${this._formatStep(inst)}</uui-table-cell>
                    <uui-table-cell title=${inst.createdAt}>
                      ${relativeTime(inst.createdAt)}
                    </uui-table-cell>
                    <uui-table-cell class="actions-cell" @click=${this._onActionsClick}>
                      <uui-action-bar>
                        <uui-button
                          label="Actions"
                          id=${`popover-trigger-${inst.id}`}
                          look="secondary"
                          compact
                        >
                          <uui-icon name="icon-navigation"></uui-icon>
                        </uui-button>
                        <uui-popover-container
                          margin="6"
                          placement="bottom-end"
                          anchor=${`popover-trigger-${inst.id}`}
                        >
                          <umb-popover-layout>
                            <uui-menu-item
                              label="View"
                              @click-label=${() => this._navigateToInstance(inst.id)}
                            ></uui-menu-item>
                            ${isTerminalStatus(inst.status)
                              ? html`
                                  <uui-menu-item
                                    label="Delete"
                                    @click-label=${(e: Event) => this._deleteInstance(inst.id, e)}
                                  ></uui-menu-item>
                                `
                              : nothing}
                          </umb-popover-layout>
                        </uui-popover-container>
                      </uui-action-bar>
                    </uui-table-cell>
                  </uui-table-row>
                `
              )}
            </uui-table>
          `}
    `;
  }
}

export default ShallaiInstanceListElement;
