import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import {
  customElement,
  html,
  css,
  property,
  state,
  nothing,
} from "@umbraco-cms/backoffice/external/lit";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import { getArtifact } from "../api/api-client.js";
import "./shallai-markdown-renderer.element.js";

@customElement("shallai-artifact-popover")
export class ShallaiArtifactPopoverElement extends UmbLitElement {
  #authContext?: typeof UMB_AUTH_CONTEXT.TYPE;

  @property({ type: String })
  instanceId = "";

  @property({ type: String })
  artifactPath = "";

  @property({ type: Boolean })
  open = false;

  @state()
  private _content: string | null = null;

  @state()
  private _loading = false;

  @state()
  private _error = false;

  @state()
  private _downloadError = false;

  private _fetchGeneration = 0;

  constructor() {
    super();
    this.consumeContext(UMB_AUTH_CONTEXT, (context) => {
      this.#authContext = context;
    });
  }

  willUpdate(changedProperties: Map<string, unknown>): void {
    const openChanged = changedProperties.has("open") && this.open;
    const pathChanged = changedProperties.has("artifactPath") && this.open && this.artifactPath;
    if (openChanged || pathChanged) {
      this._fetchArtifact();
    }
  }

  private async _fetchArtifact(): Promise<void> {
    if (!this.instanceId || !this.artifactPath) return;

    const generation = ++this._fetchGeneration;
    this._loading = true;
    this._error = false;
    this._content = null;

    try {
      const token = await this.#authContext?.getLatestToken();
      const content = await getArtifact(this.instanceId, this.artifactPath, token);
      if (generation !== this._fetchGeneration) return;
      this._content = content;
    } catch {
      if (generation !== this._fetchGeneration) return;
      this._error = true;
    } finally {
      if (generation === this._fetchGeneration) {
        this._loading = false;
      }
    }
  }

  private _getFilename(): string {
    const segments = this.artifactPath.split("/");
    return segments[segments.length - 1];
  }

  private _onClose(): void {
    this.dispatchEvent(
      new CustomEvent("popover-closed", {
        bubbles: true,
        composed: true,
      }),
    );
  }

  private _onDialogClose(): void {
    this._onClose();
  }

  private async _onDownload(): Promise<void> {
    this._downloadError = false;
    try {
      let content: string;
      if (this._content !== null) {
        content = this._content;
      } else {
        const token = await this.#authContext?.getLatestToken();
        content = await getArtifact(this.instanceId, this.artifactPath, token);
      }
      const blob = new Blob([content], { type: "text/plain" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = this._getFilename();
      anchor.click();
      URL.revokeObjectURL(url);
    } catch {
      this._downloadError = true;
    }
  }

  private _onRetry(): void {
    this._fetchArtifact();
  }

  render() {
    if (!this.open) return nothing;

    return html`
      <uui-dialog @close=${this._onDialogClose}>
        <uui-dialog-layout headline=${this._getFilename()}>
          <div class="dialog-body">
            ${this._loading
              ? html`<div class="loader-wrapper"><uui-loader></uui-loader></div>`
              : this._error
                ? html`
                    <div class="error-state">
                      <p>Could not load artifact.</p>
                      <a href="#" class="retry-link" @click=${(e: Event) => { e.preventDefault(); this._onRetry(); }}>Retry</a>
                    </div>
                  `
                : html`
                    <uui-scroll-container class="scroll-container">
                      <shallai-markdown-renderer .content=${this._content ?? ""}></shallai-markdown-renderer>
                    </uui-scroll-container>
                  `}
          </div>
          <div slot="actions">
            ${this._downloadError ? html`<span class="download-error">Download failed.</span>` : nothing}
            <uui-button label="Download" look="secondary" @click=${this._onDownload}>Download</uui-button>
            <uui-button label="Close" look="primary" @click=${this._onClose}>Close</uui-button>
          </div>
        </uui-dialog-layout>
      </uui-dialog>
    `;
  }

  static styles = css`
    :host {
      position: fixed;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      z-index: 1000;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(0, 0, 0, 0.4);
    }

    :host(:not([open])) {
      display: none;
    }

    uui-dialog {
      width: min(80vw, 800px);
      height: min(80vh, 600px);
      display: flex;
      flex-direction: column;
      background: var(--uui-color-surface);
      border-radius: var(--uui-border-radius);
      box-shadow: var(--uui-shadow-depth-5, 0 12px 40px rgba(0, 0, 0, 0.3));
    }

    uui-dialog-layout {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .dialog-body {
      flex: 1;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      padding: var(--uui-size-space-5);
    }

    .scroll-container {
      flex: 1;
      overflow-y: auto;
    }

    .loader-wrapper {
      display: flex;
      align-items: center;
      justify-content: center;
      flex: 1;
    }

    .error-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      flex: 1;
      color: var(--uui-color-text-alt);
    }

    .error-state p {
      margin: 0 0 var(--uui-size-space-3);
    }

    .retry-link {
      color: var(--uui-color-interactive);
      cursor: pointer;
      text-decoration: none;
    }

    .retry-link:hover {
      text-decoration: underline;
    }

    div[slot="actions"] {
      display: flex;
      gap: var(--uui-size-space-3);
      justify-content: flex-end;
      align-items: center;
    }

    .download-error {
      color: var(--uui-color-danger);
      font-size: var(--uui-type-small-size);
      margin-right: auto;
    }
  `;
}

declare global {
  interface HTMLElementTagNameMap {
    "shallai-artifact-popover": ShallaiArtifactPopoverElement;
  }
}
