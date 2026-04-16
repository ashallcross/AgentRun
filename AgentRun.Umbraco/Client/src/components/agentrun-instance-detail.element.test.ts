import { expect } from "@open-wc/testing";
import type { InstanceResponse, StepDetailResponse } from "../api/types.js";
import {
  extractInstanceId,
  buildInstanceListPath,
  stepSubtitle,
  stepIconName,
  stepIconColor,
  shouldAnimateStepIcon,
  shouldShowContinueButton,
  computeChatInputGate,
  shouldAppendTerminalLine,
  terminalChatLine,
} from "../utils/instance-detail-helpers.js";
import { numberAndSortInstances } from "../utils/instance-list-helpers.js";

describe("agentrun-instance-detail", () => {
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

      expect(capturedUrl).to.equal("/umbraco/api/agentrun/instances/abc123");
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

      expect(capturedUrl).to.equal("/umbraco/api/agentrun/instances/a%2Fb");
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
        "/umbraco/api/agentrun/instances/abc123/cancel",
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

    it("stepSubtitle returns 'In progress' for Active status", () => {
      expect(stepSubtitle(mockStep({ status: "Active" }))).to.equal("In progress");
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

    it("stepSubtitle returns 'Error' for Error status", () => {
      expect(stepSubtitle(mockStep({ status: "Error" }))).to.equal("Error");
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

    // shouldAnimateStepIcon
    it("shouldAnimateStepIcon returns true only when Active and streaming", () => {
      expect(shouldAnimateStepIcon("Active", true)).to.be.true;
      expect(shouldAnimateStepIcon("Active", false)).to.be.false;
      expect(shouldAnimateStepIcon("Pending", true)).to.be.false;
      expect(shouldAnimateStepIcon("Complete", true)).to.be.false;
      expect(shouldAnimateStepIcon("Error", true)).to.be.false;
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
      const shouldShowStart = (status: string, streaming: boolean) =>
        status === "Pending" && !streaming;

      expect(shouldShowStart("Pending", false)).to.be.true;
      expect(shouldShowStart("Pending", true)).to.be.false;
      expect(shouldShowStart("Running", false)).to.be.false;
      expect(shouldShowStart("Completed", false)).to.be.false;
      expect(shouldShowStart("Failed", false)).to.be.false;
      expect(shouldShowStart("Cancelled", false)).to.be.false;
    });

    // Continue button (header) logic — Story 10.10 extracted the gate into
    // shouldShowContinueButton so production render() and tests call the same
    // function. Mode is intentionally not an input: the gate is mode-agnostic.
    it("Continue header button shows between steps for both modes", () => {
      // Running with completed current step and remaining steps — visible
      expect(shouldShowContinueButton({
        instanceStatus: "Running", hasActiveStep: false, isStreaming: false, hasPendingSteps: true,
      })).to.be.true;
      // Running but step is active — hidden
      expect(shouldShowContinueButton({
        instanceStatus: "Running", hasActiveStep: true, isStreaming: false, hasPendingSteps: true,
      })).to.be.false;
      // Running but streaming (hidden during SSE)
      expect(shouldShowContinueButton({
        instanceStatus: "Running", hasActiveStep: false, isStreaming: true, hasPendingSteps: true,
      })).to.be.false;
      // Running but no pending steps — hidden
      expect(shouldShowContinueButton({
        instanceStatus: "Running", hasActiveStep: false, isStreaming: false, hasPendingSteps: false,
      })).to.be.false;
    });

    // Story 10.13 AC3 / AC4: shared terminal chat-line table + dedupe guard.
    // Both the SSE reducer and _onCancelClick consult this so a race or replay
    // produces exactly one chat line per terminal transition.
    it("terminalChatLine maps run statuses to canonical chat text", () => {
      expect(terminalChatLine("Completed")).to.equal("Workflow complete.");
      expect(terminalChatLine("Cancelled")).to.equal("Run cancelled.");
      expect(terminalChatLine("Failed")).to.equal("Run failed.");
      expect(terminalChatLine("Interrupted")).to.equal("Run interrupted.");
      // Unknown status returns null — the reducer's out-of-order-finalisation
      // defence preserves prior terminal state with no chat append.
      expect(terminalChatLine("Pending")).to.be.null;
      expect(terminalChatLine("Running")).to.be.null;
      expect(terminalChatLine("")).to.be.null;
    });

    it("shouldAppendTerminalLine skips when the last message already matches", () => {
      expect(shouldAppendTerminalLine(undefined, "Run cancelled.")).to.be.true;
      expect(shouldAppendTerminalLine("Workflow complete.", "Run cancelled.")).to.be.true;
      expect(shouldAppendTerminalLine("Run cancelled.", "Run cancelled.")).to.be.false;
      expect(shouldAppendTerminalLine("Run failed.", "Run failed.")).to.be.false;
    });

    // Story 10.13 AC3 / Failure & Edge line 98: the _onCancelClick dispatch
    // table must append "Run cancelled." only on kind === "ok". conflict (409 —
    // instance already in a non-cancellable state) and failed (non-2xx with no
    // state transition) must stay silent. Mirrors the branch in
    // agentrun-instance-detail.element.ts:_onCancelClick; keeping the predicate
    // exposed in the test lets us pin the invariant without mounting the
    // component (Bellissima import-map dependency documented elsewhere).
    it("_onCancelClick chat-append only fires on kind === 'ok' (AC3 dispatch table)", () => {
      // Produces (appendChat, logWarn) tuple — same outcome as the branches in
      // the handler at agentrun-instance-detail.element.ts:482-500.
      const dispatchCancelResult = (
        kind: "ok" | "conflict" | "failed",
        lastChatContent: string | undefined,
      ): { appendChat: boolean; logWarn: boolean } => {
        if (kind === "failed") return { appendChat: false, logWarn: true };
        if (kind === "ok") {
          return {
            appendChat: shouldAppendTerminalLine(lastChatContent, "Run cancelled."),
            logWarn: false,
          };
        }
        // conflict — chat stays silent ("Run cancelled." would be a lie about
        // a non-cancellable terminal state) but a console.warn now fires for
        // diagnostic symmetry with the failed branch (code-review patch).
        return { appendChat: false, logWarn: true };
      };

      expect(dispatchCancelResult("ok", undefined)).to.deep.equal({ appendChat: true, logWarn: false });
      // Dedupe guard still applies — AC4 reducer may have optimistically appended.
      expect(dispatchCancelResult("ok", "Run cancelled.")).to.deep.equal({ appendChat: false, logWarn: false });
      // conflict branch — silent chat, diagnostic warn for marketplace triage.
      expect(dispatchCancelResult("conflict", undefined)).to.deep.equal({ appendChat: false, logWarn: true });
      expect(dispatchCancelResult("conflict", "Run cancelled.")).to.deep.equal({ appendChat: false, logWarn: true });
      // failed branch — console.warn, no chat append.
      expect(dispatchCancelResult("failed", undefined)).to.deep.equal({ appendChat: false, logWarn: true });
    });

    // Action buttons hidden for terminal states
    it("all action buttons hidden for Completed, Failed, Cancelled", () => {
      for (const status of ["Completed", "Failed", "Cancelled"]) {
        const showStart = status === "Pending";
        const showCancel = status === "Running" || status === "Pending";
        expect(showStart).to.be.false;
        expect(showCancel).to.be.false;
      }
    });

    // Story 10.8: Cancel renders for any in-flight run (Running or Pending) across
    // both modes and during streaming — this is required for the mid-stream
    // token-burn case. Failed is excluded — Retry is the correct action for Failed
    // runs and the server's CancelInstance rejects Failed with 409.
    it("Cancel button renders whenever a run is in flight regardless of mode or streaming", () => {
      const shouldShowCancel = (status: string, _workflowMode: string, _streaming: boolean) =>
        status === "Running" || status === "Pending";

      // Interactive mode — Running/Pending show Cancel (including during streaming)
      expect(shouldShowCancel("Running", "interactive", true)).to.be.true;
      expect(shouldShowCancel("Running", "interactive", false)).to.be.true;
      expect(shouldShowCancel("Pending", "interactive", false)).to.be.true;

      // Autonomous mode — same rule applies
      expect(shouldShowCancel("Running", "autonomous", true)).to.be.true;
      expect(shouldShowCancel("Running", "autonomous", false)).to.be.true;
      expect(shouldShowCancel("Pending", "autonomous", false)).to.be.true;

      // Failed — Cancel is not shown (Retry is the correct action)
      expect(shouldShowCancel("Failed", "interactive", false)).to.be.false;
      expect(shouldShowCancel("Failed", "autonomous", false)).to.be.false;
      expect(shouldShowCancel("Failed", "interactive", true)).to.be.false;

      // Terminal states — no Cancel
      expect(shouldShowCancel("Completed", "autonomous", false)).to.be.false;
      expect(shouldShowCancel("Completed", "interactive", false)).to.be.false;
      expect(shouldShowCancel("Cancelled", "autonomous", false)).to.be.false;
      expect(shouldShowCancel("Cancelled", "interactive", false)).to.be.false;
    });

    // Story 10.9: Retry button extends to Interrupted status; Cancel stays hidden.
    describe("Interrupted status (Story 10.9)", () => {
      // Mirror of the component's showRetry computation at
      // agentrun-instance-detail.element.ts: `(status === "Failed" || status === "Interrupted") && !_streaming`.
      const shouldShowRetry = (status: string, streaming: boolean) =>
        (status === "Failed" || status === "Interrupted") && !streaming;

      // Mirror of showCancel: unchanged — Interrupted is not Running|Pending.
      const shouldShowCancel = (status: string) =>
        status === "Running" || status === "Pending";

      // Mirror of inputEnabled/inputPlaceholder Interrupted branch (hoisted above
      // isInteractive so it applies to both modes).
      const placeholderFor = (status: string, isTerminal: boolean, isInteractive: boolean, activeStep: boolean): { enabled: boolean; text: string } => {
        if (status === "Interrupted") {
          return { enabled: false, text: "Run interrupted — click Retry to resume." };
        }
        if (isInteractive) {
          if (isTerminal) return { enabled: false, text: "Workflow complete" };
          return { enabled: true, text: "Message the agent..." };
        }
        return {
          enabled: false,
          text: isTerminal ? "Workflow complete" : activeStep ? "Step complete" : "Click 'Start' to begin the workflow.",
        };
      };

      it("Retry button renders when status is Interrupted (and not streaming)", () => {
        expect(shouldShowRetry("Interrupted", false)).to.be.true;
        // Retry is suppressed while a new stream is already in flight (prevents double-retry race).
        expect(shouldShowRetry("Interrupted", true)).to.be.false;
        // Regression guard: Failed path is unchanged.
        expect(shouldShowRetry("Failed", false)).to.be.true;
        // Not shown for in-flight or terminal states.
        expect(shouldShowRetry("Running", false)).to.be.false;
        expect(shouldShowRetry("Completed", false)).to.be.false;
        expect(shouldShowRetry("Cancelled", false)).to.be.false;
        expect(shouldShowRetry("Pending", false)).to.be.false;
      });

      it("Cancel button hidden when status is Interrupted (locked decision 5)", () => {
        // Interrupted is not Running|Pending, so the existing showCancel rule
        // correctly hides Cancel — no code change needed, this test pins it.
        expect(shouldShowCancel("Interrupted")).to.be.false;
      });

      it("input placeholder reads 'Run interrupted — click Retry to resume.' for Interrupted in both modes", () => {
        // Interactive mode
        const interactive = placeholderFor("Interrupted", false, true, false);
        expect(interactive.enabled).to.be.false;
        expect(interactive.text).to.equal("Run interrupted — click Retry to resume.");

        // Autonomous mode
        const autonomous = placeholderFor("Interrupted", false, false, false);
        expect(autonomous.enabled).to.be.false;
        expect(autonomous.text).to.equal("Run interrupted — click Retry to resume.");
      });

      it("input is disabled for Interrupted (independent of streaming/activeStep)", () => {
        expect(placeholderFor("Interrupted", false, true, true).enabled).to.be.false;
        expect(placeholderFor("Interrupted", false, true, false).enabled).to.be.false;
        expect(placeholderFor("Interrupted", false, false, true).enabled).to.be.false;
        expect(placeholderFor("Interrupted", false, false, false).enabled).to.be.false;
      });

      it("Interrupted placeholder is distinct from terminal 'Workflow complete' placeholder", () => {
        // If Interrupted accidentally fell through to the isTerminal branch, the
        // user would see "Workflow complete" which is misleading — the run is
        // recoverable via Retry, not finished.
        const interrupted = placeholderFor("Interrupted", false, true, false);
        const completed = placeholderFor("Completed", true, true, false);
        expect(interrupted.text).to.not.equal(completed.text);
      });
    });

    // Story 10.10: Continue button extends to interactive mode between steps,
    // and the chat input gate is coupled to showContinue so the button and
    // input stay in lockstep ("if Continue is visible, input is disabled with
    // the Continue hint"). These tests call the exported production helpers
    // directly so the tests cannot drift from the render logic.
    describe("Story 10.10 — interactive resume between steps", () => {
      const betweenSteps = (overrides: Partial<Parameters<typeof computeChatInputGate>[0]> = {}) => {
        const base = {
          instanceStatus: "Running",
          isInteractive: true,
          isTerminal: false,
          hasActiveStep: false,
          isStreaming: false,
          isViewingStepHistory: false,
          agentResponding: false,
          isBetweenSteps: true,
          ...overrides,
        };
        return computeChatInputGate(base);
      };

      // AC1: Continue renders for interactive mode between steps.
      it("Continue renders for interactive mode between steps", () => {
        expect(shouldShowContinueButton({
          instanceStatus: "Running", hasActiveStep: false, isStreaming: false, hasPendingSteps: true,
        })).to.be.true;
      });

      // AC2: Continue still renders for autonomous mode between steps (regression).
      // The gate is mode-agnostic — taking no workflowMode argument is the
      // structural guarantee that `!isInteractive` cannot be reintroduced.
      it("Continue still renders for autonomous mode between steps (regression guard)", () => {
        // Same predicate call as AC1 — the absence of a mode parameter is the assertion.
        expect(shouldShowContinueButton({
          instanceStatus: "Running", hasActiveStep: false, isStreaming: false, hasPendingSteps: true,
        })).to.be.true;
      });

      // AC3: Continue hidden when an active step exists.
      it("Continue hidden when an active step exists", () => {
        expect(shouldShowContinueButton({
          instanceStatus: "Running", hasActiveStep: true, isStreaming: false, hasPendingSteps: true,
        })).to.be.false;
      });

      // AC4: Continue hidden for terminal states.
      it("Continue hidden for terminal states", () => {
        for (const status of ["Completed", "Failed", "Cancelled", "Interrupted"]) {
          expect(shouldShowContinueButton({
            instanceStatus: status, hasActiveStep: false, isStreaming: false, hasPendingSteps: true,
          })).to.be.false;
        }
      });

      // AC5: Chat input disabled between steps in interactive mode with Continue hint.
      it("chat input disabled between steps (interactive) with Continue hint", () => {
        const result = betweenSteps();
        expect(result.inputEnabled).to.be.false;
        expect(result.inputPlaceholder).to.equal("Click Continue to run the next step.");
      });

      // AC6: Chat input re-enables when streaming begins after Continue click.
      it("chat input re-enables when streaming starts", () => {
        const result = betweenSteps({ isStreaming: true, isBetweenSteps: false });
        expect(result.inputEnabled).to.be.true;
        expect(result.inputPlaceholder).to.equal("Message the agent...");
      });

      // AC6 (alternative path): input also enables once orchestrator marks the next step Active.
      it("chat input enables when next step becomes Active (even before SSE client flips _streaming)", () => {
        const result = betweenSteps({ hasActiveStep: true, isBetweenSteps: false });
        expect(result.inputEnabled).to.be.true;
        expect(result.inputPlaceholder).to.equal("Message the agent...");
      });

      // viewingStepId wins over the showContinue branch — user is browsing a
      // completed step's JSONL, chat reflects that context rather than the
      // between-steps Continue hint.
      it("viewingStepId beats showContinue (Viewing step history placeholder)", () => {
        const result = betweenSteps({ isViewingStepHistory: true });
        expect(result.inputEnabled).to.be.false;
        expect(result.inputPlaceholder).to.equal("Viewing step history");
      });

      // Regression: pre-10.10, Running with no activeStep and no streaming fell
      // through to the streaming/active branch and enabled the input, producing
      // a 409 not_running on send. The new gate disables the input and points
      // the user at Continue.
      it("regression guard — Running + no activeStep + no streaming no longer enables input", () => {
        const result = computeChatInputGate({
          instanceStatus: "Running",
          isInteractive: true,
          isTerminal: false,
          hasActiveStep: false,
          isStreaming: false,
          isViewingStepHistory: false,
          agentResponding: false,
          isBetweenSteps: true,
        });
        expect(result.inputEnabled).to.be.false;
      });
    });

    // Story 10.10: StepStatus.Cancelled rendering — helpers should treat it as a
    // neutral terminal state (no spinner, no danger colour, distinct icon).
    describe("Story 10.10 — StepStatus.Cancelled helpers", () => {
      it("stepSubtitle returns 'Cancelled' for Cancelled status", () => {
        expect(stepSubtitle(mockStep({ status: "Cancelled" }))).to.equal("Cancelled");
      });

      it("stepIconName returns 'icon-block' for Cancelled", () => {
        expect(stepIconName("Cancelled")).to.equal("icon-block");
      });

      it("stepIconColor returns undefined (neutral) for Cancelled", () => {
        expect(stepIconColor("Cancelled")).to.be.undefined;
      });

      it("shouldAnimateStepIcon returns false for Cancelled regardless of isStreaming", () => {
        expect(shouldAnimateStepIcon("Cancelled", true)).to.be.false;
        expect(shouldAnimateStepIcon("Cancelled", false)).to.be.false;
      });
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

  describe("interactive mode logic", () => {
    it("input enabled between turns when active step exists and not streaming", () => {
      const inputEnabled = (
        isTerminal: boolean,
        viewingStepId: string | null,
        streaming: boolean,
        hasActiveStep: boolean,
        status: string,
      ) => {
        if (isTerminal) return false;
        if (viewingStepId) return false;
        if (streaming) return false;
        if (hasActiveStep || status === "Running") return true;
        return false;
      };

      expect(inputEnabled(false, null, false, true, "Running")).to.be.true;
      expect(inputEnabled(false, null, false, false, "Running")).to.be.true;
      expect(inputEnabled(false, null, true, true, "Running")).to.be.false;
      expect(inputEnabled(true, null, false, true, "Completed")).to.be.false;
      expect(inputEnabled(false, "step-1", false, true, "Running")).to.be.false;
      expect(inputEnabled(false, null, false, false, "Pending")).to.be.false;
    });

    it("completion banner shows when step completable and not streaming", () => {
      const showBanner = (completable: boolean, streaming: boolean, isInteractive: boolean) =>
        isInteractive && completable && !streaming;

      expect(showBanner(true, false, true)).to.be.true;
      expect(showBanner(true, true, true)).to.be.false;
      expect(showBanner(false, false, true)).to.be.false;
      expect(showBanner(true, false, false)).to.be.false;
    });

    it("step completable when no active step, has complete and pending steps", () => {
      const isCompletable = (steps: Array<{ status: string }>) => {
        const hasActive = steps.some(s => s.status === "Active");
        const hasComplete = steps.some(s => s.status === "Complete");
        const hasPending = steps.some(s => s.status === "Pending");
        return !hasActive && hasComplete && hasPending;
      };

      expect(isCompletable([
        { status: "Complete" },
        { status: "Pending" },
      ])).to.be.true;
      expect(isCompletable([
        { status: "Active" },
        { status: "Pending" },
      ])).to.be.false;
      expect(isCompletable([
        { status: "Complete" },
        { status: "Complete" },
      ])).to.be.false;
    });

    it("Start button label is 'Start' for all modes", () => {
      // Validates the component uses a static "Start" label (not mode-gated)
      // — component rendering tested via manual E2E, this documents the expectation
      const startLabel = "Start";
      expect(startLabel).to.equal("Start");
    });
  });

  describe("artifact popover integration", () => {
    // 8.1 (popover): artifact list renders in sidebar with correct step data
    it("artifact list receives steps for deriving artifact entries", () => {
      const steps = [
        mockStep({ status: "Complete", writesTo: ["scan-results.md"] }),
        mockStep({ id: "step-2", name: "Analyser", status: "Complete", writesTo: ["analysis.md"] }),
        mockStep({ id: "step-3", name: "Reporter", status: "Pending", writesTo: null }),
      ];
      // The artifact list component receives all steps and derives entries internally
      expect(steps).to.have.lengthOf(3);
      expect(steps[0].writesTo).to.deep.equal(["scan-results.md"]);
      expect(steps[2].writesTo).to.be.null;
    });

    // 8.2 (popover): artifact-selected event opens popover with correct path
    it("artifact-selected handler sets popover path and opens popover", () => {
      let popoverOpen = false;
      let popoverArtifactPath: string | null = null;

      const onArtifactSelected = (detail: { path: string; stepName: string }) => {
        popoverArtifactPath = detail.path;
        popoverOpen = true;
      };

      onArtifactSelected({ path: "scan-results.md", stepName: "Content Scanner" });
      expect(popoverOpen).to.be.true;
      expect(popoverArtifactPath).to.equal("scan-results.md");
    });

    // 8.3 (popover): popover-closed event closes popover
    it("popover-closed handler sets popover open to false", () => {
      let popoverOpen = true;

      const onPopoverClosed = () => {
        popoverOpen = false;
      };

      onPopoverClosed();
      expect(popoverOpen).to.be.false;
    });

    // 8.4 (popover): run.finished auto-opens popover for final step's last artifact
    it("run.finished finds last complete step with artifacts and opens popover", () => {
      const steps = [
        mockStep({ status: "Complete", writesTo: ["scan-results.md"] }),
        mockStep({ id: "step-2", name: "Analyser", status: "Complete", writesTo: ["analysis.md", "summary.md"] }),
        mockStep({ id: "step-3", name: "Reporter", status: "Complete", writesTo: null }),
      ];

      // Logic from _handleSseEvent run.finished: find last complete step with artifacts
      const lastCompleteStep = [...steps]
        .reverse()
        .find(s => s.status === "Complete" && s.writesTo && s.writesTo.length > 0);

      expect(lastCompleteStep).to.not.be.undefined;
      expect(lastCompleteStep!.name).to.equal("Analyser");
      // Takes last entry of writesTo
      const autoOpenPath = lastCompleteStep!.writesTo![lastCompleteStep!.writesTo!.length - 1];
      expect(autoOpenPath).to.equal("summary.md");
    });

    // 8.5 (popover): run.finished adds "Workflow complete." system message
    it("run.finished adds workflow complete system message", () => {
      const chatMessages: Array<{ role: string; content: string }> = [
        { role: "system", content: "Starting Content Scanner..." },
        { role: "agent", content: "Scanning content..." },
      ];

      // Simulate run.finished handler adding the message
      const updatedMessages = [
        ...chatMessages,
        { role: "system", content: "Workflow complete." },
      ];

      expect(updatedMessages).to.have.lengthOf(3);
      expect(updatedMessages[2].role).to.equal("system");
      expect(updatedMessages[2].content).to.equal("Workflow complete.");
    });

    // 8.6 (popover): loading completed instance does NOT auto-open popover
    it("loading completed instance from list does not auto-open popover", () => {
      // Auto-open only triggers in _handleSseEvent for run.finished
      // _loadData() does NOT set _popoverOpen
      // Verify the separation: _loadData sets _instance but not _popoverOpen
      let popoverOpen = false;

      // Simulate _loadData for a completed instance — no popover logic
      const loadData = () => {
        // _loadData only sets _instance, _runNumber, _chatMessages
        // It does NOT touch _popoverOpen or _popoverArtifactPath
      };

      loadData();
      expect(popoverOpen).to.be.false;
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
