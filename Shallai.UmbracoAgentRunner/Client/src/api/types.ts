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
