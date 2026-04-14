# AgentRun.Umbraco vs Umbraco.AI Copilot — Positioning Guide

**Date:** 2026-04-13
**Purpose:** Quick reference for explaining the difference between AgentRun and the built-in Umbraco.AI Copilot when asked "why does this package exist when Umbraco already has AI?"

---

## The One-Line Answer

**Copilot helps an editor with one page. AgentRun runs a workflow across your entire site.**

## The Three-Sentence Version

Copilot is a chat assistant — you ask it a question about the page you're editing and it answers. AgentRun is a workflow engine — you define a multi-step process in YAML (scan every page, analyse the findings, produce a report) and it executes the whole thing autonomously. They're complementary, not competitive — Copilot is the assistant sat next to the editor, AgentRun is the analyst who goes away and comes back with a deliverable.

## When Someone Pushes Back

"But the Copilot can read content and write to fields — can't I just ask it to audit my whole site?"

You could try, but:

- **Copilot has no memory between conversations.** You'd have to ask it to check each page individually, remember the findings yourself, and manually synthesise a report. AgentRun chains steps — the scanner's output feeds the analyser, which feeds the reporter.
- **Copilot has no artifact handoff.** Each conversation is isolated. AgentRun writes structured artifacts (`scan-results.md` → `quality-scores.md` → `audit-report.md`) that flow between steps automatically.
- **Copilot uses one model for everything.** AgentRun lets you use a cheap model for bulk scanning and an expensive model for analysis — per-step cost control.
- **Copilot can't be templated and reused.** You can't save a Copilot conversation as a repeatable process. An AgentRun workflow is a YAML file — run it this week, run it next month, hand it to a colleague, ship it to a client.
- **Copilot is reactive, AgentRun is proactive.** Copilot waits for the editor to ask. AgentRun runs a defined process start to finish and produces a deliverable without being asked page by page.

## The Analogy

Copilot is like having a smart colleague you can ask questions. AgentRun is like hiring a consultant who takes a brief, goes away, does the work, and comes back with a report.

You'd never fire the colleague because you hired the consultant. They do different jobs.

## Side-by-Side Comparison

| Capability | Umbraco.AI Copilot | AgentRun.Umbraco |
|-----------|-------------------|-----------------|
| **Interaction model** | Conversational chat | Structured multi-step workflows |
| **Scope** | One page / one question at a time | Entire site, entire content tree |
| **Memory across tasks** | None — each conversation is isolated | Artifact handoff between steps; findings persist |
| **Output** | Chat responses, field updates | Structured markdown reports, audit deliverables |
| **Repeatability** | Manual — redo the conversation each time | YAML template — run the same workflow on any site |
| **Cost control** | One model for everything | Per-step model selection (cheap for scanning, capable for analysis) |
| **Tool access** | All registered tools, always available | Per-step whitelisting — each step only sees the tools it needs |
| **Who drives** | The editor asks questions | The workflow definition drives; editor reviews output |
| **Best for** | "Help me improve this page right now" | "Audit all 200 pages and give me a prioritised report" |
| **Customisation** | Prompt engineering in chat | YAML workflow definitions + markdown agent prompts |
| **Shareability** | Can't share a conversation as a process | Workflow folders are portable — share, version, ship as packages |

## They're Complementary

AgentRun doesn't replace Copilot. They solve different problems:

1. **Editor is writing a blog post** → Copilot helps with tone, suggests improvements, fills in meta descriptions. Real-time, conversational, page-level.
2. **Agency needs to audit a client's site** → AgentRun runs a Content Audit workflow that scans every page, analyses completeness against the content model, and produces a prioritised report. Structured, automated, site-level.
3. **Editor wants to check one page's accessibility** → Copilot can answer questions about the current page.
4. **Compliance team needs a WCAG scan of the whole site** → AgentRun runs an Accessibility Quick-Scan across every URL and produces a findings document.

The pattern: Copilot is **per-page, per-moment, per-question**. AgentRun is **per-site, per-process, per-deliverable**.

## Technical Distinction

Both systems access the same Umbraco data (content tree, document types, media). The tools look similar because they read from the same services. The difference is the execution model:

- **Copilot** uses `[AITool]` / `AIToolBase` with global tool registration. The `FunctionInvokingChatClient` auto-executes tool calls. One conversation, one model, one context.
- **AgentRun** uses `IWorkflowTool` with per-step whitelisting. The `ToolLoop` handles execution manually with validation, size limits, stall detection, and streaming. Multiple steps, multiple models, artifact handoff between steps.

AgentRun deliberately does not use Umbraco.AI's `[AITool]` system because global registration breaks per-step security (an agent that should only read content could call a write tool if all tools are globally visible).
