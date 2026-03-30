import type { WorkflowSummary, InstanceResponse } from "./types.js";

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

async function postJson<T>(path: string, body: unknown, token?: string): Promise<T> {
  const headers: Record<string, string> = {
    Accept: "application/json",
    "Content-Type": "application/json",
  };
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers,
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`);
  }
  return response.json() as Promise<T>;
}

async function deleteRequest(path: string, token?: string): Promise<void> {
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await fetch(`${API_BASE}${path}`, {
    method: "DELETE",
    headers,
  });
  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`);
  }
}

export function getWorkflows(token?: string): Promise<WorkflowSummary[]> {
  return fetchJson<WorkflowSummary[]>("/workflows", token);
}

export function getInstances(workflowAlias: string, token?: string): Promise<InstanceResponse[]> {
  return fetchJson<InstanceResponse[]>(`/instances?workflowAlias=${encodeURIComponent(workflowAlias)}`, token);
}

export function createInstance(workflowAlias: string, token?: string): Promise<InstanceResponse> {
  return postJson<InstanceResponse>("/instances", { workflowAlias }, token);
}

export function deleteInstance(id: string, token?: string): Promise<void> {
  return deleteRequest(`/instances/${encodeURIComponent(id)}`, token);
}
