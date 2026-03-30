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
