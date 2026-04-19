# Workflow Authoring Guide

This guide covers everything you need to create custom AgentRun workflows for Umbraco.

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
| Anthropic (Claude) | **Today:** ignores the hint — the current Umbraco.AI.Anthropic + Microsoft.Extensions.AI chain does not translate the neutral hint into Anthropic's native `cache_control` marker. Tracked as a planned follow-up (see AgentRun's `deferred-work.md`). **Once that follow-up ships:** the System message will carry `cache_control: { type: "ephemeral" }` and subsequent turns within a step will read from cache. |

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

Creates parent directories as needed. Writes are atomic (temp file + rename) so partially
written files never appear on disk.

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

Gets the full details and property values of a single published content node by ID.

**Parameters:**

| Name | Required | Description |
|------|----------|-------------|
| `id` | Yes | The ID of the published content node to retrieve |

Returns a JSON object: `id`, `name`, `contentType`, `url`, `level`, `createDate`, `updateDate`,
`creatorName`, `templateAlias`, `properties` (object mapping alias to extracted text value).

Property extraction: Rich Text is stripped to plain text, Text String/Textarea as-is, Content
Picker shows name + URL, Media Picker shows name + URL + alt text, Block List/Grid shows
simplified block content, TrueFalse shows `"true"`/`"false"`, fallback is JSON-serialised
source value.

**Access:** same in-process published content cache. Large responses are truncated by removing
properties from the end.

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
cp ~/.nuget/packages/agentrun.umbraco/1.0.0-beta.1/contentFiles/any/any/App_Data/AgentRun.Umbraco/workflow-schema.json \
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

- **Umbraco Content Audit** (`umbraco-content-audit/`) -- 3 steps (scanner, analyser, reporter)
  using in-process content tools (`list_content_types`, `list_content`, `get_content`) instead
  of `fetch_url`. No external URLs -- reads directly from Umbraco's published content cache.
  Good example of the content tool pattern for workflows that operate on the site's own data.

Read these alongside this guide. The agent prompts in `agents/` show what works in practice --
especially the invariant patterns for preventing interactive-mode stalls.
