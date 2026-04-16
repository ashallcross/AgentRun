import type { ChatMessage, StepDetailResponse } from "../api/types.js";

export function extractInstanceId(pathname: string): string {
  const segments = pathname.split("/");
  return decodeURIComponent(segments[segments.length - 1]);
}

export function buildInstanceListPath(
  pathname: string,
  workflowAlias: string,
): string {
  const segments = pathname.split("/");
  if (segments.length >= 2) {
    segments.splice(-2, 2, "workflows", encodeURIComponent(workflowAlias));
  }
  return segments.join("/");
}

export function stepSubtitle(step: StepDetailResponse): string {
  switch (step.status) {
    case "Pending":
      return "Pending";
    case "Active":
      return "In progress";
    case "Complete":
      return step.writesTo?.[0] ?? "Complete";
    case "Error":
      return "Error";
    case "Cancelled":
      return "Cancelled";
    default:
      return step.status;
  }
}

export function stepIconName(status: string): string {
  switch (status) {
    case "Pending":
      return "icon-circle-dotted";
    case "Active":
      return "icon-sync";
    case "Complete":
      return "icon-check";
    case "Error":
      return "icon-wrong";
    case "Cancelled":
      return "icon-block";
    default:
      return "icon-circle-dotted";
  }
}

export function shouldAnimateStepIcon(status: string, isStreaming: boolean): boolean {
  return status === "Active" && isStreaming;
}

export function stepIconColor(status: string): string | undefined {
  switch (status) {
    case "Active":
      return "current";
    case "Complete":
      return "positive";
    case "Error":
      return "danger";
    case "Cancelled":
      return undefined; // neutral — instance-level Cancelled badge carries the signal
    default:
      return undefined;
  }
}

// Story 10.10: `showContinue` and the interactive chat-input gate are extracted
// here so both the production render() and tests call the same function. This
// closes the drift risk of predicate-mirror tests — there is no mirror to
// maintain, only one contract.
export interface ContinueButtonInput {
  readonly instanceStatus: string;
  readonly hasActiveStep: boolean;
  readonly isStreaming: boolean;
  readonly hasPendingSteps: boolean;
}

export function shouldShowContinueButton(input: ContinueButtonInput): boolean {
  return input.instanceStatus === "Running"
    && !input.hasActiveStep
    && !input.isStreaming
    && input.hasPendingSteps;
}

export interface ChatInputGateInput {
  readonly instanceStatus: string;
  readonly isInteractive: boolean;
  readonly isTerminal: boolean;
  readonly hasActiveStep: boolean;
  readonly isStreaming: boolean;
  readonly isViewingStepHistory: boolean;
  readonly agentResponding: boolean;
  // True when status is Running with no active step, no in-flight stream, and
  // pending work remains. Decoupled from the header Continue button visibility
  // because the in-session completion banner shows the same affordance —
  // disabling the input is correct in both cases.
  readonly isBetweenSteps: boolean;
}

export interface ChatInputGate {
  readonly inputEnabled: boolean;
  readonly inputPlaceholder: string;
}

export function computeChatInputGate(input: ChatInputGateInput): ChatInputGate {
  if (input.instanceStatus === "Interrupted") {
    return { inputEnabled: false, inputPlaceholder: "Run interrupted — click Retry to resume." };
  }

  if (input.isInteractive) {
    if (input.isTerminal) {
      const inputPlaceholder =
        input.instanceStatus === "Cancelled" ? "Run cancelled."
        : input.instanceStatus === "Failed" ? "Run failed — click Retry to resume."
        : "Workflow complete.";
      return { inputEnabled: false, inputPlaceholder };
    }
    if (input.isViewingStepHistory) {
      return { inputEnabled: false, inputPlaceholder: "Viewing step history" };
    }
    if (input.agentResponding) {
      return { inputEnabled: false, inputPlaceholder: "Agent is responding..." };
    }
    if (input.isBetweenSteps) {
      return { inputEnabled: false, inputPlaceholder: "Click Continue to run the next step." };
    }
    if (input.isStreaming || input.hasActiveStep) {
      return { inputEnabled: true, inputPlaceholder: "Message the agent..." };
    }
    return { inputEnabled: false, inputPlaceholder: "Send a message to start." };
  }

  // Autonomous mode
  const inputEnabled = input.isStreaming && !input.isViewingStepHistory;
  let inputPlaceholder: string;
  if (input.isTerminal) {
    inputPlaceholder =
      input.instanceStatus === "Cancelled" ? "Run cancelled."
      : input.instanceStatus === "Failed" ? "Run failed — click Retry to resume."
      : "Workflow complete.";
  } else if (input.hasActiveStep) {
    inputPlaceholder = "Step complete";
  } else {
    inputPlaceholder = "Click 'Start' to begin the workflow.";
  }
  return { inputEnabled, inputPlaceholder };
}

// Story 10.13 AC3 + AC4: shared chat-line text for terminal run states. Both
// the SSE reducer (run.finished payload) and the Pending-cancel handler call
// this so the dedupe guard in `shouldAppendTerminalLine` compares like-with-like.
// Returns null for statuses where no chat append is appropriate (preserves the
// out-of-order-finalisation defence in the reducer).
export function terminalChatLine(status: string): string | null {
  switch (status) {
    case "Completed":
      return "Workflow complete.";
    case "Cancelled":
      return "Run cancelled.";
    case "Failed":
      return "Run failed.";
    case "Interrupted":
      return "Run interrupted.";
    default:
      return null;
  }
}

// Idempotency guard for AC3/AC4. Two paths can both append the same terminal
// chat line: (a) the SSE reducer when run.finished arrives, (b) _onCancelClick
// when the user cancels a Pending instance. Without this guard, racing the two
// produces a duplicate line.
export function shouldAppendTerminalLine(
  lastMessageContent: string | undefined,
  candidate: string,
): boolean {
  return lastMessageContent !== candidate;
}

// Single source of truth for the chat-panel block-cursor visibility. Called
// from both the Lit template in agentrun-chat-panel.element.ts and its test
// so the predicate cannot drift — Track F / AC3 depends on this gating the
// cursor to active text streaming only (Tom Madden beta repro: cursor must
// not flash during tool calls or waiting states).
export function shouldShowCursor(
  messageIndex: number,
  lastIndex: number,
  msg: ChatMessage,
  connectionIsStreaming: boolean,
): boolean {
  return (
    messageIndex === lastIndex &&
    msg.role === "agent" &&
    connectionIsStreaming &&
    msg.isStreaming === true
  );
}
