import type { ChatMessage, InstanceDetailResponse, StepDetailResponse, ToolCallData } from "../api/types.js";
import { extractToolSummary } from "./tool-helpers.js";
import type { InstanceDetailState } from "./instance-detail-store.js";

export type SseEvent = {
  event: string;
  data: Record<string, unknown>;
};

export type ReducerOptions = {
  now?: () => string;
};

// Pure reducer for SSE events on the instance-detail view. Every mutation the
// element's _handleSseEvent used to perform against 17 @state fields now runs
// through this function instead, so the behaviour can be tested without a DOM.
//
// Side effects (popover open on run.finished, scroll-to-bottom triggers, and
// ConnectionLost render after a catch) do NOT belong here — they remain in the
// component and fire after the reducer produces new state.
export function reduceSseEvent(
  state: InstanceDetailState,
  event: SseEvent,
  options: ReducerOptions = {},
): InstanceDetailState {
  const now = options.now ?? (() => new Date().toISOString());
  const { event: type, data } = event;

  // Track agent activity — set responding on agent events, cleared by input.wait.
  let next = state;
  if (type === "text.delta" || type === "tool.start") {
    next = next.agentResponding ? next : { ...next, agentResponding: true };
  }

  // Track G (AC5) — first post-retry activity event clears stale Failed /
  // Interrupted instance status so computeChatInputGate flips to the live
  // "Message the agent..." placeholder the moment the retry LLM turn begins,
  // instead of when the step completes. The fix is set-to-Running (F4 —
  // idempotent, not a toggle): double-click on Retry cannot re-strand the UI
  // because a second transition from Running → Running is a no-op.
  if (
    (type === "text.delta" || type === "tool.start" || type === "step.started") &&
    next.instance &&
    (next.instance.status === "Failed" || next.instance.status === "Interrupted")
  ) {
    next = { ...next, instance: { ...next.instance, status: "Running" } };
  }

  switch (type) {
    case "text.delta":
      return applyTextDelta(next, data, now);
    case "tool.start":
      return applyToolStart(next, data, now);
    case "tool.args":
      return applyToolArgs(next, data);
    case "tool.end":
      return applyToolEnd(next, data);
    case "tool.result":
      return applyToolResult(next, data);
    case "step.started":
      return applyStepStarted(next, data, now);
    case "step.finished":
      return applyStepFinished(next, data, now);
    case "run.finished":
      return applyRunFinished(next, now);
    case "input.wait":
      return applyInputWait(next);
    case "run.error":
      return applyRunError(next, data, now);
    case "user.message":
      return next;
    default:
      return next;
  }
}

// Close the streaming agent message — mirror of the component's
// _finaliseStreamingMessage helper. Returns state unchanged when there is no
// active streaming text to seal.
export function finaliseStreamingMessage(state: InstanceDetailState): InstanceDetailState {
  if (!state.streamingText) return state;
  const lastIndex = state.chatMessages.length - 1;
  const last = state.chatMessages[lastIndex];
  if (!last || last.role !== "agent" || !last.isStreaming) {
    return { ...state, streamingText: "" };
  }
  const chatMessages = [
    ...state.chatMessages.slice(0, lastIndex),
    { ...last, content: state.streamingText, isStreaming: false },
  ];
  return { ...state, chatMessages, streamingText: "" };
}

function applyTextDelta(
  state: InstanceDetailState,
  data: Record<string, unknown>,
  now: () => string,
): InstanceDetailState {
  const content = data.content as string;
  if (!content) return state;
  const streamingText = state.streamingText + content;
  const lastIndex = state.chatMessages.length - 1;
  const last = state.chatMessages[lastIndex];
  let chatMessages: ChatMessage[];
  if (last && last.role === "agent" && last.isStreaming) {
    chatMessages = [
      ...state.chatMessages.slice(0, lastIndex),
      { ...last, content: streamingText },
    ];
  } else {
    // F5 — text.delta arriving after a previous message finalised (or with no
    // active agent message at all) must NOT silently drop. Seed a new
    // streaming agent message with the delta so the UI receives every frame
    // regardless of ordering quirks between finalise and subsequent emits.
    chatMessages = [
      ...state.chatMessages,
      {
        role: "agent",
        content: streamingText,
        timestamp: now(),
        isStreaming: true,
      },
    ];
  }
  return { ...state, toolBatchOpen: false, streamingText, chatMessages };
}

