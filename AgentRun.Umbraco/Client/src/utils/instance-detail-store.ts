import type { ChatMessage, InstanceDetailResponse } from "../api/types.js";

// Local state shape for agentrun-instance-detail. This is NOT an observable or
// cross-component store (project-context.md frontend rules forbid Redux /
// standalone stores — Umbraco's UmbObjectState is the sanctioned primitive for
// shared state). The "store" here is a plain state model colocated with its
// reducer so the element owns a single @state field instead of a 17-field
// bag, and the reducer can be unit-tested without a DOM.
export type InstanceDetailState = {
  instance: InstanceDetailResponse | null;
  loading: boolean;
  error: boolean;
  selectedStepId: string | null;
  cancelling: boolean;
  runNumber: number;
  streaming: boolean;
  providerError: boolean;
  chatMessages: ChatMessage[];
  streamingText: string;
  viewingStepId: string | null;
  historyMessages: ChatMessage[];
  stepCompletable: boolean;
  agentResponding: boolean;
  retrying: boolean;
  toolBatchOpen: boolean;
};

export function initialInstanceDetailState(): InstanceDetailState {
  return {
    instance: null,
    loading: true,
    error: false,
    selectedStepId: null,
    cancelling: false,
    runNumber: 0,
    streaming: false,
    providerError: false,
    chatMessages: [],
    streamingText: "",
    viewingStepId: null,
    historyMessages: [],
    stepCompletable: false,
    agentResponding: false,
    retrying: false,
    toolBatchOpen: false,
  };
}
