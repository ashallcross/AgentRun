import { startInstance, retryInstance, cancelInstance } from "../api/api-client.js";

// Action wrappers for instance-detail. Each returns a discriminated union so
// the component can branch on the UI-relevant outcome instead of decoding raw
// HTTP status codes inline (producer of SSE response vs 400 provider-error vs
// 409 already-in-terminal-state vs transport failure). UI concerns — modal
// confirmation, loading flag toggles, chat-message writes — stay in the
// component; these functions own only the API call plus error translation.

export type StartInstanceResult =
  | { kind: "streaming"; response: Response }
  | { kind: "providerError" }
  | { kind: "failed"; status: number };

export async function startInstanceAction(
  instanceId: string,
  token: string | undefined,
): Promise<StartInstanceResult> {
  let response: Response;
  try {
    response = await startInstance(instanceId, token);
  } catch {
    // Network-level failure before a Response is produced (offline, DNS,
    // aborted). Surface as { kind: "failed", status: 0 } so the caller can
    // clear loading flags without an unhandled promise rejection.
    return { kind: "failed", status: 0 };
  }
  if (response.ok) return { kind: "streaming", response };
  if (response.status === 400) return { kind: "providerError" };
  return { kind: "failed", status: response.status };
}

export type RetryInstanceResult =
  | { kind: "streaming"; response: Response }
  | { kind: "notRetryable" }
  | { kind: "failed"; status: number };

export async function retryInstanceAction(
  instanceId: string,
  token: string | undefined,
): Promise<RetryInstanceResult> {
  let response: Response;
  try {
    response = await retryInstance(instanceId, token);
  } catch {
    return { kind: "failed", status: 0 };
  }
  if (response.ok) return { kind: "streaming", response };
  if (response.status === 409) return { kind: "notRetryable" };
  return { kind: "failed", status: response.status };
}

export type CancelInstanceResult =
  | { kind: "ok" }
  | { kind: "conflict" }
  | { kind: "failed"; error: unknown };

export async function cancelInstanceAction(
  instanceId: string,
  token: string | undefined,
): Promise<CancelInstanceResult> {
  try {
    await cancelInstance(instanceId, token);
    return { kind: "ok" };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    // A 409 means the run is already in a non-cancellable state (Completed /
    // Failed / Cancelled) — treat as idempotent for the UI.
    if (message.includes("409")) return { kind: "conflict" };
    return { kind: "failed", error: err };
  }
}
