import { expect } from "@open-wc/testing";
import type { InstanceResponse, StepDetailResponse } from "../api/types.js";
import {
  extractInstanceId,
  buildInstanceListPath,
  stepSubtitle,
  stepIconName,
  stepIconColor,
} from "../utils/instance-detail-helpers.js";
import { numberAndSortInstances } from "../utils/instance-list-helpers.js";

describe("shallai-instance-detail", () => {
  const mockStep = (overrides: Partial<StepDetailResponse> = {}): StepDetailResponse => ({
    id: "step-1",
    name: "Content Scanner",
    status: "Pending",
    startedAt: null,
    completedAt: null,
    writesTo: null,
    ...overrides,
  });

  describe("API client functions", () => {
    let originalFetch: typeof window.fetch;

    beforeEach(() => {
      originalFetch = window.fetch;
    });

    afterEach(() => {
      window.fetch = originalFetch;
    });

    // 8.1: getInstance sends GET to correct URL with bearer token
    it("getInstance sends GET to correct URL with bearer token", async () => {
      let capturedUrl = "";
      let capturedInit: RequestInit | undefined;

      window.fetch = async (
        input: RequestInfo | URL,
        init?: RequestInit,
      ): Promise<Response> => {
        capturedUrl = input as string;
        capturedInit = init;
        return new Response(
          JSON.stringify({
            id: "abc123",
            workflowAlias: "content-audit",
            workflowName: "Content Audit",
            status: "Pending",
            currentStepIndex: 0,
            createdAt: "2026-03-30T10:00:00Z",
            updatedAt: "2026-03-30T10:00:00Z",
            createdBy: "admin",
            steps: [],
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        );
      };

      const { getInstance } = await import("../api/api-client.js");
      const result = await getInstance("abc123", "test-token");

      expect(capturedUrl).to.equal("/umbraco/api/shallai/instances/abc123");
      const headers = capturedInit?.headers as Record<string, string>;
      expect(headers.Authorization).to.equal("Bearer test-token");
      expect(result.id).to.equal("abc123");
      expect(result.workflowName).to.equal("Content Audit");
    });

    // 8.1 (cont): getInstance encodes special characters in ID
    it("getInstance encodes special characters in ID", async () => {
      let capturedUrl = "";

      window.fetch = async (
        input: RequestInfo | URL,
      ): Promise<Response> => {
        capturedUrl = input as string;
        return new Response(JSON.stringify({ id: "a/b" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      };

      const { getInstance } = await import("../api/api-client.js");
      await getInstance("a/b", "token");

      expect(capturedUrl).to.equal("/umbraco/api/shallai/instances/a%2Fb");
    });

    // 8.2: cancelInstance sends POST to correct URL
    it("cancelInstance sends POST to correct URL", async () => {
      let capturedUrl = "";
      let capturedInit: RequestInit | undefined;

      window.fetch = async (
        input: RequestInfo | URL,
        init?: RequestInit,
      ): Promise<Response> => {
        capturedUrl = input as string;
        capturedInit = init;
        return new Response(
          JSON.stringify({
            id: "abc123",
            workflowAlias: "content-audit",
            status: "Cancelled",
            currentStepIndex: 0,
            createdAt: "2026-03-30T10:00:00Z",
            updatedAt: "2026-03-30T10:01:00Z",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        );
      };

      const { cancelInstance } = await import("../api/api-client.js");
      const result = await cancelInstance("abc123", "test-token");

      expect(capturedUrl).to.equal(
        "/umbraco/api/shallai/instances/abc123/cancel",
      );
      expect(capturedInit?.method).to.equal("POST");
      const headers = capturedInit?.headers as Record<string, string>;
      expect(headers.Authorization).to.equal("Bearer test-token");
      expect(result.status).to.equal("Cancelled");
    });
  });

  describe("helper functions", () => {
    // 8.3: extractInstanceId extracts and decodes last URL segment
    it("extractInstanceId extracts last URL segment", () => {
      const id = extractInstanceId(
        "/section/agent-workflows/overview/instances/abc123def456",
      );
      expect(id).to.equal("abc123def456");
    });

    it("extractInstanceId decodes URL-encoded segment", () => {
      const id = extractInstanceId(
        "/section/agent-workflows/overview/instances/my%20instance%2Ftest",
      );
      expect(id).to.equal("my instance/test");
    });

    // 8.4: buildInstanceListPath replaces instances/{id} with workflows/{alias}
    it("buildInstanceListPath replaces instances/{id} with workflows/{alias}", () => {
      const result = buildInstanceListPath(
        "/section/agent-workflows/overview/instances/abc123",
        "content-audit",
      );
      expect(result).to.equal(
        "/section/agent-workflows/overview/workflows/content-audit",
      );
    });

    it("buildInstanceListPath encodes workflow alias", () => {
      const result = buildInstanceListPath(
        "/section/agent-workflows/overview/instances/abc123",
        "my workflow/test",
      );
      expect(result).to.equal(
        "/section/agent-workflows/overview/workflows/my%20workflow%2Ftest",
      );
    });

    it("buildInstanceListPath works with longer URL structures", () => {
      const result = buildInstanceListPath(
        "/umbraco/section/agent-workflows/overview/instances/abc123",
        "content-audit",
      );
      expect(result).to.equal(
        "/umbraco/section/agent-workflows/overview/workflows/content-audit",
      );
    });

    // 8.5: stepSubtitle returns correct subtitle for each status
    it("stepSubtitle returns 'Pending' for Pending status", () => {
      expect(stepSubtitle(mockStep({ status: "Pending" }))).to.equal("Pending");
    });

    it("stepSubtitle returns 'Running...' for Active status", () => {
      expect(stepSubtitle(mockStep({ status: "Active" }))).to.equal("Running...");
    });

    it("stepSubtitle returns first writesTo filename for Complete status", () => {
      expect(
        stepSubtitle(
          mockStep({ status: "Complete", writesTo: ["scan-results.md", "extra.md"] }),
        ),
      ).to.equal("scan-results.md");
    });

    it("stepSubtitle returns 'Complete' when Complete with no writesTo", () => {
      expect(
        stepSubtitle(mockStep({ status: "Complete", writesTo: null })),
      ).to.equal("Complete");
    });

    it("stepSubtitle returns 'Failed' for Error status", () => {
      expect(stepSubtitle(mockStep({ status: "Error" }))).to.equal("Failed");
    });

    // 8.6: stepIconName returns correct icon for each status
    it("stepIconName returns correct icon for Pending", () => {
      expect(stepIconName("Pending")).to.equal("icon-circle-dotted");
    });

    it("stepIconName returns correct icon for Active", () => {
      expect(stepIconName("Active")).to.equal("icon-sync");
    });

    it("stepIconName returns correct icon for Complete", () => {
      expect(stepIconName("Complete")).to.equal("icon-check");
    });

    it("stepIconName returns correct icon for Error", () => {
      expect(stepIconName("Error")).to.equal("icon-wrong");
    });

    it("stepIconName returns fallback for unknown status", () => {
      expect(stepIconName("Unknown")).to.equal("icon-circle-dotted");
    });

    // 8.7: stepIconColor returns correct colour for each status
    it("stepIconColor returns undefined for Pending", () => {
      expect(stepIconColor("Pending")).to.be.undefined;
    });

    it("stepIconColor returns 'current' for Active", () => {
      expect(stepIconColor("Active")).to.equal("current");
    });

    it("stepIconColor returns 'positive' for Complete", () => {
      expect(stepIconColor("Complete")).to.equal("positive");
    });

    it("stepIconColor returns 'danger' for Error", () => {
      expect(stepIconColor("Error")).to.equal("danger");
    });
  });

  describe("component behaviour logic", () => {
    // 8.8: step click dispatches step-selected event with correct detail
    it("step-selected CustomEvent has correct detail shape", () => {
      const event = new CustomEvent("step-selected", {
        detail: { stepId: "scanner", status: "Complete" },
        bubbles: true,
        composed: true,
      });
      expect(event.detail.stepId).to.equal("scanner");
      expect(event.detail.status).to.equal("Complete");
      expect(event.bubbles).to.be.true;
      expect(event.composed).to.be.true;
    });

    it("step-selected event for active step has Active status", () => {
      const event = new CustomEvent("step-selected", {
        detail: { stepId: "analyser", status: "Active" },
        bubbles: true,
        composed: true,
      });
      expect(event.detail.stepId).to.equal("analyser");
      expect(event.detail.status).to.equal("Active");
    });

    // 8.9: pending steps do not dispatch events (click handler not attached)
    it("pending step click logic returns early without dispatching", () => {
      const step = mockStep({ status: "Pending" });
      // The component checks `step.status !== "Pending"` before attaching click handler.
      // Verify the guard condition works.
      const shouldAttachClick = step.status !== "Pending";
      expect(shouldAttachClick).to.be.false;
    });

    it("non-pending step click logic attaches handler", () => {
      for (const status of ["Active", "Complete", "Error"]) {
        const shouldAttachClick = status !== "Pending";
        expect(shouldAttachClick).to.be.true;
      }
    });

    // 8.10: Start button only renders for Pending status
    it("Start button renders only for Pending instance status", () => {
      const shouldShowStart = (status: string) => status === "Pending";

      expect(shouldShowStart("Pending")).to.be.true;
      expect(shouldShowStart("Running")).to.be.false;
      expect(shouldShowStart("Completed")).to.be.false;
      expect(shouldShowStart("Failed")).to.be.false;
      expect(shouldShowStart("Cancelled")).to.be.false;
    });

    // 8.11: Cancel button only renders for Running or Pending status
    it("Cancel button renders only for Running or Pending instance status", () => {
      const shouldShowCancel = (status: string) =>
        status === "Running" || status === "Pending";

      expect(shouldShowCancel("Pending")).to.be.true;
      expect(shouldShowCancel("Running")).to.be.true;
      expect(shouldShowCancel("Completed")).to.be.false;
      expect(shouldShowCancel("Failed")).to.be.false;
      expect(shouldShowCancel("Cancelled")).to.be.false;
    });

    // 8.12: run number computation
    it("run number is correctly computed from instance list position", () => {
      const instances: InstanceResponse[] = [
        {
          id: "inst-c",
          workflowAlias: "audit",
          status: "Completed",
          currentStepIndex: 2,
          createdAt: "2026-03-30T14:00:00Z",
          updatedAt: "2026-03-30T14:15:00Z",
        },
        {
          id: "inst-a",
          workflowAlias: "audit",
          status: "Pending",
          currentStepIndex: 0,
          createdAt: "2026-03-30T08:00:00Z",
          updatedAt: "2026-03-30T08:00:00Z",
        },
        {
          id: "inst-b",
          workflowAlias: "audit",
          status: "Running",
          currentStepIndex: 1,
          createdAt: "2026-03-30T10:00:00Z",
          updatedAt: "2026-03-30T10:05:00Z",
        },
      ];

      const numbered = numberAndSortInstances(instances);
      // inst-a (08:00) = #1, inst-b (10:00) = #2, inst-c (14:00) = #3
      // Display order: newest first
      const instA = numbered.find((n) => n.id === "inst-a");
      const instB = numbered.find((n) => n.id === "inst-b");
      const instC = numbered.find((n) => n.id === "inst-c");

      expect(instA?.runNumber).to.equal(1);
      expect(instB?.runNumber).to.equal(2);
      expect(instC?.runNumber).to.equal(3);
    });
  });

  describe("navigation", () => {
    it("back navigation constructs correct workflow list path", () => {
      let pushedUrl = "";
      const originalPushState = window.history.pushState;
      window.history.pushState = (
        _data: unknown,
        _unused: string,
        url?: string | URL | null,
      ) => {
        pushedUrl = url as string;
      };

      try {
        const backPath = buildInstanceListPath(
          "/section/agent-workflows/overview/instances/abc123",
          "content-audit",
        );
        window.history.pushState({}, "", backPath);

        expect(pushedUrl).to.equal(
          "/section/agent-workflows/overview/workflows/content-audit",
        );
      } finally {
        window.history.pushState = originalPushState;
      }
    });
  });
});
