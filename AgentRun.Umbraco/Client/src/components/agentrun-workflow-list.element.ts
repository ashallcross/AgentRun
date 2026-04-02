import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  state,
} from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import type { WorkflowSummary } from "../api/types.js";
import { getWorkflows } from "../api/api-client.js";

@customElement("agentrun-workflow-list")
export class AgentRunWorkflowListElement extends UmbLitElement {
  #authContext?: typeof UMB_AUTH_CONTEXT.TYPE;

  @state()
  private _workflows: WorkflowSummary[] = [];

  @state()
  private _loading = true;

  @state()
  private _error = false;

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }

    uui-table-row {
      cursor: pointer;
    }

    uui-table-row:hover {
      background-color: var(--uui-color-surface-emphasis);
    }

    .workflow-name {
      color: var(--uui-color-interactive);
    }

    .empty-state,
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
    this._loadWorkflows();
  }

  private async _loadWorkflows(): Promise<void> {
    try {
      const token = await this.#authContext?.getLatestToken();
      this._workflows = await getWorkflows(token);
      this._error = false;
    } catch {
      this._workflows = [];
      this._error = true;
    } finally {
      this._loading = false;
    }
  }

  private _navigateToWorkflow(alias: string): void {
    const currentPath = window.location.pathname;
    const encoded = encodeURIComponent(alias);
    const targetPath = currentPath.endsWith("/")
      ? `${currentPath}${encoded}`
      : `${currentPath}/${encoded}`;
    // Umbraco patches history.pushState to dispatch custom router events,
    // so this call alone triggers umb-router-slot route matching.
    window.history.pushState({}, "", targetPath);
  }

  render() {
    if (this._loading) {
      return html`<uui-loader></uui-loader>`;
    }

    if (this._error) {
      return html`
        <div class="error-state">
          Failed to load workflows. Check that you have backoffice access and
          try refreshing the page.
        </div>
      `;
    }

    if (this._workflows.length === 0) {
      return html`
        <div class="empty-state">
          No workflows found. Add a workflow folder to your project's workflows
          directory.
        </div>
      `;
    }

    return html`
      <uui-table>
        <uui-table-head>
          <uui-table-head-cell>Name</uui-table-head-cell>
          <uui-table-head-cell style="width: 100px;">Steps</uui-table-head-cell>
          <uui-table-head-cell style="width: 120px;">Mode</uui-table-head-cell>
        </uui-table-head>
        ${this._workflows.map(
          (w) => html`
            <uui-table-row @click=${() => this._navigateToWorkflow(w.alias)}>
              <uui-table-cell>
                <span class="workflow-name">${w.name}</span>
              </uui-table-cell>
              <uui-table-cell>${w.stepCount} steps</uui-table-cell>
              <uui-table-cell>
                <uui-tag>${w.mode}</uui-tag>
              </uui-table-cell>
            </uui-table-row>
          `
        )}
      </uui-table>
    `;
  }
}

export default AgentRunWorkflowListElement;
