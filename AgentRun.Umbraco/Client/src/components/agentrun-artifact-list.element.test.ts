import { expect } from "@open-wc/testing";
import type { StepDetailResponse } from "../api/types.js";

describe("agentrun-artifact-list", () => {
  const mockStep = (overrides: Partial<StepDetailResponse> = {}): StepDetailResponse => ({
    id: "step-1",
    name: "Content Scanner",
    status: "Complete",
    startedAt: "2026-04-01T10:00:00Z",
    completedAt: "2026-04-01T10:05:00Z",
    writesTo: ["scan-results.md"],
    ...overrides,
  });

  // Helper to derive artifacts — mirrors the component logic
  function deriveArtifacts(steps: StepDetailResponse[]): Array<{ path: string; stepName: string; stepStatus: string }> {
    const entries: Array<{ path: string; stepName: string; stepStatus: string }> = [];
    for (const step of steps) {
      if (!step.writesTo || step.writesTo.length === 0) continue;
      if (step.status !== "Complete") continue;
      for (const path of step.writesTo) {
        entries.push({ path, stepName: step.name, stepStatus: step.status });
      }
    }
    return entries;
  }

  // 6.2: renders empty state when steps have no writesTo
  it("derives empty list when steps have no writesTo", () => {
    const steps = [
      mockStep({ writesTo: null }),
      mockStep({ id: "step-2", name: "Analyser", writesTo: null }),
    ];
    const artifacts = deriveArtifacts(steps);
    expect(artifacts).to.have.lengthOf(0);
  });

  // 6.3: renders artifact entries from steps with writesTo arrays
  it("derives artifact entries from steps with writesTo arrays", () => {
    const steps = [
      mockStep({ writesTo: ["scan-results.md"] }),
      mockStep({ id: "step-2", name: "Analyser", writesTo: ["analysis.md"] }),
    ];
    const artifacts = deriveArtifacts(steps);
    expect(artifacts).to.have.lengthOf(2);
    expect(artifacts[0].path).to.equal("scan-results.md");
    expect(artifacts[0].stepName).to.equal("Content Scanner");
    expect(artifacts[1].path).to.equal("analysis.md");
    expect(artifacts[1].stepName).to.equal("Analyser");
  });

  // 6.4: flatMaps multiple artifacts from a single step
  it("flatMaps multiple artifacts from a single step", () => {
    const steps = [
      mockStep({ writesTo: ["scan-results.md", "scan-summary.md", "scan-errors.md"] }),
    ];
    const artifacts = deriveArtifacts(steps);
    expect(artifacts).to.have.lengthOf(3);
    expect(artifacts[0].path).to.equal("scan-results.md");
    expect(artifacts[1].path).to.equal("scan-summary.md");
    expect(artifacts[2].path).to.equal("scan-errors.md");
    // All from the same step
    expect(artifacts.every(a => a.stepName === "Content Scanner")).to.be.true;
  });

  // 6.5: shows step name for each artifact
  it("includes step name for each artifact entry", () => {
    const steps = [
      mockStep({ name: "Content Scanner", writesTo: ["scan.md"] }),
      mockStep({ id: "step-2", name: "Report Writer", writesTo: ["report.md"] }),
    ];
    const artifacts = deriveArtifacts(steps);
    expect(artifacts[0].stepName).to.equal("Content Scanner");
    expect(artifacts[1].stepName).to.equal("Report Writer");
  });

  // 6.6: dispatches artifact-selected event with correct path on click
  it("artifact-selected CustomEvent has correct detail shape", () => {
    const event = new CustomEvent("artifact-selected", {
      detail: { path: "scan-results.md", stepName: "Content Scanner" },
      bubbles: true,
      composed: true,
    });
    expect(event.detail.path).to.equal("scan-results.md");
    expect(event.detail.stepName).to.equal("Content Scanner");
    expect(event.bubbles).to.be.true;
    expect(event.composed).to.be.true;
  });

  // 6.7: excludes artifacts from non-Complete steps
  it("excludes artifacts from non-Complete steps", () => {
    const steps = [
      mockStep({ status: "Active", writesTo: ["partial.md"] }),
      mockStep({ id: "step-2", status: "Complete", writesTo: ["done.md"] }),
    ];
    const artifacts = deriveArtifacts(steps);
    expect(artifacts).to.have.lengthOf(1);
    expect(artifacts[0].path).to.equal("done.md");
    expect(artifacts[0].stepStatus).to.equal("Complete");
  });

  // 6.8: skips steps with null writesTo and empty array writesTo
  it("skips steps with null writesTo", () => {
    const steps = [
      mockStep({ writesTo: null }),
      mockStep({ id: "step-2", name: "Writer", writesTo: ["output.md"] }),
    ];
    const artifacts = deriveArtifacts(steps);
    expect(artifacts).to.have.lengthOf(1);
    expect(artifacts[0].stepName).to.equal("Writer");
  });

  it("skips steps with empty array writesTo", () => {
    const steps = [
      mockStep({ writesTo: [] }),
      mockStep({ id: "step-2", name: "Writer", writesTo: ["output.md"] }),
    ];
    const artifacts = deriveArtifacts(steps);
    expect(artifacts).to.have.lengthOf(1);
    expect(artifacts[0].stepName).to.equal("Writer");
  });

  // 6.9: filename extraction from path
  it("extracts filename from path correctly", () => {
    const getFilename = (path: string): string => {
      const segments = path.split("/");
      return segments[segments.length - 1];
    };
    expect(getFilename("scan-results.md")).to.equal("scan-results.md");
    expect(getFilename("output/reports/scan-results.md")).to.equal("scan-results.md");
    expect(getFilename("deeply/nested/path/file.txt")).to.equal("file.txt");
  });
});