function applyToolStart(
  state: InstanceDetailState,
  data: Record<string, unknown>,
  now: () => string,
): InstanceDetailState {
  const finalised = finaliseStreamingMessage(state);
  const toolCallId = data.toolCallId as string;
  const toolName = data.toolName as string;
  const newToolCall: ToolCallData = {
    toolCallId,
    toolName,
    summary: toolName,
    arguments: null,
    result: null,
    status: "running",
  };
  const lastIndex = finalised.chatMessages.length - 1;
  const lastMsg = finalised.chatMessages[lastIndex];
  let chatMessages: ChatMessage[];
  if (finalised.toolBatchOpen && lastMsg && lastMsg.role === "agent") {
    chatMessages = [
      ...finalised.chatMessages.slice(0, lastIndex),
      { ...lastMsg, toolCalls: [...(lastMsg.toolCalls ?? []), newToolCall] },
      ...finalised.chatMessages.slice(lastIndex + 1),
    ];
  } else if (lastMsg && lastMsg.role === "agent" && !lastMsg.toolCalls?.length) {
    chatMessages = [
      ...finalised.chatMessages.slice(0, lastIndex),
      { ...lastMsg, toolCalls: [newToolCall] },
      ...finalised.chatMessages.slice(lastIndex + 1),
    ];
  } else {
    chatMessages = [
      ...finalised.chatMessages,
      {
        role: "agent",
        content: "",
        timestamp: now(),
        toolCalls: [newToolCall],
      },
    ];
  }
  return { ...finalised, chatMessages, toolBatchOpen: true };
}

function applyToolArgs(
  state: InstanceDetailState,
  data: Record<string, unknown>,
): InstanceDetailState {
  const tcId = data.toolCallId as string;
  const args = data.arguments as Record<string, unknown>;
  const msgIndex = state.chatMessages.findLastIndex(
    (m) => m.role === "agent" && m.toolCalls?.some((tc) => tc.toolCallId === tcId),
  );
  if (msgIndex === -1) return state;
  const msg = state.chatMessages[msgIndex];
  const updatedToolCalls = msg.toolCalls!.map((tc) =>
    tc.toolCallId === tcId
      ? { ...tc, arguments: args, summary: extractToolSummary(tc.toolName, args) }
      : tc,
  );
  const chatMessages = [
    ...state.chatMessages.slice(0, msgIndex),
    { ...msg, toolCalls: updatedToolCalls },
    ...state.chatMessages.slice(msgIndex + 1),
  ];
  return { ...state, chatMessages };
}

function applyToolEnd(
  state: InstanceDetailState,
  data: Record<string, unknown>,
): InstanceDetailState {
  const tcId = data.toolCallId as string;
  const msgIndex = state.chatMessages.findLastIndex(
    (m) => m.role === "agent" && m.toolCalls?.some((tc) => tc.toolCallId === tcId),
  );
  if (msgIndex === -1) return state;
  const msg = state.chatMessages[msgIndex];
  const updatedToolCalls = msg.toolCalls!.map((tc) =>
    tc.toolCallId === tcId && tc.status === "running"
      ? { ...tc, status: "complete" as const }
      : tc,
  );
  const chatMessages = [
    ...state.chatMessages.slice(0, msgIndex),
    { ...msg, toolCalls: updatedToolCalls },
    ...state.chatMessages.slice(msgIndex + 1),
  ];
  return { ...state, chatMessages };
}

