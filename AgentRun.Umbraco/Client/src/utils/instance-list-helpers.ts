import type { InstanceResponse } from "../api/types.js";

export interface NumberedInstance extends InstanceResponse {
  runNumber: number;
}

export function relativeTime(isoDate: string): string {
  const seconds = Math.floor((Date.now() - new Date(isoDate).getTime()) / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? "" : "s"} ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? "" : "s"} ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days} day${days === 1 ? "" : "s"} ago`;
  const months = Math.floor(days / 30);
  return `${months} month${months === 1 ? "" : "s"} ago`;
}

export function extractWorkflowAlias(pathname: string): string {
  const segments = pathname.split("/");
  const lastSegment = segments[segments.length - 1];
  return decodeURIComponent(lastSegment);
}

export function buildInstancePath(id: string, pathname: string): string {
  // Current path: .../workflows/{alias} → target: .../instances/{id}
  // Replace last two segments with instances/{id}
  const segments = pathname.split("/");
  if (segments.length >= 2) {
    segments.splice(-2, 2, "instances", id);
  }
  return segments.join("/");
}

export function buildWorkflowListPath(pathname: string): string {
  // Current path: .../workflows/{alias} → target: .../workflows
  // Remove the last segment (the alias)
  const lastSlash = pathname.lastIndexOf("/");
  if (lastSlash > 0) {
    return pathname.substring(0, lastSlash);
  }
  return pathname;
}

export function displayStatus(status: string, mode?: string): string {
  const isInteractive = mode !== "autonomous";
  switch (status) {
    case "Running":
      return isInteractive ? "In progress" : "Running";
    case "Failed":
      return isInteractive ? "In progress" : "Failed";
    case "Completed":
      return "Complete";
    case "Pending":
      return "Pending";
    case "Cancelled":
      return "Cancelled";
    case "Interrupted":
      return "Interrupted";
    default:
      return status;
  }
}

export function statusColor(status: string, mode?: string): string | undefined {
  const isInteractive = mode !== "autonomous";
  switch (status) {
    case "Completed":
      return "positive";
    case "Failed":
      return isInteractive ? undefined : "danger";
    case "Running":
      return isInteractive ? undefined : "warning";
    case "Interrupted":
      return "warning";
    default:
      return undefined;
  }
}

export interface InstanceListLabels {
  newButton: string;
  emptyState: string;
}

export function instanceListLabels(mode?: string): InstanceListLabels {
  const isInteractive = mode !== "autonomous";
  if (isInteractive) {
    return {
      newButton: "New session",
      emptyState: "No sessions yet. Start one to begin.",
    };
  }
  return {
    newButton: "New Run",
    emptyState: "No runs yet. Click 'New Run' to start.",
  };
}

export function isTerminalStatus(status: string): boolean {
  return status === "Completed" || status === "Failed" || status === "Cancelled";
}

export function numberAndSortInstances(instances: InstanceResponse[]): NumberedInstance[] {
  const sorted = [...instances].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
  );
  const numbered = sorted.map((inst, i) => ({ ...inst, runNumber: i + 1 }));
  numbered.reverse();
  return numbered;
}
