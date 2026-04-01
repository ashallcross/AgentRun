import { expect } from "@open-wc/testing";
import { mapConversationToChat } from "./api/api-client.js";
import type { ConversationEntryResponse, ChatMessage, ToolCallData } from "./api/types.js";
import { extractToolSummary } from "./utils/tool-helpers.js";

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

  it("creates agent message for assistant entries with toolCallId (tool calls)", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "assistant", content: null, timestamp: "2026-04-01T10:00:00Z", toolCallId: "c1", toolName: "read_file" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(1);
    expect(result[0].role).to.equal("agent");
    expect(result[0].content).to.equal("");
    expect(result[0].toolCalls).to.have.length(1);
    expect(result[0].toolCalls![0].toolName).to.equal("read_file");
  });

  it("handles mixed entries correctly with tool calls attached", () => {
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
    expect(result[1].toolCalls).to.have.length(1);
    expect(result[1].toolCalls![0].toolName).to.equal("read_file");
    expect(result[1].toolCalls![0].result).to.equal("file contents");
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

describe("mapConversationToChat with tool calls", () => {
  it("attaches tool calls to preceding agent messages", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "assistant", content: "I will read the file", timestamp: "2026-04-01T10:00:00Z" },
      { role: "assistant", content: null, timestamp: "2026-04-01T10:00:01Z", toolCallId: "c1", toolName: "read_file", toolArguments: '{"path":"src/main.ts"}' },
      { role: "tool", content: null, timestamp: "2026-04-01T10:00:02Z", toolCallId: "c1", toolResult: "file contents here" },
      { role: "assistant", content: "Done!", timestamp: "2026-04-01T10:00:03Z" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(2);
    expect(result[0].content).to.equal("I will read the file");
    expect(result[0].toolCalls).to.have.length(1);
    expect(result[0].toolCalls![0].toolName).to.equal("read_file");
    expect(result[0].toolCalls![0].result).to.equal("file contents here");
    expect(result[0].toolCalls![0].status).to.equal("complete");
    expect(result[1].content).to.equal("Done!");
  });

  it("creates empty-content agent message when tool call has no preceding text", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "system", content: "Starting step...", timestamp: "2026-04-01T10:00:00Z" },
      { role: "assistant", content: null, timestamp: "2026-04-01T10:00:01Z", toolCallId: "c1", toolName: "read_file", toolArguments: '{"path":"file.md"}' },
      { role: "tool", content: null, timestamp: "2026-04-01T10:00:02Z", toolCallId: "c1", toolResult: "contents" },
    ];
    const result = mapConversationToChat(entries);
    expect(result).to.have.length(2);
    expect(result[0].role).to.equal("system");
    expect(result[1].role).to.equal("agent");
    expect(result[1].content).to.equal("");
    expect(result[1].toolCalls).to.have.length(1);
  });

  it("sets error status for tool results containing error strings", () => {
    const entries: ConversationEntryResponse[] = [
      { role: "assistant", content: "Reading file", timestamp: "2026-04-01T10:00:00Z" },
      { role: "assistant", content: null, timestamp: "2026-04-01T10:00:01Z", toolCallId: "c1", toolName: "read_file", toolArguments: '{"path":"missing.txt"}' },
      { role: "tool", content: null, timestamp: "2026-04-01T10:00:02Z", toolCallId: "c1", toolResult: "Tool 'read_file' error: file not found" },
    ];
    const result = mapConversationToChat(entries);
    expect(result[0].toolCalls![0].status).to.equal("error");
    expect(result[0].toolCalls![0].result).to.contain("error");
  });
});

describe("extractToolSummary", () => {
  it("extracts filename from path argument", () => {
    const result = extractToolSummary("read_file", { path: "src/components/main.ts" });
    expect(result).to.equal("main.ts");
  });

  it("returns URL for url argument", () => {
    const result = extractToolSummary("fetch_url", { url: "https://example.com/api" });
    expect(result).to.equal("https://example.com/api");
  });

  it("falls back to toolName when no path or url", () => {
    const result = extractToolSummary("custom_tool", { count: 42 });
    expect(result).to.equal("custom_tool");
  });

  it("falls back to toolName when args is null", () => {
    const result = extractToolSummary("write_file", null);
    expect(result).to.equal("write_file");
  });
});

describe("Live SSE tool event sequence", () => {
  it("creates ToolCallData correctly through tool.start -> tool.args -> tool.end -> tool.result", () => {
    // Simulate the SSE event handling logic from instance-detail
    let chatMessages: ChatMessage[] = [
      { role: "agent", content: "I will read the file", timestamp: "2026-04-01T10:00:00Z" },
    ];

    // tool.start
    const newToolCall: ToolCallData = {
      toolCallId: "tc_001",
      toolName: "read_file",
      summary: "read_file",
      arguments: null,
      result: null,
      status: "running",
    };
    const lastMsg = chatMessages[chatMessages.length - 1];
    chatMessages = [
      ...chatMessages.slice(0, -1),
      { ...lastMsg, toolCalls: [...(lastMsg.toolCalls ?? []), newToolCall] },
    ];

    expect(chatMessages[0].toolCalls).to.have.length(1);
    expect(chatMessages[0].toolCalls![0].status).to.equal("running");

    // tool.args
    const args = { path: "src/main.ts" };
    chatMessages = chatMessages.map(m => {
      const tc = m.toolCalls?.find(t => t.toolCallId === "tc_001");
      if (!tc) return m;
      return {
        ...m,
        toolCalls: m.toolCalls!.map(t =>
          t.toolCallId === "tc_001"
            ? { ...t, arguments: args, summary: extractToolSummary(t.toolName, args) }
            : t
        ),
      };
    });

    expect(chatMessages[0].toolCalls![0].summary).to.equal("main.ts");
    expect(chatMessages[0].toolCalls![0].arguments).to.deep.equal({ path: "src/main.ts" });

    // tool.end
    chatMessages = chatMessages.map(m => ({
      ...m,
      toolCalls: m.toolCalls?.map(t =>
        t.toolCallId === "tc_001" && t.status === "running"
          ? { ...t, status: "complete" as const }
          : t
      ),
    }));

    expect(chatMessages[0].toolCalls![0].status).to.equal("complete");

    // tool.result
    chatMessages = chatMessages.map(m => ({
      ...m,
      toolCalls: m.toolCalls?.map(t =>
        t.toolCallId === "tc_001"
          ? { ...t, result: "file contents here", status: "complete" as const }
          : t
      ),
    }));

    expect(chatMessages[0].toolCalls![0].result).to.equal("file contents here");
    expect(chatMessages[0].toolCalls![0].status).to.equal("complete");
  });
});
