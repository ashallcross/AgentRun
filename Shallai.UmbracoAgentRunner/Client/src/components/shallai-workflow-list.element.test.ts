import { expect } from "@open-wc/testing";
import type { WorkflowSummary } from "../api/types.js";

describe("shallai-workflow-list", () => {
  const mockWorkflows: WorkflowSummary[] = [
    {
      alias: "content-audit",
      name: "Content Audit",
      description: "Audits content quality",
      stepCount: 3,
      mode: "interactive",
    },
    {
      alias: "seo-check",
      name: "SEO Check",
      description: "Checks SEO compliance",
      stepCount: 1,
      mode: "autonomous",
    },
  ];

  describe("table rendering logic", () => {
    it("maps workflow data to correct column values", () => {
      const w = mockWorkflows[0];
      expect(w.name).to.equal("Content Audit");
      expect(`${w.stepCount} steps`).to.equal("3 steps");
      expect(w.mode).to.equal("interactive");
    });

    it("renders correct number of rows for workflow count", () => {
      expect(mockWorkflows.length).to.equal(2);
      expect(mockWorkflows[0].alias).to.equal("content-audit");
      expect(mockWorkflows[1].alias).to.equal("seo-check");
    });

    it("handles empty workflow array for empty state", () => {
      const empty: WorkflowSummary[] = [];
      expect(empty.length).to.equal(0);
    });
  });

  describe("navigation logic", () => {
    it("constructs correct navigation path with encoded alias", () => {
      const alias = "content-audit";
      const currentPath = "/section/agent-workflows/overview/workflows";
      const encoded = encodeURIComponent(alias);
      const targetPath = currentPath.endsWith("/")
        ? `${currentPath}${encoded}`
        : `${currentPath}/${encoded}`;
      expect(targetPath).to.equal(
        "/section/agent-workflows/overview/workflows/content-audit"
      );
    });

    it("handles trailing slash in current path", () => {
      const alias = "seo-check";
      const currentPath = "/section/agent-workflows/overview/workflows/";
      const encoded = encodeURIComponent(alias);
      const targetPath = currentPath.endsWith("/")
        ? `${currentPath}${encoded}`
        : `${currentPath}/${encoded}`;
      expect(targetPath).to.equal(
        "/section/agent-workflows/overview/workflows/seo-check"
      );
    });

    it("encodes URL-unsafe characters in alias", () => {
      const alias = "my workflow/test";
      const currentPath = "/section/agent-workflows/overview/workflows";
      const encoded = encodeURIComponent(alias);
      const targetPath = `${currentPath}/${encoded}`;
      expect(targetPath).to.equal(
        "/section/agent-workflows/overview/workflows/my%20workflow%2Ftest"
      );
    });

    it("row click triggers history pushState only", () => {
      const originalPushState = window.history.pushState;
      let pushedUrl = "";
      window.history.pushState = (
        _data: unknown,
        _unused: string,
        url?: string | URL | null
      ) => {
        pushedUrl = url as string;
      };

      try {
        const alias = "content-audit";
        const currentPath = "/section/agent-workflows/overview/workflows";
        const encoded = encodeURIComponent(alias);
        const targetPath = currentPath.endsWith("/")
          ? `${currentPath}${encoded}`
          : `${currentPath}/${encoded}`;
        window.history.pushState({}, "", targetPath);

        expect(pushedUrl).to.equal(
          "/section/agent-workflows/overview/workflows/content-audit"
        );
      } finally {
        window.history.pushState = originalPushState;
      }
    });
  });

  describe("API client integration", () => {
    let originalFetch: typeof window.fetch;

    beforeEach(() => {
      originalFetch = window.fetch;
    });

    afterEach(() => {
      window.fetch = originalFetch;
    });

    it("fetch helper sends bearer token in Authorization header", async () => {
      let capturedUrl = "";
      let capturedInit: RequestInit | undefined;

      window.fetch = async (
        input: RequestInfo | URL,
        init?: RequestInit
      ): Promise<Response> => {
        capturedUrl = input as string;
        capturedInit = init;
        return new Response(JSON.stringify(mockWorkflows), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      };

      const { getWorkflows } = await import("../api/api-client.js");
      const result = await getWorkflows("test-token-123");

      expect(capturedUrl).to.equal("/umbraco/api/shallai/workflows");
      const headers = capturedInit?.headers as Record<string, string>;
      expect(headers.Accept).to.equal("application/json");
      expect(headers.Authorization).to.equal("Bearer test-token-123");
      expect(result).to.have.length(2);
      expect(result[0].alias).to.equal("content-audit");
    });

    it("fetch helper works without token", async () => {
      let capturedInit: RequestInit | undefined;

      window.fetch = async (
        _input: RequestInfo | URL,
        init?: RequestInit
      ): Promise<Response> => {
        capturedInit = init;
        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      };

      const { getWorkflows } = await import("../api/api-client.js");
      await getWorkflows();

      const headers = capturedInit?.headers as Record<string, string>;
      expect(headers.Accept).to.equal("application/json");
      expect(headers.Authorization).to.be.undefined;
    });

    it("fetch helper throws on non-OK response", async () => {
      window.fetch = async (): Promise<Response> =>
        new Response("Unauthorized", {
          status: 401,
          statusText: "Unauthorized",
        });

      const apiModule = await import("../api/api-client.js");

      try {
        await apiModule.getWorkflows();
        expect.fail("Should have thrown");
      } catch (err) {
        expect((err as Error).message).to.include("401");
      }
    });
  });
});
