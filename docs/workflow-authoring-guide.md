# Workflow Authoring Guide

This guide covers everything you need to create custom AgentRun workflows for Umbraco.

## Contents

- [Overview](#overview)
- [Workflow Structure](#workflow-structure)
- [Writing Agent Prompts](#writing-agent-prompts)
- [Artifact Handoff](#artifact-handoff)
- [Prompt Variables](#prompt-variables)
- [Prompt Caching](#prompt-caching)
- [Agent Sanctum Pattern](#agent-sanctum-pattern)
- [Conditional Pillars via Config](#conditional-pillars-via-config)
- [Completion Checking](#completion-checking)
- [Profile Configuration](#profile-configuration)
- [Available Tools](#available-tools) — `fetch_url` · `read_file` · `write_file` · `list_files` · `list_content` · `get_content` · `list_content_types` · `web_search` · `get_ai_context` · `search_content`
- [Configuring Tool Tuning Values](#configuring-tool-tuning-values)
- [IDE Validation Setup](#ide-validation-setup)
- [Walkthrough: Creating a 2-Step Workflow](#walkthrough-creating-a-2-step-workflow)
- [Learning from the Examples](#learning-from-the-examples)

## Overview

A workflow is a folder containing:

- **`workflow.yaml`** -- the definition file that declares the workflow's name, steps, tools, and configuration
- **`agents/*.md`** -- markdown files containing the system prompt for each step's AI agent

The engine executes steps in order. Each step gets an LLM conversation seeded with the agent
prompt, access to declared tools, and visibility of artifacts produced by prior steps. In
**interactive** mode (the default), the agent pauses for user input between tool-call sequences.
In **autonomous** mode, steps run back-to-back without user interaction.

```
my-workflow/
  workflow.yaml
  agents/
    step-one.md
    step-two.md
```

Place your workflow folder in `App_Data/AgentRun.Umbraco/workflows/` (or wherever `WorkflowPath`
is configured). The folder name becomes the **workflow alias** -- the engine uses it as the
unique identifier, so keep it short, lowercase, and hyphenated.

## Workflow Structure

Here is a minimal `workflow.yaml`:

```yaml
# yaml-language-server: $schema=../../workflow-schema.json
name: URL Summary
description: Fetches a page and produces a plain-language summary
mode: interactive
default_profile: your-profile-alias
steps:
  - id: fetcher
    name: Page Fetcher
    agent: agents/fetcher.md
    tools:
      - fetch_url
      - write_file
    writes_to:
      - artifacts/page-content.md
    completion_check:
      files_exist:
        - artifacts/page-content.md
  - id: summariser
    name: Summariser
    agent: agents/summariser.md
    tools:
      - read_file
      - write_file
    reads_from:
      - artifacts/page-content.md
    writes_to:
      - artifacts/summary.md
    completion_check:
      files_exist:
        - artifacts/summary.md
```

### Root-Level Fields

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Human-readable name shown in the dashboard |
| `description` | Yes | Short description shown in the workflow list |
| `steps` | Yes | Ordered array of step definitions (at least one) |
| `mode` | No | `interactive` (default) or `autonomous` |
| `default_profile` | No | Umbraco.AI profile alias used when a step doesn't specify its own `profile`. Falls back to the site-level `AgentRun:DefaultProfile` if omitted |
| `icon` | No | Icon identifier for the dashboard |
| `variants` | No | Reserved for future use -- free-form object, not yet stable |
| `tool_defaults` | No | Workflow-wide tool tuning values (see [Configuring Tool Tuning Values](#configuring-tool-tuning-values)) |

### Step Fields

| Field | Required | Description |
|-------|----------|-------------|
| `id` | Yes | Unique identifier within this workflow (`snake_case` recommended) |
| `name` | Yes | Human-readable name shown in the step sidebar |
| `agent` | Yes | Relative path to the agent markdown file (from the workflow folder) |
| `description` | No | Short description surfaced in the dashboard |
| `profile` | No | LLM profile override for this step |
| `tools` | No | List of tool names this step can call. Only listed tools are available -- unlisted tools are blocked |
| `reads_from` | No | Artifact paths this step needs as input. The engine checks these exist before the step runs -- missing inputs are fatal |
| `writes_to` | No | Artifact paths this step is expected to produce. Informational for the dashboard and prompt context |
| `completion_check` | No | Defines when the step is considered complete |
| `data_files` | No | Read-only data files from the workflow folder, available at prompt-assembly time |
| `tool_overrides` | No | Step-specific tool tuning values that override `tool_defaults` |

For the full schema with validation constraints, see the [JSON Schema](#ide-validation-setup).

## Writing Agent Prompts

Each step's `agent` field points to a markdown file containing the system prompt. This is where
you tell the AI what to do, how to behave, and what output to produce.

The engine assembles the final system prompt by combining:

1. **Your agent markdown** -- the full contents of the file
2. **Runtime context** -- injected automatically after your prompt:
   - Step ID, name, and description
   - List of available tools (name + description for each)
   - Input artifacts (from `reads_from`) with existence flags
   - Prior artifacts (from earlier completed steps) with existence flags
   - Expected output artifacts (from `writes_to`)

You don't need to tell the agent which tools it has or what artifacts exist -- the engine
provides that context. Focus your prompt on:

- **What the agent should do** -- the task, step by step
- **How it should behave** -- interaction style, constraints, invariants
- **What output to produce** -- file format, structure, naming

Agent prompts can also include `{token}` placeholders that the runner
substitutes before the LLM sees the prompt -- see
[Prompt Variables](#prompt-variables) below for the available tokens and
the workflow-level `config:` block.

The way you order content in an agent prompt also affects provider-side
prompt caching — see [Prompt Caching](#prompt-caching) below for the
stable-first discipline that unlocks cache hits without any code or
configuration change.

### Tips from the Shipped Examples

The Content Quality Audit and Accessibility Quick-Scan workflows demonstrate patterns that
work well in practice:

- **Be explicit about tool-call sequences.** The scanner prompts specify exactly when to call
  `fetch_url` vs `write_file` and prohibit text-only turns between them. Vague instructions
  lead to stalls in interactive mode.
- **Define invariants.** Hard rules the agent must never violate. Number them so they're easy
  to reference.
- **Provide output templates.** Show the exact markdown structure the agent should write. This
  makes downstream steps predictable.
- **Handle failures in the prompt.** Tell the agent what to do when `fetch_url` returns an error,
  when content is truncated, or when input is unexpected.

### Interactive Mode Considerations

In interactive mode, any assistant turn that produces text without a tool call **pauses execution**
and waits for user input. This is by design -- it's how the agent asks questions and presents
results. But it means:

- An agent that narrates ("Let me fetch that for you...") before calling a tool will stall
- Progress updates must be in the same turn as a tool call, not standalone
- The agent's first message is typically a prompt asking the user for input

Design your prompts with this in mind. The shipped scanner prompts use numbered invariants to
prevent mid-task stalls.

## Artifact Handoff

Steps communicate through artifacts -- files written to the instance's working directory.

### How It Works

1. Step A declares `writes_to: [artifacts/scan-results.md]` and its agent calls `write_file`
   to create that file
2. Step B declares `reads_from: [artifacts/scan-results.md]` -- the engine verifies the file
   exists before Step B starts. If it's missing, Step B fails immediately (no LLM call)
3. Step B's agent calls `read_file` to load the artifact and work with it

### Key Points

- **`reads_from` is enforced.** Missing input artifacts block the step. This catches broken
  handoffs early.
- **`writes_to` is informational.** The engine doesn't enforce that the agent actually writes
  these files (that's what `completion_check` is for). But they appear in the prompt context
  and the dashboard artifact browser.
- **Artifact paths are relative** to the instance's working directory (not the workflow folder).
  Use `artifacts/` as a convention for clarity.
- **Prior step artifacts are visible.** The prompt context lists artifacts from all completed
  steps with existence flags, so later steps can see what earlier steps produced even without
  explicit `reads_from`.

## Prompt Variables

Agent prompts can include `{token}` placeholders that the runner resolves to
concrete values before the LLM ever sees the prompt. This is the right tool
for values the runner knows deterministically -- today's date, the running
instance id, or workflow-level config -- rather than relying on the LLM to
guess or on the user to supply them in conversation.

The canonical use case is stamping output artifacts with the real run date:
the `umbraco-content-audit` workflow's prompts write `Date: {today}` into
their artifacts and the runner substitutes `{today}` for the current UTC
date in ISO-8601 format, killing the common "the agent hallucinated a date"
bug. Note that the date is always UTC regardless of server locale — a
server in Sydney at 02:00 local on the 18th still sees `{today}` resolve
to the 17th until UTC midnight passes.

### Available Tokens

| Token | Value | Notes |
|---|---|---|
| `{today}` | Current UTC date in `YYYY-MM-DD` format | Date only — no time component. Always UTC regardless of server locale; changes on the UTC day boundary. |
| `{instance_id}` | Full GUID string identifying the running instance | Useful for correlating artifacts, logs, and external references. |
| `{step_index}` | Zero-based index of the current step within the workflow | The first step is `0`, not `1`. |
| `{previous_artifacts}` | Markdown bullet list of artifacts from completed prior steps | Each line: `- {path} (from step "{stepName}"): exists\|missing`. Empty workflows render `- No prior artifacts`. |
| `{<config_key>}` | Value from the workflow's `config:` block (see below) | Resolves only when the key exists in the workflow's declared config. |

Token matching is **case-sensitive** and limited to the ASCII set
`[a-z0-9_]+`. `{Today}`, `{INSTANCE_ID}`, and `{instanceId}` are all left as
literal text -- they do not resolve to anything.

### Workflow Config Block

Declare a workflow-level `config:` block with flat key/value pairs to expose
author-defined values to every step:

```yaml
name: Localised Audit
description: Demonstrates config-driven variable injection
config:
  language: en-GB
  severity_threshold: medium
steps:
  - id: analyser
    name: Analyser
    agent: agents/analyser.md
```

The analyser's prompt (e.g. `agents/analyser.md`) can then reference
`{language}` and `{severity_threshold}`, and the runner substitutes them
before the LLM call.

**Rules:**

- **Keys** must match `^[a-z0-9_]+$` (snake_case, lowercase). `config.Language`
  and `config.severity-threshold` are rejected at workflow load.
- **Values** must be strings. Nested maps and lists are rejected at load
  time. If you need structure, flatten it (e.g. `severity_threshold: medium`
  rather than `severity: { threshold: medium }`).
- **Absent/empty** `config:` is legal. Workflows that don't declare a config
  block still receive the four built-in runtime variables above.

**Precedence.** Runtime-provided variables (`{today}`, `{instance_id}`,
`{step_index}`, `{previous_artifacts}`) always win over workflow config. If
a workflow declares `config: { today: "2000-01-01" }`, the real server date
still resolves in prompts -- the config value is silently ignored.

**No recursive expansion.** Config values are inserted literally. If a
config value itself contains `{other_token}`, the inner token is not
re-substituted. Keep values simple strings; put placeholder composition in
the agent prompt itself.

### Unknown Tokens

A token the runner doesn't recognise (a typo, an illustrative example, or a
future variable the prompt anticipates) is **left as literal text** in the
assembled prompt. The runner emits a single `Warning` log line per distinct
unknown token per step -- look for `Unresolved prompt variable in step
{StepId}: {Token}` in the engine logs if you're debugging why a token isn't
substituting.

This design intentionally avoids throwing on unknown tokens so that
prompts can freely include literal braces in comments, code fences, and
meta-examples without breaking.

### Escaping Braces

Need a literal `{today}` in your prompt without substitution (for example,
documenting the feature inside a prompt)? Use double braces:

```text
Write the author-literal text {{today}} without the runner replacing it.
```

At assembly time this renders as `{today}` in the final prompt. The escape
pass runs before token substitution, so no `Warning` log fires for escaped
tokens.

### Canonical Example: Dated Artifact Output

The shipped `umbraco-content-audit` workflow uses `{today}` in every
artifact template to stamp run dates authoritatively:

```markdown
# Umbraco Content Audit Report

Date: {today} | Content nodes audited: [number]

## Executive Summary
...
```

Note the split convention: `{token}` is a **runner-substituted** value
(supplied by the engine), whereas `[bracket]` syntax is an **LLM template
slot** that the agent fills in from tool results (here, `[number]`). Use
`{token}` only for values the runner can provide deterministically; use
`[bracket]` for anything the agent must reason about.

## Prompt Caching

Long workflows that iterate through many items (e.g. the Content Audit
scanner calling `get_content` across hundreds of nodes, one node per turn)
re-send the same system prompt + tool schemas on every LLM call. Provider-
side prompt caching lets the model read that stable preamble from cache at
a 90% discount on Anthropic, or benefit from automatic threshold-based
caching on OpenAI and Azure OpenAI — without any code or configuration
change on your side, as long as your prompts follow the stable-first
discipline below.

### What's Cached Automatically

The runner annotates every System message with a provider-neutral
`Cacheable` hint on the way out. Each provider adapter decides what to do
with it:

| Provider | Behaviour |
|---|---|
| OpenAI / Azure OpenAI | Ignores the hint. Automatic caching engages once the prompt crosses the provider's minimum-size threshold (currently ~1024 tokens). No action required. |
| GitHub Copilot / Google Gemini / Ollama / local | Ignores the hint. Caching (if any) depends on the specific provider and is out of scope here. |
| Anthropic (Claude) | **Today:** ignores the hint — the current Umbraco.AI.Anthropic + Microsoft.Extensions.AI chain does not translate the neutral hint into Anthropic's native `cache_control` marker. Tracked as a planned follow-up. **Once that follow-up ships:** the System message will carry `cache_control: { type: "ephemeral" }` and subsequent turns within a step will read from cache. |

You don't need to do anything provider-specific in your workflow. Your job
is to write cache-friendly prompts (below); the runner and the adapter
handle the rest.

### Prompt-Author Discipline: Stable First

For caching to work — on any provider, automatic or marker-driven — the
stable parts of your prompt must come **first**, before anything that
varies from run to run. Caching covers the longest stable prefix, so even
one variable byte near the top invalidates everything after it.

**Put this at the top (stable):**

- Identity, principles, role description
- Hard invariants and locked rules
- Scoring rubrics, severity bands, evaluation criteria
- Tool-calling procedure and failure handling
- Output templates (the *structure*, with `[bracket]` slots)

**Put this near the bottom (variable):**

- References to prior-step artifacts by name
- `{token}` substitutions that vary per run (especially `{today}`)
- Any user-supplied scope that's woven into the prompt text

The shipped `umbraco-content-audit` scanner follows this pattern — the
entire agent markdown is stable across a single run, and the only
per-run variation is the Runtime Context section that the engine appends
after your prompt. If you follow the same layout, your workflow is
cache-ready by construction.

**Do:**

```markdown
# My Scanner Agent

## Identity
You are a measurement instrument...

## Invariants
1. Always call tools before narrating...

## Output Template
# Results for [node name]
...
```

**Don't:**

```markdown
# My Scanner Agent

Today is {today}. The user asked to audit [something specific].

## Identity
You are a measurement instrument...
```

The "don't" example leaks two variable bytes (`{today}` plus the
user-supplied scope) above the stable identity section — every run writes
a new cache entry instead of reading an existing one.

### Observability

Every step emits a structured `cache.usage` log line at Information level
with these fields:

- `step`, `workflow`, `instance` — correlation identifiers
- `input`, `output` — total input / output token counts for the step
- `cached_input` — Microsoft.Extensions.AI `UsageDetails.CachedInputTokenCount`
  (tokens the provider read from cache)
- `cache_read`, `cache_write` — provider-specific extras (Anthropic
  surfaces these as `CacheReadInputTokens` / `CacheCreationInputTokens`;
  OpenAI and Azure don't split the counts today)

Zeros are signal, not absence. If your Anthropic workflow is supposed to
be caching but `cached_input` is 0 turn after turn, the hint isn't being
translated to `cache_control` yet (see the follow-up note above) or your
stable preamble is under Anthropic's 1024-token minimum.

**Filtering in Serilog** (other sinks follow the same pattern):

```csharp
Log.Logger = new LoggerConfiguration()
    .Filter.ByIncludingOnly(e => e.MessageTemplate.Text.StartsWith("cache.usage"))
    .WriteTo.File("logs/cache-usage.log")
    .CreateLogger();
```

Umbraco.AI's backoffice Analytics dashboard covers general token usage
(requests, input / output tokens, success rate, provider / model / profile
breakdowns) — the `cache.usage` log line is AgentRun's contribution for
the cache read / write split that Analytics doesn't yet show.

### Out of Scope

- **Tool-schema caching.** Tool declarations pass through
  `ChatOptions.Tools`, a separate transport channel. A future pass may
  extend caching to tool schemas once Anthropic's 4-breakpoint budget is
  understood empirically.
- **Cross-step cache reuse.** Caching is optimised for within-step tool
  loops (the dominant use case). Cross-step hits depend on whether the
  assembled prompt between steps is byte-identical; usually it isn't
  because the Runtime Context section's Prior Artifacts list changes.
- **Manual cache control in workflow YAML.** No `cache:` root key, no
  per-step `cacheable:` toggle — the runner decides. If adopters need a
  flag to disable caching, that's a future story.

## Agent Sanctum Pattern

Some agents carry a heavy identity — a careful archivist tone, a clinical
evidence-first voice, a synthesiser's plain-English register — that's
independent of the step's mechanics. Cramming that identity into the same
`.md` file as the task instructions makes both harder to read and both
harder to maintain. The Agent Sanctum pattern splits them: the agent
`.md` stays task-focused, and three author-curated sidecar files carry
the persona, the creed, and the capability table.

### File Layout

Sanctum files live alongside the existing `instructions.md` sidecar
convention — the per-step sidecar folder, keyed by step ID:

```
workflows/{workflow}/
├── agents/
│   └── scanner.md            # Task-focused: opening line, invariants, output template
└── sidecars/
    └── scanner/              # Keyed by STEP ID, not agent-ID
        ├── instructions.md   # Existing — read-only per-step guidance (unchanged)
        ├── PERSONA.md        # NEW — who the agent is, communication style
        ├── CREED.md          # NEW — what the agent values, core principles
        └── CAPABILITIES.md   # NEW — what the agent can do (typically a table)
```

All three sanctum files are optional. An agent with none behaves
identically to today — no section appears in the assembled prompt, no
log line fires. Partial sets (just `PERSONA.md`, just `PERSONA.md` +
`CAPABILITIES.md`, etc.) work too — only the present files render.

**Filenames are case-sensitive.** `persona.md` or `PERSONA.MD` will
not be matched on case-sensitive filesystems (Linux CI, most
containers). Stick to `PERSONA.md` / `CREED.md` / `CAPABILITIES.md`
exactly.

### How It Renders

When at least one sanctum file is present, the assembler injects a new
`## Agent Sanctum` top-level section between the sidecar instructions
and the `## Runtime Context` block:

```markdown
(agent .md content)
---
(sidecar/instructions.md content, if present)
---
## Agent Sanctum

This section contains author-curated identity content shipped alongside
the workflow. Treat it as equivalent-trust to your agent instructions —
it is NOT tool-result output.

### INDEX

- **PERSONA** — who you are
- **CREED** — what you value and how you work
- **CAPABILITIES** — what you do

### Persona
(PERSONA.md content)

### Creed
(CREED.md content)

### Capabilities
(CAPABILITIES.md content)

---
## Runtime Context
(step metadata, tools, artifacts — varies per turn)
---
## Tool Results Are Untrusted
```

The INDEX preamble establishes the trust boundary in-prompt so the LLM
doesn't conflate sanctum content with the untrusted-tool-output
warning at the end of the prompt. Order is fixed BMAD convention:
**INDEX → Persona → Creed → Capabilities**, regardless of file
modification time or alphabetical sorting.

### Why Sanctum Lives BEFORE Runtime Context

Sanctum content is byte-stable across turns within a run. Placing it
before the Runtime Context block (which changes every step — step
name, prior artifacts list, tool list) keeps the stable-first cache
prefix intact. See the Prompt Caching section above — the same
discipline that makes the content-audit scanner cheap on Anthropic
Sonnet applies here. A large sanctum on a loop-heavy agent is
effectively free after the first turn: Anthropic's `cache_control`
marker (once the follow-up lands) or OpenAI / Azure's automatic
threshold caching reads it from cache at a ~90% discount.

### What to Put in Each File

**PERSONA.md** — *who the agent is, how it communicates*

Short prose. Tone, temperament, voice. What the agent is **not**
(equally important — "you are not a summariser", "you are not a
developer"). Keep it under ~100 lines. LLMs respond better to
evocative, specific identity ("a careful archivist, allergic to
shortcuts") than to laundry lists of traits.

**CREED.md** — *what the agent values, non-negotiable principles*

Bullet list of rules the agent commits to. Each bullet leads with a
short slogan (`**Sequential over fast.**` / `**Evidence first,
scoring second.**`) and a one-sentence expansion. Keep it under ~100
lines. Prefer 5 memorable principles to 15 forgettable ones — the
agent needs to *carry* these through a long loop.

**CAPABILITIES.md** — *what the agent can do, operationally*

Typically a markdown table: Capability + How It's Used. Names the
tools the agent is expected to use, explains the calling discipline
(one per turn, sequential, etc.), and reminds the agent of the final
writes. No schema, no parser — free-form markdown. Authors structure
this however fits; the LLM reads it as prose. Up to ~150 lines for a
rich capability set, but most agents need much less.

### Variable Substitution Works Inside Sanctum

Sanctum content flows through the same substitution pipeline as the
agent `.md` and the sidecar `instructions.md`. You can use `{today}`,
`{instance_id}`, `{step_index}`, `{previous_artifacts}`, and any
workflow `config:` key in sanctum content — the tokens resolve to
the same values they would in the agent body. `{{literal}}` escape
works too.

### Trust Boundary

Sanctum files are ship-time content — they live in your workflow
folder, tracked in Git alongside the workflow `.yaml` and the agent
`.md`. The runner treats them as **equivalent-trust to the agent
instructions**, not as tool-result output. The in-prompt preamble
makes this explicit so the LLM doesn't second-guess persona content.

If you want user-scoped identity (per-end-user persona) or
agent-written memory (the agent updates its own PERSONA over time),
those are not part of this pattern — they belong to a future
write-capable extension.

### Backwards Compatibility

Workflows without sanctum files behave identically to pre-sanctum
AgentRun. No `## Agent Sanctum` section, no Debug log line, no prompt
shape change. Every existing workflow keeps working unchanged. Adopt
sanctum files incrementally as you build new workflows or when
refactoring an existing agent feels valuable — there's no migration
path because there's no migration.

### Token-Budget Guidance

Because sanctum loads into every step-execution prompt, large
sanctum files multiply by the loop count. A 500-line PERSONA on a
scanner that loops 50 times is effectively 25,000 lines of persona
in the run. Soft limits:

- PERSONA.md ≤ ~100 lines
- CREED.md ≤ ~100 lines
- CAPABILITIES.md ≤ ~150 lines (more if it's a structured table
  that benefits from length)

The runner doesn't enforce these — they're author guidance. Prompt
caching (above) mitigates the cost on cache-aware providers, but
smaller sanctum files still read better and are easier to maintain.

### Canonical Example

The shipped `umbraco-content-audit` workflow uses the pattern across
its three agents — scanner / analyser / reporter. Read the
`sidecars/{stepId}/` folders under
`AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/umbraco-content-audit/`
for a working reference. Each agent's `.md` file stays focused on
its task (opening line, invariants, output template) while its
identity, principles, and capabilities live in the sanctum.

### Out of Scope

- **No write capability.** Agents cannot create, update, or delete
  sanctum files via tool calls. PERSONA / CREED / CAPABILITIES are
  author-edited with a regular filesystem editor. Agent-written
  memory (BOND / MEMORY / session logs / capabilities the agent
  learns) is a separate future direction.
- **No user-scoping.** Sanctum is per-step, not per-user. A future
  memory-agent pattern would add per-user identity layered on top.
- **No `agent_type` YAML field.** `workflow.yaml` schema is
  unchanged. Sanctum is a pure filesystem convention.
- **No runtime size enforcement.** Docs guidance only.

## Conditional Pillars via Config

Some workflows benefit from optional pillars / features that adopters toggle
via the workflow's `config:` block rather than by maintaining separate
workflow variants. The shipped `umbraco-content-audit` workflow uses this
pattern for its optional **Brand** pillar.

The pattern has three moving parts:

1. **A `config` key toggles the feature.** An empty string disables it; a
   non-empty string enables it with that value as input to the pillar.
2. **Agent prompts ship dual verbatim-locked variants of their user-facing
   surfaces.** A "6-pillar variant" (feature disabled) and "7-pillar variant"
   (feature enabled) for each of: Opening Line, Capability Answer, Re-prompt,
   Charitable Confirmation Template. The LLM selects at runtime based on
   whether the substituted token is empty or non-empty.
3. **The scanner pre-validates the configured value before starting the
   audit.** A failed validation halts with a structured user-facing error —
   it never silently falls back to the disabled variant. Misconfiguration
   deserves visibility.

### Worked example: `brand_voice_context`

Add to your `workflow.yaml`:

```yaml
config:
  brand_voice_context: ""   # empty disables Brand pillar; set to an Umbraco.AI Context alias to enable
```

Agent prompts reference the value via the
[Prompt Variables](#prompt-variables) substitution pipeline — the
`{brand_voice_context}` token resolves at prompt-assembly time. The
scanner's Brand Pillar Configuration preamble renders the substituted
alias so the LLM can route:

```markdown
Brand voice context alias: **{brand_voice_context}**

- If the bold text above is **empty** (renders as `****`) → Brand disabled.
  Use 6-pillar variants throughout.
- If the bold text above is a **non-empty alias** → Brand enabled. Use
  7-pillar variants. Follow the Brand Pillar Pre-Validation Gate before any
  other tool call.
```

### When to use this pattern

- **Pillar / feature toggles that require author-curated content** (brand
  voice, industry-specific vocabulary, compliance frameworks). The author
  sets one config value; the workflow adapts.
- **Optional scoring dimensions** where a meaningful default exists (the
  6-pillar audit) and the feature adds scope on top.
- **Features whose enablement depends on external infrastructure** being
  present (in this case, an Umbraco.AI Context existing). The pre-validation
  gate catches misconfiguration early.

### When NOT to use this pattern

- **Simple on/off flags** with no author content. Use a `variants:` block
  once that mechanism stabilises in a future story (today's `variants:` key
  in `workflow.yaml` is reserved — don't rely on its shape yet).
- **Settings that vary per-run** (e.g. which nodes to scan). Those belong
  in user-interactive prompts or per-step config, not workflow-level config.
- **Feature flags with three or more states.** The dual-variant pattern is
  reliable with a binary decision; LLM variant-selection reliability drops
  sharply with more options.

### Gotchas

- **`config` values must be strings.** Use `""` to mean "disabled", not
  `null` or an absent key. An absent key leaves a literal `{token}` in the
  rendered prompt and emits a Warning; explicit empty string resolves cleanly.
- **Dual-variant drift risk.** Any edit that paraphrases one variant must
  equally paraphrase the other (or deliberately keep the other byte-locked).
  Integration tests that assert both variants are present catch accidental
  drift in CI — see `ContentAuditBrandPillarTests.cs` for the pattern.
- **Pre-validate at the earliest step.** The scanner gates the Brand pillar
  because a failing validation at the analyser step wastes 10–30 minutes of
  audit time. Push validation to the earliest tool-call boundary where the
  configured value is consumable.
- **No history / trendline in v1.2.** The Brand pillar scores this run's
  content; comparing to previous-run scores is Epic 13 work (cross-run agent
  memory). Document this as a known limitation in your adopter docs.

### Upgrading an existing workflow to add a conditional pillar

v1.0 / v1.1 adopters of AgentRun have their own `App_Data/` copies of the
shipped workflow files. The NuGet `CopyExampleWorkflowsToDisk` migration does
NOT overwrite existing copies — so upgrading the package does not
automatically pick up new workflow features. To adopt the Brand pillar on an
existing `umbraco-content-audit` installation:

1. Add a `config:` block to your `workflow.yaml` with
   `brand_voice_context: "<your-Context-alias>"`.
2. Add `get_ai_context` AND `search_content` to the `scanner` step's `tools:`
   array.
3. Add `get_ai_context` to the `analyser` step's `tools:` array.
4. Replace your `agents/scanner.md`, `agents/analyser.md`, and
   `agents/reporter.md` with the v1.2 canonical versions (find them in your
   NuGet packages folder at
   `~/.nuget/packages/agentrun.umbraco/<version>/contentFiles/any/any/Workflows/umbraco-content-audit/`).
5. Replace your sidecar CAPABILITIES.md / CREED.md files with the v1.2
   canonical versions (same source folder).

Keeping `brand_voice_context: ""` preserves the v1.1 6-pillar audit exactly.

## Completion Checking

The `completion_check` block tells the engine when a step is done:

```yaml
completion_check:
  files_exist:
    - artifacts/scan-results.md
```

Currently the only check type is `files_exist` -- a list of file paths that must all exist.

### How It's Used

The engine checks completion in two places:

1. **During the tool loop** -- after each tool-call round, the engine checks if the completion
   criteria are met. If they are, the step exits early (useful in interactive mode where the
   agent might otherwise pause for unnecessary user input).
2. **After the tool loop** -- a final check runs after the LLM conversation ends. If the
   completion criteria aren't met, the step fails.

If you omit `completion_check`, the step completes when the LLM conversation ends naturally
(no file existence verification).

## Profile Configuration

The LLM profile determines which AI provider and model handles a step. Profiles are configured
in the Umbraco backoffice under **Settings > AI**.

### Resolution Order

1. **Step `profile`** -- highest priority, for steps that need a specific model
2. **Workflow `default_profile`** -- applies to all steps that don't override
3. **Site `AgentRun:DefaultProfile`** in `appsettings.json` -- fallback for workflows that
   don't specify a default
4. **Auto-detection** -- if none of the above are set, AgentRun uses the first available
   Umbraco.AI profile (by alias alphabetical order). No configuration needed for sites
   with a single AI profile.

If no profile can be resolved (including auto-detection), the step fails with a clear error
directing the user to configure a provider in Settings > AI.

### When to Use Step-Level Profiles

Most workflows work fine with a single profile. Use step-level overrides when:

- A step needs a more capable model (e.g., analysis vs. simple fetching)
- A step needs a cheaper/faster model for high-volume work
- You want to test the same workflow against different providers

### Canonical Example: Content Audit Routing

The Content Audit workflow is a good illustration of where step-level
routing pays for itself. Scanner and analyser do high-volume, rubric-driven
work where a fast mid-tier model (Sonnet-class) is the price/quality sweet
spot. The reporter produces the single visible audit output -- paying for
a flagship model (Opus-class) here buys the most visible quality delta
for a small number of tokens, since the reporter's output is capped.

```yaml
name: Umbraco Content Audit
description: Audits content for quality, completeness, and structural issues
default_profile: anthropic-sonnet                 # fast tier for scanner + analyser
steps:
  - id: scanner
    name: Content Scanner
    agent: agents/scanner.md
    # no profile -- inherits default_profile (Sonnet)
  - id: analyser
    name: Quality Analyser
    agent: agents/analyser.md
    # no profile -- inherits default_profile (Sonnet)
  - id: reporter
    name: Report Generator
    agent: agents/reporter.md
    profile: anthropic-opus                       # flagship tier for the visible writeup
```

Profile aliases (`anthropic-sonnet`, `anthropic-opus`) are **placeholders**
-- replace them with your own aliases configured under **Settings > AI**.
An alias referenced here but not configured in Umbraco.AI causes the step
to fail with `ProfileNotFoundException` when it runs.

Because the reporter has a small token budget (typically 200 lines or
fewer per the shipped prompt), the cost delta across a full audit is
usually pennies rather than pounds -- most requests still flow through
the cheaper Sonnet tier.

## Available Tools

Workflows declare which tools each step can use. Only declared tools are available -- the
engine blocks calls to undeclared tools.

### `fetch_url`

Fetches a URL via HTTP GET.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `url` | Yes | The URL to fetch |
| `extract` | No | `"raw"` (default) or `"structured"` |

**Raw mode** (`extract: "raw"` or omitted): fetches the response body, saves it to the
instance's `.fetch-cache/` directory, and returns a JSON handle with URL, status, content type,
size, and the cached file path. The agent can then use `read_file` to inspect the content.

**Structured mode** (`extract: "structured"`): parses the HTML server-side and returns a
compact JSON summary: title, meta description, heading structure, word count, image alt-text
coverage, link counts, semantic element presence, and lang attribute. No file is written to
disk. Use this when the agent needs facts about the page rather than the raw HTML.

**Security:** all URLs are validated against SSRF rules before fetching. Private/internal
IP ranges (10.x, 172.16-31.x, 192.168.x), loopback (127.x, ::1), and link-local (169.254.x)
addresses are blocked. Redirects are followed (up to 5 hops) but each hop is re-validated.

### `read_file`

Reads a file from the instance's working directory.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `path` | Yes | Relative file path within the instance folder |

Returns the file contents as UTF-8 text. Large files are truncated at the configured limit
(default 256 KB) with a marker indicating truncation.

**Security:** all paths are sandboxed to the instance folder. Directory traversal (`../`) and
symlink escapes are blocked.

### `write_file`

Writes content to a file in the instance's working directory.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `path` | Yes | Relative file path within the instance folder |
| `content` | Yes | The content to write (can be empty) |
| `append` | No | If `true`, append content to the existing file (creating it if absent). If `false` or omitted, overwrite the file atomically. |

Creates parent directories as needed. **Overwrite mode** (default) is atomic (temp file +
rename) so partially written files never appear on disk. **Append mode** is not atomic — the
file may be partially written if cancellation fires mid-write. That's the right trade-off for
append's use case: streaming per-item outputs where each append happens while the source data
is fresh in the agent's context.

**Append size cap.** Append-mode writes are bounded at 10 MB per file (aggregate existing
size + incoming content). A write that would exceed this returns a structured
`ToolExecutionException` the agent can self-correct on. The cap guards against runaway
tool-call loops and keeps `read_file` downstream readable. Overwrite-mode writes have no
aggregate cap — they're naturally bounded by each call's content size.

**Append typo guard.** When `append: true` creates a previously-missing file, a Warning is
logged so operators can catch path typos (e.g. `scan-resultts.md` vs `scan-results.md`)
that silently produce parallel malformed artifacts.

**When to use `append: true`.** LLMs reliably copy tool-result data into markdown while that
data is in immediate context (1–2 turns old), but lose fidelity as the data scrolls back
(4+ turns). A workflow that scans N items and writes a consolidated report can hit
content-drift / hallucination if it batches all N items into a single final `write_file`
call. The fix: initialise the report with `write_file(append: false)` (header only), then after
each per-item tool call immediately append that item's section via `write_file(append: true,
content: <that item's section>)`. Each append happens one turn after the item's tool response
so the data is fresh, producing byte-accurate markdown. Then a final append for any summary.

This pattern is the intended use for streaming scanners, per-URL fetch-and-summarise workflows,
per-node audit reports, per-ticket triage logs, and similar iteration-heavy outputs.

**Security:** same path sandboxing as `read_file`.

### `list_files`

Lists all files in the instance's working directory (or a subdirectory).

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `path` | No | Subdirectory to list (empty = instance root) |

Returns newline-separated relative file paths, sorted alphabetically. Useful when the agent
needs to discover what files exist before reading them.

### Umbraco Content Tools

The following tools are available when the workflow runs inside an Umbraco instance. They read
from the published content cache in-process -- no HTTP requests, no SSRF concerns.

### `list_content`

Lists published content nodes from the Umbraco instance. Optionally filter by document type
alias or parent node ID.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `contentType` | No | Filter by document type alias (e.g., `'blogPost'`). Returns all types if omitted |
| `parentId` | No | Filter to direct children of this content node ID. Returns all content if omitted |

Returns a JSON array of objects: `id`, `name`, `contentType`, `url`, `level`, `createDate`,
`updateDate`, `creatorName`, `childCount`. Large result sets are truncated with a marker
indicating how many nodes were returned vs total.

**Access:** reads from Umbraco's published content cache in-process. No network calls.

### `get_content`

Gets the full details and property values of a single published content node by ID or Key.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `id` | Yes | The node reference — either the Umbraco GUID **Key** (preferred; durable across environments) or the integer tree ID (legacy; env-variable). Both formats accepted; the backoffice URL typically shows the GUID. |

Returns a **summary handle** to the LLM (full content is offloaded to an instance-local cache
file — see "Offload" below): **`key`** (GUID — primary node reference), `id` (integer — display
only), `name`, `contentType`, `url`, `propertyCount`, `size_bytes`, `saved_to`, `truncated`,
**`body_text`**, **`body_metadata`**.

**GUID-first node identification:** Integer `id` values vary across environments (dev ≠ stage ≠
prod, DB restores can renumber) and should be treated as display-only. The GUID `key` is the
durable reference. Artifacts that reference nodes (scan-results.md, quality-scores.md,
downstream memory, cross-workflow refs) should carry the GUID to survive environment promotion.

Property extraction: Rich Text is stripped to plain text, Text String/Textarea as-is, Content
Picker shows name + URL, Media Picker shows name + URL + alt text, Block List/Grid shows
simplified block content, TrueFalse shows `"true"`/`"false"`, fallback is JSON-serialised
source value.

**Access:** same in-process published content cache. Large responses are truncated by removing
properties from the end.

#### Offload

The full response (11 fields: `key`, `id`, `name`, `contentType`, `url`, `level`,
`createDate`, `updateDate`, `creatorName`, `templateAlias`, and the `properties` dict) is
written to `.content-cache/{id}.json` within the workflow instance folder. The LLM receives
only the summary handle. Workflows that need to re-read full content between steps use
`read_file` on the cache file path in `saved_to`.

Note: the cache file carries `key` (GUID) as its primary node reference alongside the
integer `id` — durable artifacts that reference nodes should read the GUID from the cache
file, not just the display-only integer id. `body_text` and `body_metadata` are handle-only
and do NOT appear in the cache file.

#### Body extraction (`body_text` + `body_metadata`)

Two additive fields on the summary handle carry pre-extracted body content, computed
deterministically in C# before the response is returned:

- **`body_text`** — concatenated prose extracted from the node's body-copy property, HTML
  stripped and whitespace normalised. Capped via workflow-level config (default 15000 chars).
  When the full prose exceeds the cap, the literal marker ` [...truncated at N of M chars]` is
  appended — M is the pre-cap length, so operators can see how much content was excluded.
- **`body_metadata`** — always-full structural extraction (never capped): `headings` (list of
  `{level, text}`), `links` (list of `{text, target}`), `alt_texts` (list of
  `{image_src, alt}`), `image_count`. Parsed via AngleSharp from the full concatenated HTML
  (not the truncated body_text).

**Which property yields the body?** A 3-tier first-match-wins priority list walks
`node.Properties`:

1. **Tier 1 — Block List / Block Grid composition aliases (in order):**
   `contentRows`, `mainContent`, `pageContent`, `blocks`, `contentGrid`
2. **Tier 2 — direct text / RTE aliases (in order):** `bodyContent`, `bodyText`, `articleBody`,
   `body`, `content`, `description`, `richText`
3. **Tier 3 — editor-alias fallback:** any property whose `PropertyType.EditorAlias` is
   `Umbraco.RichText`, `Umbraco.BlockList`, or `Umbraco.BlockGrid` (in property-declaration
   order). `Umbraco.TextBox` / `Umbraco.TextArea` are intentionally NOT matched in Tier 3 —
   short-text editors imply non-body use (headlines, SEO titles).

**Three-state return distinction:**

- Both fields `null` → content type has no body-copy property at all (layout-only / navigation /
  redirect node).
- `body_text: ""` + `body_metadata: {headings: [], links: [], alt_texts: [], image_count: 0}` →
  body property exists but contains no prose (unsaved draft, empty block list, all blocks are
  non-textual — images only, CTAs only, etc.).
- `body_text: "<prose>"` + populated `body_metadata` → body content was successfully extracted.

**Alt-text three-state preservation (WCAG-aware):**

| HTML | `alt_texts` entry |
|---|---|
| `<img src="/hero.jpg" alt="Hero banner">` | `{image_src: "/hero.jpg", alt: "Hero banner"}` |
| `<img src="/deco.jpg" alt="">` | `{image_src: "/deco.jpg", alt: ""}` (decorative — WCAG H67 correct) |
| `<img src="/missing.jpg">` | `{image_src: "/missing.jpg", alt: null}` (WCAG 1.1.1 failure) |

#### Configuration — `content_audit_body_max_chars`

Set in the workflow's `config:` block (per-workflow, not site-wide):

```yaml
config:
  content_audit_body_max_chars: "15000"  # string per YAML constraint; parsed at tool runtime
```

- Default: **15000** (covers ~95% of editorial content fully; very long pillar pages get
  truncated with marker).
- Lower (e.g. `"5000"`) for large-site audits where prompt budget matters across 200+ nodes.
- Raise (e.g. `"50000"`) for sites with long-form pillar pages where sampling risk matters.
- Non-integer / zero / negative values fall back to 15000 + a per-(stepId, value) deduped
  Warning log.
- Values above 10,000,000 (the internal upper bound guarding against OOM allocations) also
  fall back to 15000 with the same deduped Warning.
- The cap applies to `body_text` ONLY. `body_metadata` is never capped for `body_text`
  budget reasons, but each metadata list (`headings`, `links`, `alt_texts`) is bounded at
  1000 items as a defensive cap against pathological auto-generated pages.
  `image_count` always reflects the true image total so accessibility audits can detect
  the truncation.

#### Caveats

- **Nested Block Lists / Block Grids** — the extractor recurses up to 5 levels deep. Nested
  block content (Block Grid column containing a Block List, etc.) is extracted. Deeper
  nesting than 5 levels is truncated with a Warning log — surface those as separate
  top-level properties if you need to audit them.
- **Empty Block Lists** — a content type with an empty Block List property alongside a
  populated Rich Text body (common draft pattern) falls through to the Rich Text property
  rather than returning the "empty body" state. The empty state is reserved for content
  types whose only body-bearing property is genuinely empty (null or zero blocks).
- **Multi-language variants** — extracts the default-language value (same as existing
  `get_content` behaviour for all properties).
- **Custom property editors** — Tier 3 only matches Umbraco's built-in RichText/BlockList/
  BlockGrid editors. A custom editor alias that stores rich prose but doesn't match any alias
  in Tiers 1/2 will be missed. Standardise on the canonical aliases (`contentRows`,
  `bodyContent`, etc.) to benefit from extraction.
- **Cache file has 11 fields, body fields are handle-only** — `.content-cache/{id}.json`
  carries `key` (GUID) + 10 node-identity/content fields. `body_text` and `body_metadata`
  are NOT in the cache file; downstream tools reading the cache get the raw `properties`
  dict and can run their own extraction if needed.
- **Character-set safety** — truncation walks back to the previous grapheme boundary if the
  cap would split a surrogate pair (emoji, CJK supplementary-plane characters) so
  `body_text` always round-trips cleanly through UTF-8.
- **Scripts and styles stripped** — `<script>` / `<style>` subtrees are removed wholesale
  before tag-stripping so neither their tags nor their text content appear in `body_text`.
  Metadata (`body_metadata`) is parsed via AngleSharp and follows the same DOM semantics.

### `list_content_types`

Lists document type definitions from the Umbraco instance, including their properties,
compositions, and allowed child types.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `alias` | No | Filter by document type alias. Returns all document types if omitted |

Returns a JSON array of objects: `alias`, `name`, `description`, `icon`, `properties` (array
of `{alias, name, editorAlias, mandatory}`), `compositions` (array of aliases),
`allowedChildTypes` (array of aliases). Large responses are truncated.

**Access:** reads from `IContentTypeService`. No published content cache needed.

### `web_search`

Searches the web via a configured provider (Brave by default, Tavily as a second
registered provider) and returns URL + title + snippet results. URLs are **not** fetched —
call `fetch_url` on any result URL that needs to be read in full.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `query` | Yes | The search query |
| `count` | No | 1–25, default 10. Each adapter clamps to its provider's ceiling (Brave 20, Tavily 20) |
| `freshness` | No | `last_day` / `last_week` / `last_month` / `last_year` / `all` (default `all`) |

Returns a JSON object: `{ "provider": "Brave", "results": [ { title, url, snippet, publishedDate, sourceProvider, relevanceScore } ] }`. Fields absent in the provider response are `null`.

#### Configuration

Web search needs at least one provider API key in `appsettings.json`:

```json
{
  "AgentRun": {
    "WebSearch": {
      "DefaultProvider": "Brave",
      "CacheTtl": "01:00:00",
      "Providers": {
        "Brave":  { "ApiKey": "<your-brave-key>" },
        "Tavily": { "ApiKey": "<your-tavily-key>" }
      }
    }
  }
}
```

- Get a Brave key (free tier, 2000 queries/month) at `api.search.brave.com`.
- Get a Tavily key (free dev tier) at `tavily.com`.
- Env-var overrides work the usual way: `AGENTRUN__WEBSEARCH__PROVIDERS__BRAVE__APIKEY=...`.
- No backoffice UI for keys in v1 — a future dashboard epic may add one.

#### Provider Selection

The tool picks the provider via this chain:

1. `AgentRun:WebSearch:DefaultProvider` (if set AND that provider has an API key).
2. First registered provider with a configured API key (Brave before Tavily).
3. Otherwise → structured `not_configured` error result.

Switching providers is a config change + restart; no workflow YAML edits needed.

#### Caching

Responses are cached in memory for `CacheTtl` (default 1 hour) keyed on
`(provider, normalisedQuery, count, freshness)`. Query normalisation lowercases + trims +
collapses whitespace, so `"Umbraco 16"` and `"umbraco   16"` share a cache entry. The cache
resets on app restart — no disk persistence.

#### Error Shapes the Agent Sees

Rather than throw, the tool returns structured JSON errors so the LLM can reason about them:

| `error` | When | LLM should |
|--------|------|-----------|
| `not_configured` | Provider has no API key, OR the configured key was rejected (HTTP 401/403) | ask user, skip search, or proceed without web context — do NOT retry |
| `rate_limited` | HTTP 429 from provider (carries `retryAfterSeconds`) | wait, try a different query, or move on |
| `transport` | 5xx, DNS error, timeout, malformed JSON, body-read failure | try again or move on |
| `invalid_argument` | Bad tool-call arguments (empty query, count out of range) | fix the tool call and retry |

#### Security Notes

- API keys are read from `appsettings.json` and never logged, echoed in tool results, or
  surfaced to the LLM. Rotating a key = edit appsettings + restart.
- Search-result URLs are **not** automatically fetched. If the agent wants content, it calls
  `fetch_url`, which applies SSRF protection at that point.
- There is no automatic provider fallback (e.g. Brave 429 → Tavily). The agent sees the
  rate-limit error and decides.

#### Workflow Example

```yaml
name: External Research
alias: external-research

steps:
  - id: research
    name: Research
    agent: agents/researcher.md
    tools: [web_search, fetch_url, write_file]
    writes_to: [research-notes.md]
```

### `get_ai_context`

Reads an Umbraco.AI Context by alias or by content-node tree-inheritance. Returns the
Context's resources (brand voice, tone-of-voice guidance, reference text) so agents can
pull on-brand guidance into their work. Read-only — no write capability in v1.

> **Pattern tip:** if you want to ship a workflow with an optional pillar / feature that
> wraps `get_ai_context` retrieval, see [Conditional Pillars via Config](#conditional-pillars-via-config)
> for the canonical adopt-for-your-workflow pattern — including pre-validation discipline,
> dual-variant prompt surfaces, and the load-bearing "configured empty" vs "configured
> non-empty" semantics.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `alias` | No | The alias of the Context (e.g. `"corporate-brand-voice"`). Takes precedence if both are supplied. |
| `content_node_id` | No | The ID of a published content node whose AI Context Picker property (or its nearest ancestor's) selects the Context. |

At least one of `alias` or `content_node_id` must be supplied — the tool returns a
structured `invalid_argument` result if both are omitted.

Returns a JSON object: `{ alias, name, version, resolvedFrom?, resources: [...] }`. Each
resource has `name`, `description`, `resourceTypeId` (e.g. `"brand-voice"`, `"text"`),
`sortOrder`, `injectionMode` (`"always"` or `"on_demand"`), and `settings` (the raw
Umbraco.AI settings object for that resource type — the LLM reads fields directly).
For the `brand-voice` type shipped in Umbraco.AI 1.8, `settings` has `toneDescription`,
`targetAudience`, `styleGuidelines`, and `avoidPatterns` — each a free-text string. For
the `text` type, `settings` has a single `content` string.

The `resolvedFrom` field is only populated in `content_node_id` mode and identifies which
ancestor's picker value won the tree-walk: `{ contentNodeId, contentNodeName }`.

#### Lookup modes

**Alias mode** — workflow author knows exactly which Context to load:

```yaml
config:
  brand_voice_alias: "corporate-brand-voice"

steps:
  - id: writer
    agent: agents/writer.md
    tools: [get_ai_context, write_file]
```

In the agent prompt, call `get_ai_context(alias: "{brand_voice_alias}")` — the
`{brand_voice_alias}` token resolves via the
[Prompt Variables](#prompt-variables) substitution pipeline.

**Content-node-ID mode** — the Context is attached to a content node via the AI Context
Picker property editor; tree inheritance lets subtrees override parent contexts. For a
target content node `N`, the tool walks `N → parent → grandparent → …` and returns the
first picker value that resolves to a live Context. A child node can override its
parent's brand voice by picking a different Context on its own doc-type property.

```yaml
steps:
  - id: writer
    agent: agents/writer.md
    tools: [get_ai_context, get_content, write_file]
```

The agent calls `get_ai_context(content_node_id: N)` where `N` is the content ID the
workflow is authoring for.

#### Result Shape

```json
{
  "alias": "corporate-brand-voice",
  "name": "Corporate Brand Voice",
  "version": 3,
  "resolvedFrom": { "contentNodeId": 2000, "contentNodeName": "Services" },
  "resources": [
    {
      "name": "Tone of Voice",
      "description": "Our house style guide",
      "resourceTypeId": "brand-voice",
      "sortOrder": 0,
      "injectionMode": "always",
      "settings": {
        "toneDescription": "Warm, authoritative, never jargon-heavy.",
        "targetAudience": "UK SMB decision-makers, age 30-55.",
        "styleGuidelines": "Active voice. Short sentences. Pound signs for money.",
        "avoidPatterns": "Exclamation marks. US spellings. Corporate buzzwords."
      }
    }
  ]
}
```

Resources are ordered by `sortOrder` ascending. `resources` is an empty array if the
Context has no resources configured.

#### Tree Inheritance (content_node_id mode)

The walk is top-down (self first, then parent, grandparent, …) and stops at the first
picker value that resolves to a live Context. A node whose picker is empty, malformed, or
references a deleted Context is skipped, and the walk continues to the next ancestor.
Multi-select pickers return the first resolvable Context and log an Info line.

This matches Umbraco.AI's own `ContentContextResolver` semantics — a child subtree
overrides the parent (e.g. `/services/divorce-law` can pick "Legal Voice" to override the
"Services Voice" picked on `/services`).

#### Error Shapes the Agent Sees

Rather than throw, the tool returns structured JSON errors so the LLM can reason about them:

| `error` | When | LLM should |
|--------|------|-----------|
| `not_found` | Alias lookup returned no Context | pick a different alias, ask the user, or proceed without context |
| `no_context_for_node` | Tree-walk found no picker with a live Context | pick a different node, ask the user, or proceed without context |
| `content_not_found` | `content_node_id` points to a non-existent or unpublished node | fix the node ID and retry |
| `invalid_argument` | Neither parameter supplied, empty alias, non-positive `content_node_id`, or unknown parameter | fix the tool call and retry |
| `context_service_failure` | Umbraco.AI's Context service threw (e.g. the known SQLite lock cascade) | try again in a moment, or proceed without context |

#### Prerequisites

- The Umbraco.AI Context must exist — create it via the Umbraco backoffice **Contexts**
  section (part of Umbraco.AI 1.x). AgentRun does not ship UI for Context management.
- For `content_node_id` mode: the doc type needs an `Uai.ContextPicker` property; pick a
  Context value on the content node (or any ancestor). No code or config changes
  required — the property editor ships with Umbraco.AI.

#### Security Notes

- `Settings` is user-curated reference material the site admin authored in the backoffice.
  Treat it as equivalent-trust to the agent's own system prompt — it is **not** tagged as
  untrusted data the way `fetch_url` and `web_search` results are.
- No secrets exposed. Umbraco.AI Contexts hold text reference material, not credentials.
- `OperationCanceledException` is the only exception that propagates to the step; all
  other failures return structured error JSON so the workflow keeps running.

#### Workflow Example

```yaml
name: On-Brand Page Draft
alias: on-brand-draft

config:
  brand_voice_alias: "corporate-brand-voice"

steps:
  - id: writer
    name: Writer
    agent: agents/writer.md
    tools: [get_ai_context, get_content, write_file]
    writes_to: [draft.md]
```

```markdown
<!-- agents/writer.md (excerpt) -->
Before drafting, call `get_ai_context(alias: "{brand_voice_alias}")` to load the
brand voice guidance. Read the returned `resources` — apply every `brand-voice` resource
(injectionMode: `always`) to your writing style. Call `get_content(id: …)` to load the
target page, then draft a rewrite to `draft.md` that keeps the requested changes while
matching the brand voice.
```

### `search_content`

Full-text keyword search against Umbraco's published content via the **Examine External
index** — the same search engine that powers Umbraco's own site-search feature. AgentRun
does not build a parallel search index; this tool is a thin adapter that lets an agent
query the existing index.

**Parameters**

| Name          | Type    | Required | Description                                                                      |
|---------------|---------|----------|----------------------------------------------------------------------------------|
| `query`       | string  | yes      | Keyword query. Lucene reserved characters are escaped automatically.             |
| `contentType` | string  | no       | Filter to this document-type alias (e.g. `"articlePage"`). Omit for all types.   |
| `parentId`    | integer | no       | Filter to descendants of this content node ID. Omit for whole-tree search.       |
| `count`       | integer | no       | Max results. Default `10`, silently clamped to `50`.                             |

**Return shape**

JSON array of hits, ordered by `relevanceScore` descending:

```json
[
  {
    "id": 1100,
    "name": "Pension Reform 2026",
    "contentType": "articlePage",
    "url": "/articles/pension-reform-2026",
    "level": 3,
    "relevanceScore": 4.72
  }
]
```

Empty index match returns `[]` (not an error). If the index is unavailable or the query
fails, a structured error object is returned instead:

```json
{ "error": "index_unavailable", "message": "Umbraco's External Examine index is not available. …" }
```

Error kinds: `invalid_argument`, `index_unavailable`, `search_failure`. The tool never
throws to the step except on cancellation.

**Complement to `list_content`, not a replacement.** Two different jobs:

- `list_content` → enumerate pages by filter (content type, parent). Use when you know *where* to look.
- `search_content` → find pages by keyword. Use when you know *what* you're looking for but not where.

A workflow can use both — e.g. a Scout agent uses `search_content` to discover candidates
by keyword, then calls `get_content` on the top hits to read each one in full.

**Known limitations — read before relying on results**

1. **No `snippet` / highlight field.** Examine 3.x does not ship a first-party highlighter
   API, and the Umbraco default indexer does not enable the term-vector indexing that
   Lucene's `FastVectorHighlighter` would need. The tool returns hit *metadata* (id, name,
   content type, url, level, score) — not match context. Agents that need match context
   should call `get_content(id: …)` on the top hits.

2. **Block List / Block Grid content is NOT indexed by default.** Umbraco's out-of-the-box
   External indexer reliably covers `nodeName`, plain-text properties, and rich-text
   (via Umbraco's `__Raw_*` transformation). It does **not** flatten Block List / Block
   Grid JSON into searchable text without custom wiring. If block content must be
   searchable on your site, wire a custom `IValueSetBuilder` or subscribe to the index's
   `TransformingIndexValues` event — see Umbraco's docs on custom indexing. The tool
   searches the index as it's configured; it cannot add content that isn't indexed.

3. **External index only.** The Internal index (backoffice, unpublished content, members)
   is deliberately not exposed — preventing accidental leaks of unpublished content to
   LLM agents. No configurable `indexName` parameter.

4. **No pagination.** `count` is capped at 50; there's no `skip`. If the top 50 hits don't
   contain what you need, narrow via `contentType`/`parentId` or refine the `query` —
   don't try to walk the whole index.

5. **Cancellation latency.** Examine's query path doesn't accept a `CancellationToken`
   in 3.x, so cancellation fires *between* query and mapping, not inside the Lucene call.
   In practice this is single-digit milliseconds.

6. **Stopwords are stripped.** Examine's default `StandardAnalyzer` filters common
   English stopwords — `a`, `the`, `is`, `of`, `and`, `or`, `but`, and many more — from
   both indexing and queries. A query of `"a"` alone returns `[]` even when nodes named
   "About" or "All Services" exist. Use meaningful keywords; if a single-word query
   returns nothing, try a less common synonym before assuming the content is missing.
   Sites that have customised their analyser (e.g. to preserve stopwords for exact-match
   search) will behave differently.

**Usage examples**

Content creator Scout — finding related articles by keyword:

```yaml
- name: scout
  tools: [search_content, get_content, write_file]
  writes_to: [scan-results.md]
```

```markdown
<!-- agents/scout.md (excerpt) -->
Call `search_content(query: "pension reform", contentType: "articlePage", count: 20)` to
find articles already mentioning the topic. For each top hit, call `get_content(id: …)`
to read the full page. Summarise the existing coverage in `scan-results.md`.
```

Content migration QA — stale-link audit:

```markdown
<!-- agents/qa.md (excerpt) -->
Call `search_content(query: "old-hostname.com", count: 50)` to find any content still
referencing the retired hostname. Report each offender with its URL in `stale-refs.md`.
```

Content audit Scanner — topic sampling rather than tree walk:

```markdown
<!-- agents/scanner.md (excerpt) -->
Rather than enumerating the whole tree, call
`search_content(query: "GDPR compliance", count: 50)` to focus the audit on pages that
actually mention the topic.
```

## Configuring Tool Tuning Values

Tool behaviour can be tuned at multiple levels. Values resolve through a four-tier chain,
picking the first non-null value:

```
step tool_overrides  (most specific)
       |
workflow tool_defaults
       |
site AgentRun:ToolDefaults in appsettings.json
       |
engine hard-coded defaults  (least specific)
```

After resolution, the value is capped by the site-level `AgentRun:ToolLimits` ceiling. Any
workflow that declares values above the ceiling is **rejected at load time** -- it won't appear
in the dashboard.

### Available Settings

| Setting | Engine Default | Description |
|---------|---------------|-------------|
| `fetch_url.max_response_bytes` | 1,048,576 (1 MB) | Max response body size for raw fetches |
| `fetch_url.timeout_seconds` | 15 | Per-request HTTP timeout |
| `read_file.max_response_bytes` | 262,144 (256 KB) | Max bytes returned from `read_file` |
| `tool_loop.user_message_timeout_seconds` | 300 (5 min) | How long to wait for user input in interactive mode |

### Example

```yaml
# Workflow-level defaults: apply to all steps
tool_defaults:
  fetch_url:
    max_response_bytes: 2097152   # 2 MB — large marketing pages
    timeout_seconds: 30           # slow CDNs

steps:
  - id: scanner
    name: Scanner
    agent: agents/scanner.md
    tools: [fetch_url, write_file]
    # No tool_overrides — inherits workflow defaults

  - id: analyser
    name: Analyser
    agent: agents/analyser.md
    tools: [read_file, write_file]
    tool_overrides:
      read_file:
        max_response_bytes: 524288  # 512 KB — scan results can be large
```

The site administrator can set ceilings in `appsettings.json` to prevent workflows from
requesting excessive resources:

```json
{
  "AgentRun": {
    "ToolLimits": {
      "FetchUrl": {
        "MaxResponseBytesCeiling": 5242880,
        "TimeoutSecondsCeiling": 120
      }
    }
  }
}
```

## IDE Validation Setup

The package includes a JSON Schema for `workflow.yaml` that provides autocomplete and inline
validation in editors that support the `yaml-language-server` directive (VS Code with the
YAML extension, JetBrains IDEs).

### Setting Up

1. Copy the schema file from the NuGet cache to your workflows folder:

```bash
# Replace <version> with the AgentRun.Umbraco version you installed (e.g. 1.2.0).
cp ~/.nuget/packages/agentrun.umbraco/<version>/contentFiles/any/any/App_Data/AgentRun.Umbraco/workflow-schema.json \
   App_Data/AgentRun.Umbraco/workflow-schema.json
```

2. Add this directive as the **first line** of your `workflow.yaml`:

```yaml
# yaml-language-server: $schema=../../workflow-schema.json
```

The path is relative: from `App_Data/AgentRun.Umbraco/workflows/{your-workflow}/workflow.yaml`
up two levels to `App_Data/AgentRun.Umbraco/workflow-schema.json`.

The shipped example workflows already include this directive. The schema uses JSON Schema
draft 2020-12 with `additionalProperties: false` at all levels -- unknown fields are flagged
as errors immediately.

## Walkthrough: Creating a 2-Step Workflow

Let's create a "URL Summary" workflow that fetches a page and produces a plain-language summary.

### Step 1: Create the Folder Structure

```
App_Data/AgentRun.Umbraco/workflows/
  url-summary/
    workflow.yaml
    agents/
      fetcher.md
      summariser.md
```

### Step 2: Write `workflow.yaml`

```yaml
# yaml-language-server: $schema=../../workflow-schema.json
name: URL Summary
description: Fetches a page and writes a plain-language summary
mode: interactive
default_profile: your-profile-alias
steps:
  - id: fetcher
    name: Page Fetcher
    agent: agents/fetcher.md
    tools:
      - fetch_url
      - write_file
    writes_to:
      - artifacts/page-content.md
    completion_check:
      files_exist:
        - artifacts/page-content.md

  - id: summariser
    name: Summariser
    agent: agents/summariser.md
    tools:
      - read_file
      - write_file
    reads_from:
      - artifacts/page-content.md
    writes_to:
      - artifacts/summary.md
    completion_check:
      files_exist:
        - artifacts/summary.md
```

If you have multiple Umbraco.AI profiles, set `default_profile` to your preferred alias.
Otherwise, auto-detection picks up your single configured profile automatically.

### Step 3: Write the Fetcher Prompt (`agents/fetcher.md`)

```markdown
# Page Fetcher

You fetch web pages and save the raw HTML for downstream processing.

## Your Task

Ask the user for a URL. When they provide one:

1. Call `fetch_url` with the URL (raw mode)
2. Call `read_file` to load the cached HTML from the path in the fetch result
3. Call `write_file` to save the content to `artifacts/page-content.md`

## Opening Line

Your first message must be exactly:

`Paste the URL of the page you'd like summarised.`

## Rules

- One URL only. If the user provides multiple, use the first one.
- If `fetch_url` returns an error status, tell the user and ask for a different URL.
- Do not produce text-only turns between tool calls -- this will pause the workflow.
```

### Step 4: Write the Summariser Prompt (`agents/summariser.md`)

```markdown
# Summariser

You read page content and produce a concise, plain-language summary.

## Your Task

1. Call `read_file` to load `artifacts/page-content.md`
2. Write a summary covering: what the page is about, who it's for, and key
   points or calls to action
3. Call `write_file` to save the summary to `artifacts/summary.md`

## Output Format

Write the summary as a short markdown document (under 300 words). Use plain
language -- no jargon, no bullet-point walls.

## Rules

- Base the summary only on what's in the page content. Do not invent facts.
- Do not produce text-only turns between tool calls.
```

### Step 5: Test It

1. Restart your Umbraco site (or wait for the manifest cache to refresh)
2. Open **Agent Workflows** in the backoffice
3. Your "URL Summary" workflow should appear in the list
4. Click it, click **Start**, and paste a URL when prompted

If the workflow doesn't appear, check the Umbraco logs -- the engine logs validation errors
with the workflow alias and the specific issue.

## Learning from the Examples

The shipped example workflows are practical references:

- **Content Quality Audit** (`content-quality-audit/`) -- 3 steps (scanner, analyser, reporter)
  with artifact handoff, structured extraction, output templates, and comprehensive failure
  handling. Good example of a multi-step pipeline.

- **Accessibility Quick-Scan** (`accessibility-quick-scan/`) -- 2 steps (scanner, reporter)
  with the same interactive pattern but a different domain. Good example of a minimal but
  production-quality workflow.

- **Umbraco Content Audit** (`umbraco-content-audit/`) -- 3 steps (scanner, analyser,
  reporter) using in-process content tools (`list_content_types`, `list_content`,
  `get_content`, `search_content`) plus the Umbraco.AI Context reader (`get_ai_context`)
  -- no external URLs, reads directly from Umbraco's published content cache. Scores a
  6-pillar content audit by default; enable the optional 7th **Brand** pillar by setting
  `config.brand_voice_context:` to an Umbraco.AI Context alias (see
  [Conditional Pillars via Config](#conditional-pillars-via-config)). Good example of the
  content-tool pattern, the [Agent Sanctum Pattern](#agent-sanctum-pattern) (agent identity
  split across `sidecars/{step}/PERSONA.md` / `CREED.md` / `CAPABILITIES.md`), and the
  prompt-variable + `config:` block pattern for authoring optional features.

Read these alongside this guide. The agent prompts in `agents/` show what works in practice --
especially the invariant patterns for preventing interactive-mode stalls.
