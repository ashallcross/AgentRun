export function extractToolSummary(toolName: string, args: Record<string, unknown> | null): string {
  if (!args) return toolName;

  if (typeof args.path === "string") {
    const segments = args.path.split("/");
    return segments[segments.length - 1] || toolName;
  }

  if (typeof args.url === "string") {
    return args.url.length > 60 ? args.url.slice(0, 60) + "…" : args.url;
  }

  for (const value of Object.values(args)) {
    if (typeof value === "string") return value;
  }

  return toolName;
}
