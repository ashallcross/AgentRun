import { expect } from "@open-wc/testing";
import { mapConversationToChat } from "./api/api-client.js";
import type { ConversationEntryResponse, ChatMessage } from "./api/types.js";

describe("ChatMessage type mapping", () => {
  it("maps assistant text entries to agent role", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "assistant", content: "Hello", timestamp: "2026-04-01T10:00:00Z" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(1);
    expect(result[0].role).to.equal("agent");
    expect(result[0].content).to.equal("Hello");
  });

  it("maps system entries to system role", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "system", content: "Step started", timestamp: "2026-04-01T10:00:00Z" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(1);
    expect(result[0].role).to.equal("system");
  });

  it("skips tool entries", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "tool", content: null, timestamp: "2026-04-01T10:00:00Z", toolCallId: "c1", toolResult: "ok" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(0);
  });

  it("skips assistant entries with toolCallId (tool calls, not text)", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "assistant", content: null, timestamp: "2026-04-01T10:00:00Z", toolCallId: "c1", toolName: "read_file" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(0);
  });

  it("handles mixed entries correctly", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "system", content: "Starting step...", timestamp: "2026-04-01T10:00:00Z" },
      { role: "assistant", content: "I will read the file", timestamp: "2026-04-01T10:00:01Z" },
      { role: "assistant", content: null, timestamp: "2026-04-01T10:00:02Z", toolCallId: "c1", toolName: "read_file" },
      { role: "tool", content: null, timestamp: "2026-04-01T10:00:03Z", toolCallId: "c1", toolResult: "file contents" },
      { role: "assistant", content: "Done!", timestamp: "2026-04-01T10:00:04Z" },
      { role: "system", content: "Step completed", timestamp: "2026-04-01T10:00:05Z" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(4);
    expect(result[0].role).to.equal("system");
    expect(result[1].role).to.equal("agent");
    expect(result[1].content).to.equal("I will read the file");
    expect(result[2].role).to.equal("agent");
    expect(result[2].content).to.equal("Done!");
    expect(result[3].role).to.equal("system");
  });
});

describe("Streaming text accumulation", () => {
  it("accumulates text.delta events into a single message", () => {
    // Simulates the logic in instance-detail._handleSseEvent
    let streamingText = "";
    const messages: ChatMessage[] = [];

    const deltas = ["Hello", " ", "world", "!"];
    for (const delta of deltas) {
      streamingText += delta;
      const last = messages[messages.length - 1];
      if (last && last.role === "agent" && last.isStreaming) {
        last.content = streamingText;
      } else {
        messages.push({
          role: "agent",
          content: streamingText,
          timestamp: new Date().toISOString(),
          isStreaming: true,
        });
      }
    }

    expect(messages).to.have.length(1);
    expect(messages[0].content).to.equal("Hello world!");
    expect(messages[0].isStreaming).to.be.true;
  });
});

describe("Finalise streaming message on tool.start", () => {
  it("clears streaming text and keeps message content", () => {
    let streamingText = "I will read the file";
    const messages: ChatMessage[] = [
      { role: "agent", content: streamingText, timestamp: new Date().toISOString(), isStreaming: true },
    ];

    // Simulate _finaliseStreamingMessage
    if (streamingText) {
      const last = messages[messages.length - 1];
      if (last && last.role === "agent") {
        last.content = streamingText;
      }
      streamingText = "";
    }

    expect(messages[0].content).to.equal("I will read the file");
    expect(streamingText).to.equal("");
  });

  it("is idempotent when streamingText is empty", () => {
    let streamingText = "";
    const messages: ChatMessage[] = [
      { role: "agent", content: "previous", timestamp: new Date().toISOString() },
    ];

    // Simulate _finaliseStreamingMessage — should be no-op
    if (streamingText) {
      const last = messages[messages.length - 1];
      if (last && last.role === "agent") {
        last.content = streamingText;
      }
      streamingText = "";
    }

    expect(messages[0].content).to.equal("previous");
  });
});

describe("Auto-scroll threshold", () => {
  it("detects near-bottom within 50px threshold", () => {
    const scrollHeight = 1000;
    const clientHeight = 500;

    // At bottom
    let scrollTop = 500;
    let isNearBottom = scrollHeight - scrollTop - clientHeight < 50;
    expect(isNearBottom).to.be.true;

    // Within threshold
    scrollTop = 460;
    isNearBottom = scrollHeight - scrollTop - clientHeight < 50;
    expect(isNearBottom).to.be.true;

    // Beyond threshold
    scrollTop = 400;
    isNearBottom = scrollHeight - scrollTop - clientHeight < 50;
    expect(isNearBottom).to.be.false;
  });
});
