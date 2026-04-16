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
