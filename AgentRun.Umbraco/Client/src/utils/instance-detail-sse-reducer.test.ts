import { expect } from "@open-wc/testing";
import type { InstanceDetailResponse } from "../api/types.js";
import { initialInstanceDetailState, type InstanceDetailState } from "./instance-detail-store.js";
import { reduceSseEvent, finaliseStreamingMessage } from "./instance-detail-sse-reducer.js";

describe("instance-detail-sse-reducer", () => {
  const FIXED_NOW = "2026-04-15T12:00:00.000Z";
  const now = () => FIXED_NOW;

  const instance = (overrides: Partial<InstanceDetailResponse> = {}): InstanceDetailResponse => ({
    id: "inst-1",
    workflowAlias: "content-audit",
    workflowName: "Content Audit",
    workflowMode: "interactive",
    status: "Running",
    currentStepIndex: 0,
    createdAt: "2026-04-15T11:59:00.000Z",
    updatedAt: "2026-04-15T11:59:00.000Z",
    createdBy: "admin",
    steps: [
      { id: "scanner", name: "Scanner", status: "Active", startedAt: null, completedAt: null, writesTo: null },
      { id: "analyser", name: "Analyser", status: "Pending", startedAt: null, completedAt: null, writesTo: null },
    ],
    ...overrides,
  });

  const withInstance = (overrides: Partial<InstanceDetailState> = {}): InstanceDetailState => ({
    ...initialInstanceDetailState(),
    instance: instance(),
    loading: false,
    streaming: true,
    ...overrides,
  });

  describe("text.delta", () => {
    it("seeds a new streaming agent message when none is open", () => {
      const state = withInstance();
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "Hello" } }, { now });
      expect(next.chatMessages).to.have.lengthOf(1);
      expect(next.chatMessages[0]).to.deep.equal({
        role: "agent",
        content: "Hello",
        timestamp: FIXED_NOW,
        isStreaming: true,
      });
      expect(next.streamingText).to.equal("Hello");
      expect(next.agentResponding).to.be.true;
      expect(next.toolBatchOpen).to.be.false;
    });

    it("appends to the currently streaming agent message", () => {
      const state = withInstance({
        chatMessages: [{ role: "agent", content: "Hel", timestamp: FIXED_NOW, isStreaming: true }],
        streamingText: "Hel",
      });
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "lo" } }, { now });
      expect(next.chatMessages).to.have.lengthOf(1);
      expect(next.chatMessages[0].content).to.equal("Hello");
      expect(next.streamingText).to.equal("Hello");
    });

    it("ignores empty content (no state change, no message spawn)", () => {
      const state = withInstance({
        chatMessages: [{ role: "agent", content: "Hello", timestamp: FIXED_NOW, isStreaming: false }],
      });
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "" } }, { now });
      expect(next.chatMessages).to.have.lengthOf(1);
      expect(next.streamingText).to.equal("");
      // Still marks agentResponding since we received an agent event.
      expect(next.agentResponding).to.be.true;
    });

    it("F5 — delta after finalise creates a new streaming message (never silently drops)", () => {
      // Previous message finalised — isStreaming: false. New delta must spawn
      // a fresh streaming message rather than drop onto the closed one.
      const state = withInstance({
        chatMessages: [{ role: "agent", content: "Done.", timestamp: FIXED_NOW, isStreaming: false }],
      });
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "More" } }, { now });
      expect(next.chatMessages).to.have.lengthOf(2);
      expect(next.chatMessages[1].content).to.equal("More");
      expect(next.chatMessages[1].isStreaming).to.be.true;
    });
  });

  describe("tool.start", () => {
    it("creates a new agent message with the tool call when no prior tool batch is open", () => {
      const state = withInstance();
      const next = reduceSseEvent(
        state,
        { event: "tool.start", data: { toolCallId: "tc1", toolName: "read_file" } },
        { now },
      );
      expect(next.chatMessages).to.have.lengthOf(1);
      expect(next.chatMessages[0].toolCalls).to.have.lengthOf(1);
      expect(next.chatMessages[0].toolCalls![0].status).to.equal("running");
      expect(next.chatMessages[0].toolCalls![0].toolName).to.equal("read_file");
      expect(next.toolBatchOpen).to.be.true;
      expect(next.agentResponding).to.be.true;
    });

    it("attaches to the existing agent message when a tool batch is open", () => {
      const state = withInstance({
        chatMessages: [
          {
            role: "agent",
            content: "",
            timestamp: FIXED_NOW,
            toolCalls: [{ toolCallId: "tc1", toolName: "read_file", summary: "read_file", arguments: null, result: null, status: "complete" }],
          },
        ],
        toolBatchOpen: true,
      });
      const next = reduceSseEvent(
        state,
        { event: "tool.start", data: { toolCallId: "tc2", toolName: "list_content" } },
        { now },
      );
      expect(next.chatMessages).to.have.lengthOf(1);
      expect(next.chatMessages[0].toolCalls).to.have.lengthOf(2);
    });

    it("finalises an active streaming message before attaching the tool call", () => {
      const state = withInstance({
        chatMessages: [{ role: "agent", content: "Intro", timestamp: FIXED_NOW, isStreaming: true }],
        streamingText: "Intro",
      });
      const next = reduceSseEvent(
        state,
        { event: "tool.start", data: { toolCallId: "tc1", toolName: "read_file" } },
        { now },
      );
      expect(next.chatMessages[0].isStreaming).to.be.false;
      expect(next.chatMessages[0].content).to.equal("Intro");
      expect(next.chatMessages[0].toolCalls).to.have.lengthOf(1);
      expect(next.streamingText).to.equal("");
    });
  });

  describe("tool.args / tool.end / tool.result", () => {
    const withToolCall = () =>
      withInstance({
        chatMessages: [
          {
            role: "agent",
            content: "",
            timestamp: FIXED_NOW,
            toolCalls: [{ toolCallId: "tc1", toolName: "read_file", summary: "read_file", arguments: null, result: null, status: "running" }],
          },
        ],
        toolBatchOpen: true,
      });

    it("tool.args sets arguments + derives summary", () => {
      const state = withToolCall();
      const next = reduceSseEvent(
        state,
        { event: "tool.args", data: { toolCallId: "tc1", arguments: { path: "/foo/bar.md" } } },
      );
      expect(next.chatMessages[0].toolCalls![0].arguments).to.deep.equal({ path: "/foo/bar.md" });
      expect(next.chatMessages[0].toolCalls![0].summary).to.equal("bar.md");
    });

    it("tool.end transitions a running tool call to complete", () => {
      const state = withToolCall();
      const next = reduceSseEvent(state, { event: "tool.end", data: { toolCallId: "tc1" } });
      expect(next.chatMessages[0].toolCalls![0].status).to.equal("complete");
    });

    it("tool.result sets result text and marks complete on normal output", () => {
      const state = withToolCall();
      const next = reduceSseEvent(
        state,
        { event: "tool.result", data: { toolCallId: "tc1", result: "file contents" } },
      );
      expect(next.chatMessages[0].toolCalls![0].result).to.equal("file contents");
      expect(next.chatMessages[0].toolCalls![0].status).to.equal("complete");
    });

    it("tool.result marks error when result starts with \"Tool '...'\" and contains error/failed", () => {
      const state = withToolCall();
      const next = reduceSseEvent(
        state,
        { event: "tool.result", data: { toolCallId: "tc1", result: "Tool 'read_file' failed: ENOENT" } },
      );
      expect(next.chatMessages[0].toolCalls![0].status).to.equal("error");
    });

    it("tool.args / tool.end / tool.result no-op when tool call id is unknown", () => {
      const state = withToolCall();
      const a = reduceSseEvent(state, { event: "tool.args", data: { toolCallId: "missing", arguments: {} } });
      const b = reduceSseEvent(state, { event: "tool.end", data: { toolCallId: "missing" } });
      const c = reduceSseEvent(state, { event: "tool.result", data: { toolCallId: "missing", result: "x" } });
      expect(a).to.equal(state);
      expect(b).to.equal(state);
      expect(c).to.equal(state);
    });
  });

  describe("step lifecycle", () => {
    it("step.started marks the step Active and emits a starting system message", () => {
      const state = withInstance({
        instance: instance({ steps: [
          { id: "scanner", name: "Scanner", status: "Pending", startedAt: null, completedAt: null, writesTo: null },
        ]}),
      });
      const next = reduceSseEvent(
        state,
        { event: "step.started", data: { stepId: "scanner", stepName: "Scanner" } },
        { now },
      );
      expect(next.instance!.steps[0].status).to.equal("Active");
      expect(next.chatMessages.at(-1)).to.deep.equal({
        role: "system",
        content: "Starting Scanner...",
        timestamp: FIXED_NOW,
      });
      expect(next.toolBatchOpen).to.be.false;
    });

    it("step.finished marks the step with the provided status and emits a completion message", () => {
      const state = withInstance();
      const next = reduceSseEvent(
        state,
        { event: "step.finished", data: { stepId: "scanner", stepName: "Scanner", status: "Complete" } },
        { now },
      );
      expect(next.instance!.steps[0].status).to.equal("Complete");
      expect(next.chatMessages.at(-1)!.content).to.equal("Scanner completed");
    });

    it("step.finished with Error status uses 'failed' in the message", () => {
      const state = withInstance();
      const next = reduceSseEvent(
        state,
        { event: "step.finished", data: { stepId: "scanner", stepName: "Scanner", status: "Error" } },
        { now },
      );
      expect(next.chatMessages.at(-1)!.content).to.equal("Scanner failed");
    });
  });

  describe("run lifecycle", () => {
    it("run.finished marks instance Completed and appends 'Workflow complete.'", () => {
      const state = withInstance();
      const next = reduceSseEvent(state, { event: "run.finished", data: {} }, { now });
      expect(next.instance!.status).to.equal("Completed");
      expect(next.chatMessages.at(-1)!.content).to.equal("Workflow complete.");
    });

    it("run.error marks instance Failed and emits the provided error message", () => {
      const state = withInstance();
      const next = reduceSseEvent(
        state,
        { event: "run.error", data: { message: "Provider timeout" } },
        { now },
      );
      expect(next.instance!.status).to.equal("Failed");
      expect(next.chatMessages.at(-1)!.content).to.equal("Provider timeout");
    });

    it("run.error falls back to a generic message when none is supplied", () => {
      const state = withInstance();
      const next = reduceSseEvent(state, { event: "run.error", data: {} }, { now });
      expect(next.chatMessages.at(-1)!.content).to.equal("An error occurred.");
    });

    it("input.wait clears agentResponding and finalises any open streaming message", () => {
      const state = withInstance({
        agentResponding: true,
        chatMessages: [{ role: "agent", content: "Hi", timestamp: FIXED_NOW, isStreaming: true }],
        streamingText: "Hi",
      });
      const next = reduceSseEvent(state, { event: "input.wait", data: {} });
      expect(next.agentResponding).to.be.false;
      expect(next.chatMessages[0].isStreaming).to.be.false;
      expect(next.streamingText).to.equal("");
    });

    it("user.message is a no-op (server echo ignored, already added optimistically)", () => {
      const state = withInstance({
        chatMessages: [{ role: "user", content: "hi", timestamp: FIXED_NOW }],
      });
      const next = reduceSseEvent(state, { event: "user.message", data: { message: "hi" } });
      expect(next).to.equal(state);
    });

    it("unknown event type returns the state unchanged (F1 — forward compatibility)", () => {
      const state = withInstance();
      const next = reduceSseEvent(state, { event: "future.unknown", data: {} });
      expect(next).to.equal(state);
    });
  });

  describe("Track G — retry clears stale Failed / Interrupted status (AC5)", () => {
    it("Failed → text.delta flips instance.status to Running", () => {
      const state = withInstance({ instance: instance({ status: "Failed" }) });
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "x" } }, { now });
      expect(next.instance!.status).to.equal("Running");
    });

    it("Interrupted → text.delta flips instance.status to Running", () => {
      const state = withInstance({ instance: instance({ status: "Interrupted" }) });
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "x" } }, { now });
      expect(next.instance!.status).to.equal("Running");
    });

    it("Failed → tool.start flips instance.status to Running", () => {
      const state = withInstance({ instance: instance({ status: "Failed" }) });
      const next = reduceSseEvent(
        state,
        { event: "tool.start", data: { toolCallId: "tc1", toolName: "read_file" } },
        { now },
      );
      expect(next.instance!.status).to.equal("Running");
    });

    it("Running → text.delta is idempotent (F4 — no state churn, double-click safe)", () => {
      const state = withInstance({ instance: instance({ status: "Running" }) });
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "x" } }, { now });
      expect(next.instance!.status).to.equal("Running");
    });

    it("Completed → text.delta leaves status Completed (no regression)", () => {
      // Not a realistic event order, but the reducer must not rewrite a
      // terminal success state into Running.
      const state = withInstance({ instance: instance({ status: "Completed" }) });
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "x" } }, { now });
      expect(next.instance!.status).to.equal("Completed");
    });
  });

  describe("finaliseStreamingMessage helper", () => {
    it("returns state unchanged when there is no streaming text", () => {
      const state = withInstance();
      expect(finaliseStreamingMessage(state)).to.equal(state);
    });

    it("seals the last streaming agent message with accumulated text", () => {
      const state = withInstance({
        chatMessages: [{ role: "agent", content: "Hel", timestamp: FIXED_NOW, isStreaming: true }],
        streamingText: "Hello",
      });
      const next = finaliseStreamingMessage(state);
      expect(next.chatMessages[0].content).to.equal("Hello");
      expect(next.chatMessages[0].isStreaming).to.be.false;
      expect(next.streamingText).to.equal("");
    });
  });
});
