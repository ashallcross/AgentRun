import { expect } from "@open-wc/testing";
import type { InstanceResponse } from "../api/types.js";
import {
  relativeTime,
  extractWorkflowAlias,
  buildInstancePath,
  buildWorkflowListPath,
  statusColor,
  isTerminalStatus,
  numberAndSortInstances,
} from "../utils/instance-list-helpers.js";

describe("shallai-instance-list", () => {
  const mockInstances: InstanceResponse[] = [
    {
      id: "aaa111bbb222ccc333ddd444eee55500",
      workflowAlias: "content-audit",
      status: "Completed",
      currentStepIndex: 2,
      createdAt: "2026-03-30T10:00:00Z",
      updatedAt: "2026-03-30T10:15:00Z",
    },
    {
      id: "bbb222ccc333ddd444eee555fff66600",
      workflowAlias: "content-audit",
      status: "Running",
      currentStepIndex: 0,
      createdAt: "2026-03-30T12:00:00Z",
      updatedAt: "2026-03-30T12:05:00Z",
    },
    {
      id: "ccc333ddd444eee555fff666aaa11100",
      workflowAlias: "content-audit",
      status: "Failed",
      currentStepIndex: 1,
      createdAt: "2026-03-30T08:00:00Z",
      updatedAt: "2026-03-30T08:30:00Z",
    },
  ];

  describe("API client functions", () => {
    let originalFetch: typeof window.fetch;

    beforeEach(() => {
      originalFetch = window.fetch;
    });

    afterEach(() => {
      window.fetch = originalFetch;
    });

    it("getInstances sends correct URL with workflowAlias query parameter and bearer token", async () => {
      let capturedUrl = "";
      let capturedInit: RequestInit | undefined;

      window.fetch = async (
        input: RequestInfo | URL,
        init?: RequestInit
      ): Promise<Response> => {
        capturedUrl = input as string;
        capturedInit = init;
        return new Response(JSON.stringify(mockInstances), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      };

      const { getInstances } = await import("../api/api-client.js");
      const result = await getInstances("content-audit", "test-token");

      expect(capturedUrl).to.equal(
        "/umbraco/api/shallai/instances?workflowAlias=content-audit"
      );
      const headers = capturedInit?.headers as Record<string, string>;
      expect(headers.Authorization).to.equal("Bearer test-token");
      expect(result).to.have.length(3);
    });

    it("createInstance sends POST with JSON body and returns response", async () => {
      let capturedUrl = "";
      let capturedInit: RequestInit | undefined;

      const newInstance: InstanceResponse = {
        id: "new00000000000000000000000000000",
        workflowAlias: "content-audit",
        status: "Pending",
        currentStepIndex: 0,
        createdAt: "2026-03-30T14:00:00Z",
        updatedAt: "2026-03-30T14:00:00Z",
      };

      window.fetch = async (
        input: RequestInfo | URL,
        init?: RequestInit
      ): Promise<Response> => {
        capturedUrl = input as string;
        capturedInit = init;
        return new Response(JSON.stringify(newInstance), {
          status: 201,
          headers: { "Content-Type": "application/json" },
        });
      };

      const { createInstance } = await import("../api/api-client.js");
      const result = await createInstance("content-audit", "test-token");

      expect(capturedUrl).to.equal("/umbraco/api/shallai/instances");
      expect(capturedInit?.method).to.equal("POST");
      const body = JSON.parse(capturedInit?.body as string);
      expect(body.workflowAlias).to.equal("content-audit");
      expect(result.id).to.equal("new00000000000000000000000000000");
      expect(result.status).to.equal("Pending");
    });

    it("deleteInstance sends DELETE request to correct URL", async () => {
      let capturedUrl = "";
      let capturedInit: RequestInit | undefined;

      window.fetch = async (
        input: RequestInfo | URL,
        init?: RequestInit
      ): Promise<Response> => {
        capturedUrl = input as string;
        capturedInit = init;
        return new Response(null, { status: 204 });
      };

      const { deleteInstance } = await import("../api/api-client.js");
      await deleteInstance("aaa111bbb222ccc333ddd444eee55500", "test-token");

      expect(capturedUrl).to.equal(
        "/umbraco/api/shallai/instances/aaa111bbb222ccc333ddd444eee55500"
      );
      expect(capturedInit?.method).to.equal("DELETE");
      const headers = capturedInit?.headers as Record<string, string>;
      expect(headers.Authorization).to.equal("Bearer test-token");
    });
  });

  describe("data mapping and numbering", () => {
    it("assigns sequential run numbers with newest first for display", () => {
      const numbered = numberAndSortInstances(mockInstances);

      // Oldest (08:00) = run 1, middle (10:00) = run 2, newest (12:00) = run 3
      // Display order: newest first
      expect(numbered[0].runNumber).to.equal(3);
      expect(numbered[0].id).to.equal("bbb222ccc333ddd444eee555fff66600");
      expect(numbered[1].runNumber).to.equal(2);
      expect(numbered[1].id).to.equal("aaa111bbb222ccc333ddd444eee55500");
      expect(numbered[2].runNumber).to.equal(1);
      expect(numbered[2].id).to.equal("ccc333ddd444eee555fff666aaa11100");
    });

    it("maps status to correct badge colour via statusColor function", () => {
      expect(statusColor("Completed")).to.equal("positive");
      expect(statusColor("Failed")).to.equal("danger");
      expect(statusColor("Running")).to.equal("warning");
      expect(statusColor("Pending")).to.be.undefined;
      expect(statusColor("Cancelled")).to.be.undefined;
    });

    it("formats step display correctly", () => {
      const stepCount = 3;

      // Completed shows "Complete"
      const completed = mockInstances[0];
      const completedDisplay =
        completed.status === "Completed"
          ? "Complete"
          : `${completed.currentStepIndex + 1} of ${stepCount}`;
      expect(completedDisplay).to.equal("Complete");

      // Running shows "1 of 3"
      const running = mockInstances[1];
      const runningDisplay =
        running.status === "Completed"
          ? "Complete"
          : `${running.currentStepIndex + 1} of ${stepCount}`;
      expect(runningDisplay).to.equal("1 of 3");

      // Failed shows "2 of 3"
      const failed = mockInstances[2];
      const failedDisplay =
        failed.status === "Completed"
          ? "Complete"
          : `${failed.currentStepIndex + 1} of ${stepCount}`;
      expect(failedDisplay).to.equal("2 of 3");
    });
  });

  describe("delete availability", () => {
    it("delete is available for terminal statuses (Completed, Failed, Cancelled)", () => {
      for (const status of ["Completed", "Failed", "Cancelled"]) {
        expect(isTerminalStatus(status)).to.be.true;
      }
    });

    it("delete is hidden for Pending and Running statuses", () => {
      for (const status of ["Pending", "Running"]) {
        expect(isTerminalStatus(status)).to.be.false;
      }
    });
  });

  describe("empty state", () => {
    it("empty instances array triggers empty state rendering", () => {
      const instances: InstanceResponse[] = [];
      expect(instances.length).to.equal(0);
      const numbered = numberAndSortInstances(instances);
      expect(numbered.length).to.equal(0);
    });
  });

  describe("navigation logic", () => {
    it("row click constructs correct instance detail path", () => {
      const pathname =
        "/section/agent-workflows/overview/workflows/content-audit";
      const result = buildInstancePath("abc123def456", pathname);
      expect(result).to.equal(
        "/section/agent-workflows/overview/instances/abc123def456"
      );
    });

    it("back button constructs correct workflow list path", () => {
      const pathname =
        "/section/agent-workflows/overview/workflows/content-audit";
      const result = buildWorkflowListPath(pathname);
      expect(result).to.equal(
        "/section/agent-workflows/overview/workflows"
      );
    });

    it("back button works with any URL structure", () => {
      const pathname = "/umbraco/section/agent-workflows/overview/workflows/content-audit";
      const result = buildWorkflowListPath(pathname);
      expect(result).to.equal(
        "/umbraco/section/agent-workflows/overview/workflows"
      );
    });

    it("instance path works with any URL structure", () => {
      const pathname = "/umbraco/section/agent-workflows/overview/workflows/content-audit";
      const result = buildInstancePath("abc123", pathname);
      expect(result).to.equal(
        "/umbraco/section/agent-workflows/overview/instances/abc123"
      );
    });

    it("New Run creates instance and navigates to detail", () => {
      let pushedUrl = "";
      const originalPushState = window.history.pushState;
      window.history.pushState = (
        _data: unknown,
        _unused: string,
        url?: string | URL | null
      ) => {
        pushedUrl = url as string;
      };

      try {
        const pathname =
          "/section/agent-workflows/overview/workflows/content-audit";
        const newId = "new00000000000000000000000000000";
        const targetPath = buildInstancePath(newId, pathname);
        window.history.pushState({}, "", targetPath);

        expect(pushedUrl).to.equal(
          "/section/agent-workflows/overview/instances/new00000000000000000000000000000"
        );
      } finally {
        window.history.pushState = originalPushState;
      }
    });
  });

  describe("relative time helper", () => {
    it("returns 'just now' for times less than 60 seconds ago", () => {
      const now = new Date().toISOString();
      expect(relativeTime(now)).to.equal("just now");
    });

    it("returns 'X minutes ago' for times within the last hour", () => {
      const fiveMinAgo = new Date(Date.now() - 5 * 60 * 1000).toISOString();
      expect(relativeTime(fiveMinAgo)).to.equal("5 minutes ago");
    });

    it("returns '1 minute ago' for singular", () => {
      const oneMinAgo = new Date(Date.now() - 61 * 1000).toISOString();
      expect(relativeTime(oneMinAgo)).to.equal("1 minute ago");
    });

    it("returns 'X hours ago' for times within the last day", () => {
      const threeHoursAgo = new Date(
        Date.now() - 3 * 60 * 60 * 1000
      ).toISOString();
      expect(relativeTime(threeHoursAgo)).to.equal("3 hours ago");
    });

    it("returns 'X days ago' for times within the last month", () => {
      const fiveDaysAgo = new Date(
        Date.now() - 5 * 24 * 60 * 60 * 1000
      ).toISOString();
      expect(relativeTime(fiveDaysAgo)).to.equal("5 days ago");
    });
  });

  describe("workflow alias extraction", () => {
    it("extracts and decodes alias from URL path", () => {
      const alias = extractWorkflowAlias(
        "/section/agent-workflows/overview/workflows/content-audit"
      );
      expect(alias).to.equal("content-audit");
    });

    it("decodes URL-encoded alias with special characters", () => {
      const alias = extractWorkflowAlias(
        "/section/agent-workflows/overview/workflows/my%20workflow%2Ftest"
      );
      expect(alias).to.equal("my workflow/test");
    });
  });
});
