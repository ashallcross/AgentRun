import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  property,
} from "@umbraco-cms/backoffice/external/lit";
import type { StepDetailResponse } from "../api/types.js";

interface ArtifactEntry {
  path: string;
  stepName: string;
  stepStatus: string;
}

@customElement("agentrun-artifact-list")
export class AgentRunArtifactListElement extends UmbLitElement {
  @property({ attribute: false })
  steps: StepDetailResponse[] = [];

  private _deriveArtifacts(): ArtifactEntry[] {
    const entries: ArtifactEntry[] = [];
    for (const step of this.steps) {
      if (!step.writesTo || step.writesTo.length === 0) continue;
      for (const path of step.writesTo) {
        entries.push({ path, stepName: step.name, stepStatus: step.status });
      }
    }
    return entries;
  }

  private _getFilename(path: string): string {
    const segments = path.split("/");
    return segments[segments.length - 1];
  }

  private _onArtifactClick(entry: ArtifactEntry): void {
    if (entry.stepStatus !== "Complete") return;
    this.dispatchEvent(
      new CustomEvent("artifact-selected", {
        detail: { path: entry.path, stepName: entry.stepName },
        bubbles: true,
        composed: true,
      }),
    );
  }

  private _onKeyDown(e: KeyboardEvent, entry: ArtifactEntry): void {
    if (entry.stepStatus !== "Complete") return;
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      this._onArtifactClick(entry);
    }
  }

  render() {
    const artifacts = this._deriveArtifacts();

    return html`
      <h4 class="heading">Artifacts</h4>
      ${artifacts.length === 0
        ? html`<p class="empty-state">No artifacts yet.</p>`
        : html`
            <div class="artifact-list" role="list">
              ${artifacts.map(
                (entry) => html`
                  <div
                    class="artifact-item ${entry.stepStatus !== "Complete" ? "disabled" : ""}"
                    role="listitem"
                  >
                    <uui-icon class="file-icon" name="icon-document"></uui-icon>
                    <div class="artifact-text">
                      <span
                        class="artifact-name"
                        role="${entry.stepStatus === "Complete" ? "button" : "text"}"
                        tabindex="${entry.stepStatus === "Complete" ? "0" : "-1"}"
                        aria-label="${this._getFilename(entry.path)} from ${entry.stepName}"
                        aria-disabled="${entry.stepStatus !== "Complete"}"
                        @click=${() => this._onArtifactClick(entry)}
                        @keydown=${(e: KeyboardEvent) => this._onKeyDown(e, entry)}
                      >${this._getFilename(entry.path)}</span>
                      <span class="artifact-step">${entry.stepName}</span>
                    </div>
                  </div>
                `,
              )}
            </div>
          `}
    `;
  }

  static styles = css`
    :host {
      display: block;
    }

    .heading {
      margin: 0 0 var(--uui-size-space-3);
      font-size: var(--uui-type-small-size);
      font-weight: 600;
      color: var(--uui-color-text);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .empty-state {
      margin: 0;
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    .artifact-list {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-3);
    }

    .artifact-item {
      display: flex;
      align-items: flex-start;
      gap: var(--uui-size-space-3);
    }

    .artifact-item.disabled {
      color: var(--uui-color-disabled);
    }

    .file-icon {
      flex-shrink: 0;
      margin-top: 2px;
      color: var(--uui-color-text-alt);
    }

    .artifact-item.disabled .file-icon {
      color: var(--uui-color-disabled);
    }

    .artifact-text {
      display: flex;
      flex-direction: column;
      gap: 1px;
      min-width: 0;
    }

    .artifact-name {
      color: var(--uui-color-interactive);
      cursor: pointer;
      font-size: var(--uui-type-small-size);
      word-break: break-all;
    }

    .artifact-name:hover {
      color: var(--uui-color-interactive-emphasis);
    }

    .artifact-name:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 2px;
      border-radius: 2px;
    }

    .artifact-item.disabled .artifact-name {
      color: var(--uui-color-disabled);
      cursor: default;
    }

    .artifact-step {
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    .artifact-item.disabled .artifact-step {
      color: var(--uui-color-disabled);
    }
  `;
}

declare global {
  interface HTMLElementTagNameMap {
    "agentrun-artifact-list": AgentRunArtifactListElement;
  }
}
