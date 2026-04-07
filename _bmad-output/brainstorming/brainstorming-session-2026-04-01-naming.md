---
stepsCompleted: [1, 2, 3]
inputDocuments: []
session_topic: 'Product name for Umbraco AI workflow orchestration package'
session_goals: 'Discoverable, community-feel, communicates AI workflow orchestration for Umbraco'
selected_approach: 'AI-Recommended — focused riff on established themes'
techniques_used: ['semantic clustering', 'metaphor mining', 'conflict checking']
ideas_generated: ['Conductor', 'RunBook AI', 'AgentFlow', 'Relay', 'Weave', 'Cadence', 'Tempo', 'Overture', 'Dispatch', 'Forge', 'Loom', 'Stitch', 'Current', 'Flux', 'WorkflowKit', 'AgentKit', 'FlowAgent', 'AgentRun', 'StepAgent', 'RunFlow', 'AgentBook', 'FlowBook', 'RunAgent', 'AgentForge', 'AgentLab', 'Herald', 'Marshal', 'Envoy', 'Sentinel', 'Steward', 'Courier', 'Traverse', 'Circuit', 'Cue', 'Anvil', 'Rivet', 'Tasker', 'Errand', 'Brief', 'Mandate', 'Sortie', 'Venture', 'Harness', 'Helm', 'Nexus']
context_file: ''
---

# Brainstorming Session: Package Naming

**Facilitator:** Adam
**Date:** 2026-04-01
**Status:** Complete

## Session Overview

**Topic:** Product name for the Umbraco AI workflow orchestration package (previously "Shallai")
**Goals:** Name that is discoverable, communicates value, feels practitioner-built, works on NuGet and in conversation

## Decision

**Winner: AgentRun**

### Why AgentRun

- "Agent" says AI, "Run" says execution — zero ambiguity
- No active NuGet conflict
- No active product/trademark conflict (previous insurance company using the name is permanently closed)
- Doesn't sound like an Umbraco HQ product
- Works naturally in conversation: "have you tried AgentRun?"
- Clean namespace: `AgentRun.Umbraco`, `AgentRun.Umbraco.Pro`
- Marketplace-friendly: "AgentRun for Umbraco"

### Package Mapping

| Current | New |
|---------|-----|
| `Shallai.UmbracoAgentRunner` | `AgentRun.Umbraco` |
| `Shallai.UmbracoAgentRunner.Tests` | `AgentRun.Umbraco.Tests` |
| `Shallai.UmbracoAgentRunner.Pro` (V2) | `AgentRun.Umbraco.Pro` |

### Runners Up

| Name | Why Not |
|------|---------|
| Conductor | NuGet taken (3 packages), overloaded in .NET ecosystem, sounds HQ-ish |
| Helm | Kubernetes package manager owns this word, Helm.ai is active AI company |
| AgentFlow | 5+ existing products with this name — crowded namespace |
| RunBook AI | HCL BigFix Runbook AI is a major enterprise product, IT ops owns "runbook" |
| Relay | Clean but sounds like IT hardware |
| Envoy | Sounds like a messaging system |
| Maestro | Already used for Adam's other app |

### Eliminated Categories

- `Umbraco.Something` pattern — risks looking like an official HQ product
- Anything overlapping with "Compose" energy — HQ just launched Umbraco Compose
- Courier — was an old Umbraco package name
