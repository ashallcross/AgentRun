import type { WorkflowSummary, InstanceResponse, InstanceDetailResponse, ConversationEntryResponse, ChatMessage, ToolCallData } from "./types.js";
import { extractToolSummary } from "../utils/tool-helpers.js";

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

export function getInstance(id: string, token?: string): Promise<InstanceDetailResponse> {
  return fetchJson<InstanceDetailResponse>(`/instances/${encodeURIComponent(id)}`, token);
}

export function cancelInstance(id: string, token?: string): Promise<InstanceResponse> {
  return postJson<InstanceResponse>(`/instances/${encodeURIComponent(id)}/cancel`, {}, token);
}

export async function startInstance(id: string, token?: string): Promise<Response> {
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  return fetch(`${API_BASE}/instances/${encodeURIComponent(id)}/start`, {
    method: "POST",
    headers,
  });
}

export function getConversation(instanceId: string, stepId: string, token?: string): Promise<ConversationEntryResponse[]> {
  return fetchJson<ConversationEntryResponse[]>(
    `/instances/${encodeURIComponent(instanceId)}/conversation/${encodeURIComponent(stepId)}`,
    token,
  );
}

export function mapConversationToChat(entries: ConversationEntryResponse[]): ChatMessage[] {
  const messages: ChatMessage[] = [];
  for (const entry of entries) {
    if (entry.role === "assistant" && entry.content != null && !entry.toolCallId) {
      // Text-only assistant message
      messages.push({
        role: "agent",
        content: entry.content,
        timestamp: entry.timestamp,
      });
    } else if (entry.role === "assistant" && entry.toolCallId) {
      // Tool call entry — attach to preceding agent message
      let parsedArgs: Record<string, unknown> | null = null;
      if (entry.toolArguments) {
        try {
          parsedArgs = JSON.parse(entry.toolArguments) as Record<string, unknown>;
        } catch {
          parsedArgs = null;
        }
      }
      const toolCall: ToolCallData = {
        toolCallId: entry.toolCallId,
        toolName: entry.toolName ?? "unknown",
        summary: extractToolSummary(entry.toolName ?? "unknown", parsedArgs),
        arguments: parsedArgs,
        result: null,
        status: "complete",
      };
      const lastAgent = findLastAgentMessage(messages);
      if (lastAgent) {
        lastAgent.toolCalls = [...(lastAgent.toolCalls ?? []), toolCall];
      } else {
        // No preceding text message — create empty-content agent message
        messages.push({
          role: "agent",
          content: "",
          timestamp: entry.timestamp,
          toolCalls: [toolCall],
        });
      }
    } else if (entry.role === "tool" && entry.toolCallId) {
      // Tool result entry — find matching ToolCallData and set result
      const resultStr = entry.toolResult ?? null;
      const isError = typeof resultStr === "string"
        && resultStr.startsWith("Tool '") && (resultStr.includes("error") || resultStr.includes("failed"));
      for (let i = messages.length - 1; i >= 0; i--) {
        const msg = messages[i];
        const tc = msg.toolCalls?.find(t => t.toolCallId === entry.toolCallId);
        if (tc) {
          tc.result = resultStr;
          if (isError) tc.status = "error";
          break;
        }
      }
    } else if (entry.role === "system" && entry.content != null) {
      messages.push({
        role: "system",
        content: entry.content,
        timestamp: entry.timestamp,
      });
    }
  }
  return messages;
}

function findLastAgentMessage(messages: ChatMessage[]): ChatMessage | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    if (messages[i].role === "agent") return messages[i];
  }
  return null;
}
