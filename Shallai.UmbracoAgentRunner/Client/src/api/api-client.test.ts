import { expect } from "@open-wc/testing";
import { startInstance } from "./api-client.js";

describe("startInstance", () => {
  let originalFetch: typeof globalThis.fetch;
  let lastRequest: { url: string; init: RequestInit } | null = null;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
    lastRequest = null;
    globalThis.fetch = ((url: string, init: RequestInit) => {
      lastRequest = { url, init };
      return Promise.resolve(new Response("", { status: 200 }));
    }) as typeof globalThis.fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it("sends POST to correct path", async () => {
    await startInstance("abc123");
    expect(lastRequest).to.not.be.null;
    expect(lastRequest!.url).to.include("/instances/abc123/start");
    expect(lastRequest!.init.method).to.equal("POST");
  });

  it("includes Authorization header when token provided", async () => {
    await startInstance("abc123", "my-token");
    expect(lastRequest).to.not.be.null;
    const headers = lastRequest!.init.headers as Record<string, string>;
    expect(headers.Authorization).to.equal("Bearer my-token");
  });

  it("returns raw Response object", async () => {
    const response = await startInstance("abc123");
    expect(response).to.be.instanceOf(Response);
  });
});
