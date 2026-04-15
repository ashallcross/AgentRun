import { expect } from "@open-wc/testing";
import type { ChatMessage } from "../api/types.js";

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
});
