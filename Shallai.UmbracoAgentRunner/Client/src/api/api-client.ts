import type { WorkflowSummary } from "./types.js";

const API_BASE = "/umbraco/api/shallai";

async function fetchJson<T>(path: string, token?: string): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  const response = await fetch(`${API_BASE}${path}`, { headers });
  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`);
  }
  return response.json() as Promise<T>;
}

export function getWorkflows(token?: string): Promise<WorkflowSummary[]> {
  return fetchJson<WorkflowSummary[]>("/workflows", token);
}
