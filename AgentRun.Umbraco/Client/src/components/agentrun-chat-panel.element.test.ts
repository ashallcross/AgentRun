import { expect } from "@open-wc/testing";
import type { ChatMessage, InstanceDetailResponse } from "../api/types.js";
import { initialInstanceDetailState } from "../utils/instance-detail-store.js";
import { reduceSseEvent } from "../utils/instance-detail-sse-reducer.js";
import { computeChatInputGate } from "../utils/instance-detail-helpers.js";

describe("agentrun-chat-panel", () => {
  // Track F — chat cursor visibility predicate mirrors the attribute expression
  // at agentrun-chat-panel.element.ts:184. Tests assert the exact condition the
  // element evaluates, so any drift requires updating both sides in lockstep.
  describe("Track F — cursor visibility (AC3)", () => {
    const cursorOn = (
      i: number,
      lastIndex: number,
      role: ChatMessage["role"],
      isStreaming: boolean,
      msgIsStreaming: boolean | undefined,
    ): boolean =>
      i === lastIndex && role === "agent" && isStreaming && msgIsStreaming === true;

    it("cursor present when both connection- and message-level streaming are true", () => {
      expect(cursorOn(0, 0, "agent", true, true)).to.be.true;
    });

    it("cursor absent when connection is streaming but the message is not (tool-call / waiting state)", () => {
      // This is the Tom Madden beta repro: SSE connection alive throughout the
      // run, but the assistant message isStreaming flips false the moment a
      // tool.start fires. Pre-fix the cursor flashed throughout tool execution.
      expect(cursorOn(0, 0, "agent", true, false)).to.be.false;
    });

    it("cursor absent when msg.isStreaming is undefined (history / non-streaming message)", () => {
      expect(cursorOn(0, 0, "agent", true, undefined)).to.be.false;
    });

    it("cursor absent when the connection is not streaming regardless of msg flag", () => {
      expect(cursorOn(0, 0, "agent", false, true)).to.be.false;
    });

    it("cursor absent on user / system messages even if they were last", () => {
      expect(cursorOn(0, 0, "user", true, true)).to.be.false;
      expect(cursorOn(0, 0, "system", true, true)).to.be.false;
    });

    it("cursor absent on non-last messages even when the last message is streaming", () => {
      // last=2 → message at index 0 or 1 cannot show the cursor, only index 2.
      expect(cursorOn(0, 2, "agent", true, true)).to.be.false;
      expect(cursorOn(1, 2, "agent", true, true)).to.be.false;
      expect(cursorOn(2, 2, "agent", true, true)).to.be.true;
    });
  });

  // Track G — Failed / Interrupted retry must clear the stale "Run failed …" /
  // "Run interrupted …" banner AND re-enable the chat input the moment the
  // retry LLM turn starts, not when the step completes. The fix lives in the
  // instance-detail SSE reducer; these tests walk the full composition from
  // a retry-start SSE event through computeChatInputGate to prove the UI
  // surface inputs are now correct on both evidence paths.
  describe("Track G — retry clears stale banner and re-enables input (AC5)", () => {
    const instance = (status: string): InstanceDetailResponse => ({
      id: "inst-1",
      workflowAlias: "content-audit",
      workflowName: "Content Audit",
      workflowMode: "interactive",
      status,
      currentStepIndex: 0,
      createdAt: "2026-04-15T11:59:00.000Z",
      updatedAt: "2026-04-15T11:59:00.000Z",
      createdBy: "admin",
      steps: [
        { id: "scanner", name: "Scanner", status: "Active", startedAt: null, completedAt: null, writesTo: null },
      ],
    });

    const gateFor = (status: string, agentResponding: boolean) =>
      computeChatInputGate({
        instanceStatus: status,
        isInteractive: true,
        isTerminal: status === "Completed" || status === "Failed" || status === "Cancelled",
        hasActiveStep: true,
        isStreaming: true,
        isViewingStepHistory: false,
        agentResponding,
        isBetweenSteps: false,
      });

    it("reducer unit — Failed → text.delta flips instance.status to Running", () => {
      const state = { ...initialInstanceDetailState(), instance: instance("Failed"), streaming: true };
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "Hello" } });
      expect(next.instance!.status).to.equal("Running");
    });

    it("reducer unit — Interrupted → text.delta flips instance.status to Running", () => {
      const state = { ...initialInstanceDetailState(), instance: instance("Interrupted"), streaming: true };
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "Hello" } });
      expect(next.instance!.status).to.equal("Running");
    });

    it("Failed→Retry path — stale banner replaced with live 'Agent is responding…' during the retry turn", () => {
      // Pre-retry: stale banner text + disabled input (the AC5 regression baseline).
      const pre = gateFor("Failed", false);
      expect(pre.inputEnabled).to.be.false;
      expect(pre.inputPlaceholder).to.equal("Run failed — click Retry to resume.");

      // First post-retry text.delta flips status AND sets agentResponding true.
      const state = { ...initialInstanceDetailState(), instance: instance("Failed"), streaming: true };
      const next = reduceSseEvent(state, { event: "text.delta", data: { content: "x" } });

      // Gate now reflects live state: stale banner gone, "Agent is responding…"
      // takes over. Input stays disabled while the agent is actively responding —
      // that's the correct live UX, not the stale-banner disabled state.
      const post = gateFor(next.instance!.status, next.agentResponding);
      expect(post.inputPlaceholder).to.equal("Agent is responding...");
      expect(post.inputPlaceholder).to.not.equal("Run failed — click Retry to resume.");
    });

    it("Interrupted→Retry path — stale banner replaced with live 'Agent is responding…' during the retry turn", () => {
      const pre = gateFor("Interrupted", false);
      expect(pre.inputEnabled).to.be.false;
      expect(pre.inputPlaceholder).to.equal("Run interrupted — click Retry to resume.");

      const state = { ...initialInstanceDetailState(), instance: instance("Interrupted"), streaming: true };
      const next = reduceSseEvent(state, { event: "tool.start", data: { toolCallId: "tc1", toolName: "read_file" } });

      const post = gateFor(next.instance!.status, next.agentResponding);
      expect(post.inputPlaceholder).to.equal("Agent is responding...");
      expect(post.inputPlaceholder).to.not.equal("Run interrupted — click Retry to resume.");
    });

    it("Input re-enables with live placeholder once the retry turn yields (input.wait)", () => {
      // Simulate the full retry turn: Failed → text.delta (reducer fires) →
      // agent finishes responding → input.wait clears agentResponding. The
      // gate then reports enabled input + "Message the agent..." placeholder.
      let state = { ...initialInstanceDetailState(), instance: instance("Failed"), streaming: true };
      state = reduceSseEvent(state, { event: "text.delta", data: { content: "hello" } });
      state = reduceSseEvent(state, { event: "input.wait", data: {} });
      const post = gateFor(state.instance!.status, state.agentResponding);
      expect(post.inputEnabled).to.be.true;
      expect(post.inputPlaceholder).to.equal("Message the agent...");
    });

    it("Placeholder never reverts to the stale retry banner after double-click on Retry (F4)", () => {
      // F4 — double-click safety. Fire two retry-start events back to back;
      // the second is a no-op against already-cleared state and the gate
      // never reverts to the stale banner text.
      let state = { ...initialInstanceDetailState(), instance: instance("Failed"), streaming: true };
      state = reduceSseEvent(state, { event: "text.delta", data: { content: "a" } });
      state = reduceSseEvent(state, { event: "text.delta", data: { content: "b" } });
      const post = gateFor(state.instance!.status, state.agentResponding);
      expect(post.inputPlaceholder).to.not.equal("Run failed — click Retry to resume.");
      expect(post.inputPlaceholder).to.not.equal("Run interrupted — click Retry to resume.");
    });
  });
});
