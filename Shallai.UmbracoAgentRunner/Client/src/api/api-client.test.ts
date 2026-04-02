import { expect } from "@open-wc/testing";
import { startInstance, encodeArtifactPath, getArtifact } from "./api-client.js";

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

describe("encodeArtifactPath", () => {
  it("preserves simple paths with slashes", () => {
    expect(encodeArtifactPath("artifacts/review-notes.md")).to.equal("artifacts/review-notes.md");
  });

  it("encodes spaces in path segments", () => {
    expect(encodeArtifactPath("artifacts/my notes.md")).to.equal("artifacts/my%20notes.md");
  });

  it("encodes special characters per segment", () => {
    expect(encodeArtifactPath("artifacts/file (1).md")).to.equal("artifacts/file%20(1).md");
  });

  it("handles deeply nested paths", () => {
    expect(encodeArtifactPath("artifacts/sub/deep/file.md")).to.equal("artifacts/sub/deep/file.md");
  });

  it("encodes hash and question mark characters", () => {
    expect(encodeArtifactPath("artifacts/notes#2.md")).to.equal("artifacts/notes%232.md");
    expect(encodeArtifactPath("artifacts/file?v=1.md")).to.equal("artifacts/file%3Fv%3D1.md");
  });

  it("handles single segment path", () => {
    expect(encodeArtifactPath("file.md")).to.equal("file.md");
  });
});

describe("getArtifact", () => {
  let originalFetch: typeof globalThis.fetch;
  let lastRequest: { url: string; init: RequestInit } | null = null;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
    lastRequest = null;
    globalThis.fetch = ((url: string, init: RequestInit) => {
      lastRequest = { url, init };
      return Promise.resolve(new Response("# Markdown content", { status: 200 }));
    }) as typeof globalThis.fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it("calls correct URL with encoded path segments", async () => {
    await getArtifact("inst-001", "artifacts/review-notes.md");
    expect(lastRequest).to.not.be.null;
    expect(lastRequest!.url).to.include("/instances/inst-001/artifacts/artifacts/review-notes.md");
  });

  it("encodes special characters in filePath segments", async () => {
    await getArtifact("inst-001", "artifacts/my notes.md");
    expect(lastRequest).to.not.be.null;
    expect(lastRequest!.url).to.include("/artifacts/artifacts/my%20notes.md");
  });

  it("returns raw text content", async () => {
    const result = await getArtifact("inst-001", "artifacts/file.md");
    expect(result).to.equal("# Markdown content");
  });

  it("encodes instanceId in URL", async () => {
    await getArtifact("id with spaces", "artifacts/file.md");
    expect(lastRequest).to.not.be.null;
    expect(lastRequest!.url).to.include("/instances/id%20with%20spaces/artifacts/");
  });
});
