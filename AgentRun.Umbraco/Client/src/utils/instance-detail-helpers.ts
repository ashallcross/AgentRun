import type { StepDetailResponse } from "../api/types.js";

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
    default:
      return undefined;
  }
}