function applyToolResult(
  state: InstanceDetailState,
  data: Record<string, unknown>,
): InstanceDetailState {
  const tcId = data.toolCallId as string;
  const rawResult = data.result;
  const resultStr =
    typeof rawResult === "string" ? rawResult : JSON.stringify(rawResult);
  const isError =
    typeof resultStr === "string" &&
    resultStr.startsWith("Tool '") &&
    (resultStr.includes("error") || resultStr.includes("failed"));
  const msgIndex = state.chatMessages.findLastIndex(
    (m) => m.role === "agent" && m.toolCalls?.some((tc) => tc.toolCallId === tcId),
  );
  if (msgIndex === -1) return state;
  const msg = state.chatMessages[msgIndex];
  const updatedToolCalls = msg.toolCalls!.map((tc) =>
    tc.toolCallId === tcId
      ? {
          ...tc,
          result: resultStr,
          status: (isError ? "error" : "complete") as ToolCallData["status"],
        }
      : tc,
  );
  const chatMessages = [
    ...state.chatMessages.slice(0, msgIndex),
    { ...msg, toolCalls: updatedToolCalls },
    ...state.chatMessages.slice(msgIndex + 1),
  ];
  return { ...state, chatMessages };
}

function applyStepStarted(
  state: InstanceDetailState,
  data: Record<string, unknown>,
  now: () => string,
): InstanceDetailState {
  const finalised = finaliseStreamingMessage(state);
  const stepId = data.stepId as string;
  const stepName = (data.stepName as string) || stepId;
  const instance = replaceStepStatus(finalised.instance, stepId, "Active");
  const chatMessages = [
    ...finalised.chatMessages,
    {
      role: "system" as const,
      content: `Starting ${stepName}...`,
      timestamp: now(),
    },
  ];
  return { ...finalised, instance, toolBatchOpen: false, chatMessages };
}

function applyStepFinished(
  state: InstanceDetailState,
  data: Record<string, unknown>,
  now: () => string,
): InstanceDetailState {
  const finalised = finaliseStreamingMessage(state);
  const stepId = data.stepId as string;
  const stepStatus = data.status as string;
  const stepName = (data.stepName as string) || stepId;
  const outcome = stepStatus === "Error" ? "failed" : "completed";
  const instance = replaceStepStatus(finalised.instance, stepId, stepStatus);
  const chatMessages = [
    ...finalised.chatMessages,
    {
      role: "system" as const,
      content: `${stepName} ${outcome}`,
      timestamp: now(),
    },
  ];
  return { ...finalised, instance, chatMessages };
}

function applyRunFinished(state: InstanceDetailState, now: () => string): InstanceDetailState {
  const finalised = finaliseStreamingMessage(state);
  if (!finalised.instance) return finalised;
  const instance = { ...finalised.instance, status: "Completed" };
  const chatMessages = [
    ...finalised.chatMessages,
    {
      role: "system" as const,
      content: "Workflow complete.",
      timestamp: now(),
    },
  ];
  return { ...finalised, instance, chatMessages };
}

function applyInputWait(state: InstanceDetailState): InstanceDetailState {
  const finalised = finaliseStreamingMessage(state);
  return { ...finalised, agentResponding: false };
}

function applyRunError(
  state: InstanceDetailState,
  data: Record<string, unknown>,
  now: () => string,
): InstanceDetailState {
  const finalised = finaliseStreamingMessage(state);
  const message = (data.message as string) || "An error occurred.";
  const instance = finalised.instance
    ? { ...finalised.instance, status: "Failed" }
    : finalised.instance;
  const chatMessages = [
    ...finalised.chatMessages,
    {
      role: "system" as const,
      content: message,
      timestamp: now(),
    },
  ];
  return { ...finalised, instance, chatMessages };
}

function replaceStepStatus(
  instance: InstanceDetailResponse | null,
  stepId: string,
  status: string,
): InstanceDetailResponse | null {
  if (!instance) return instance;
  const steps: StepDetailResponse[] = instance.steps.map((s) =>
    s.id === stepId ? { ...s, status } : s,
  );
  return { ...instance, steps };
}
