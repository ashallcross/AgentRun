export interface WorkflowSummary {
  alias: string;
  name: string;
  description: string;
  stepCount: number;
  mode: string;
}

export interface InstanceResponse {
  id: string;
  workflowAlias: string;
  status: string;
  currentStepIndex: number;
  createdAt: string;
  updatedAt: string;
}

export interface StepDetailResponse {
  id: string;
  name: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  writesTo: string[] | null;
}

export interface InstanceDetailResponse {
  id: string;
  workflowAlias: string;
  workflowName: string;
  status: string;
  currentStepIndex: number;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
  steps: StepDetailResponse[];
}

export interface ConversationEntryResponse {
  role: string;
  content: string | null;
  timestamp: string;
  toolCallId?: string;
  toolName?: string;
  toolArguments?: string;
  toolResult?: string;
}

export interface ToolCallData {
  toolCallId: string;
  toolName: string;
  summary: string;
  arguments: Record<string, unknown> | null;
  result: string | null;
  status: "running" | "complete" | "error";
}

export interface ChatMessage {
  role: "agent" | "system" | "user";
  content: string;
  timestamp: string;
  isStreaming?: boolean;
  toolCalls?: ToolCallData[];
}
