import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { customElement, html, css } from "@umbraco-cms/backoffice/external/lit";

@customElement("shallai-instance-list")
export class ShallaiInstanceListElement extends UmbLitElement {
  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }
  `;

  render() {
    return html`
      <uui-box headline="Instances">
        <p>Instance list view</p>
      </uui-box>
    `;
  }
}

export default ShallaiInstanceListElement;
