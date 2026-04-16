import { expect } from "@open-wc/testing";
import {
  startInstanceAction,
  retryInstanceAction,
  cancelInstanceAction,
} from "./instance-detail-actions.js";

describe("instance-detail-actions", () => {
  let originalFetch: typeof window.fetch;

  beforeEach(() => {
    originalFetch = window.fetch;
  });

  afterEach(() => {
    window.fetch = originalFetch;
  });

  it("startInstanceAction returns streaming result with the Response on 200", async () => {
    window.fetch = async () =>
      new Response("event: run.started\ndata: {}\n\n", {
        status: 200,
        headers: { "Content-Type": "text/event-stream" },
      });

    const result = await startInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("streaming");
    if (result.kind === "streaming") {
      expect(result.response.ok).to.be.true;
    }
  });

  it("startInstanceAction returns providerError on 400", async () => {
    window.fetch = async () => new Response("bad request", { status: 400 });

    const result = await startInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("providerError");
  });

  it("startInstanceAction returns failed with status for other non-ok responses", async () => {
    window.fetch = async () => new Response("boom", { status: 500 });

    const result = await startInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("failed");
    if (result.kind === "failed") {
      expect(result.status).to.equal(500);
    }
  });

  it("retryInstanceAction returns streaming result with the Response on 200", async () => {
    window.fetch = async () =>
      new Response("event: run.started\ndata: {}\n\n", {
        status: 200,
        headers: { "Content-Type": "text/event-stream" },
      });

    const result = await retryInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("streaming");
  });

  it("retryInstanceAction returns notRetryable on 409", async () => {
    window.fetch = async () => new Response("conflict", { status: 409 });

    const result = await retryInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("notRetryable");
  });

  it("cancelInstanceAction returns ok when the API call succeeds", async () => {
    window.fetch = async () =>
      new Response(
        JSON.stringify({
          id: "inst-1",
          workflowAlias: "a",
          status: "Cancelled",
          currentStepIndex: 0,
          createdAt: "x",
          updatedAt: "x",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );

    const result = await cancelInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("ok");
  });

  it("cancelInstanceAction surfaces 409 as a conflict (idempotent cancel)", async () => {
    window.fetch = async () => new Response("conflict", { status: 409 });

    const result = await cancelInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("conflict");
  });

  it("cancelInstanceAction surfaces non-409 transport errors as failed", async () => {
    window.fetch = async () => new Response("server error", { status: 500 });

    const result = await cancelInstanceAction("inst-1", "token");

    expect(result.kind).to.equal("failed");
  });
});
