# AgentRun.Umbraco

AI-powered workflow engine for Umbraco CMS -- define multi-step agent workflows in YAML.

<!-- Badges: NuGet version, Umbraco compatibility -->

> AgentRun ships with three example workflows, including an **Umbraco Content Audit** that
> analyses the content in your Umbraco instance directly from the backoffice -- no external URLs
> needed. After installing the package and configuring an Umbraco.AI profile, open Agent
> Workflows, click the Content Audit, and click Start. The agent reads your published content
> tree and produces a prioritised audit report.
>
> Also included: **Content Quality Audit** and **Accessibility Quick-Scan** -- paste one or
> more public URLs and the agent fetches, scores, and reports on content quality or WCAG 2.1 AA
> accessibility issues.

## What's Inside

AgentRun is an engine, not just a set of workflows. The shipped examples are a starting
point; building your own is the intended path. Headline capabilities:

- **Extensible tool surface.** 10 shipped tools covering HTTP fetch, file I/O, published
  Umbraco content (`list_content`, `get_content`, `list_content_types`, `search_content`
  via Examine), an Umbraco.AI Context reader (`get_ai_context`), and web search
  (`web_search`, Brave + Tavily). Custom tools plug in via a simple .NET interface --
  register once through Umbraco's composer system and every workflow can declare them.
- **Prompt variables.** Agent prompts can include `{today}`, `{instance_id}`, `{step_index}`,
  `{previous_artifacts}`, and workflow-declared `config:` tokens -- substituted
  deterministically at prompt-assembly time, so dates, IDs, and config values are never
  hallucinated.
- **Provider-aware prompt caching.** Cache-friendly prompts get ~90% discounts on Anthropic
  and automatic caching on OpenAI / Azure OpenAI, without YAML changes -- just a stable-
  first authoring discipline documented in the guide.
- **Per-step model profiles.** Route each step to the Umbraco.AI profile that fits --
  Sonnet-class for high-volume scanning, Opus-class for the visible report -- and keep the
  cost / quality balance tunable per workflow.
- **Agent Sanctum pattern.** For agents whose identity matters (archivist voice, evidence-
  first analyst), split PERSONA / CREED / CAPABILITIES into ship-time sidecars. The
  engine assembles them into a cache-stable preamble above the per-turn runtime context.
- **Umbraco Content Audit v2.** The shipped audit now extracts real body prose (Block List,
  Block Grid, Rich Text) via a 3-tier property priority chain and scores an optional
  Brand pillar when an Umbraco.AI Context is configured -- six-pillar audit by default,
  seven with a brand voice attached.

See the [Workflow Authoring Guide](docs/workflow-authoring-guide.md) for the full tool
reference and walkthroughs.

## Quick Start

### Prerequisites

- Umbraco 17+
- .NET 10
- At least one Umbraco.AI provider package ([Umbraco.AI.Anthropic](https://www.nuget.org/packages/Umbraco.AI.Anthropic) or [Umbraco.AI.OpenAI](https://www.nuget.org/packages/Umbraco.AI.OpenAI)) with a configured profile

### 1. Set up Umbraco first

If you're adding AgentRun to a new site, run the Umbraco install wizard first (`dotnet run`,
complete setup in the browser, then stop the site). Umbraco.AI runs database migrations on
startup and needs an existing database.

### 2. Install the packages

```bash
dotnet add package Umbraco.AI
dotnet add package Umbraco.AI.Anthropic
dotnet add package AgentRun.Umbraco --version 1.2.0
```

All three packages auto-register via Umbraco's composer system -- no `Program.cs` changes needed.
Installing `Umbraco.AI` as a direct dependency ensures it survives if you later remove AgentRun.

### 3. Run the site

```bash
dotnet run
```

On first startup, AgentRun automatically:

- Copies three example workflows to `App_Data/AgentRun.Umbraco/workflows/`
- Grants the AgentRun section permission to the Administrators user group

No manual file copying or permission setup needed. Log out and back in to pick up the new
section permission.

The three example workflows:

| Workflow | Steps | What it does |
|----------|-------|-------------|
| **Umbraco Content Audit** | Scanner, Analyser, Reporter | Reads your Umbraco content tree, scores completeness and structure, generates a prioritised audit report |
| **Content Quality Audit** | Scanner, Analyser, Reporter | Fetches pages, scores content quality, generates an audit report |
| **Accessibility Quick-Scan** | Scanner, Reporter | Fetches pages, identifies WCAG 2.1 AA issues, produces a prioritised fix list |

### 4. Configure Umbraco.AI

In the backoffice, go to the **AI** section:

1. **Create a Connection** -- select your provider (e.g., Anthropic), enter your API key
2. **Create a Chat Profile** (Profiles > Create > Chat) -- select the connection, choose a model, set an alias

AgentRun auto-detects your Umbraco.AI profile -- no YAML editing needed for first run.
If you have multiple profiles, you can set `default_profile` in each workflow's `workflow.yaml`
for deterministic selection.

### 5. Open Agent Workflows

Navigate to the **Agent Workflows** section in the backoffice. All three example workflows
should appear. Start with the **Umbraco Content Audit** -- it reads your site's own content,
so there's nothing to configure beyond the profile. For the URL-based workflows, click
**Start** and paste one or more URLs when prompted.

## Trying the Content Audit's Brand Pillar (Optional)

The Umbraco Content Audit runs as a 6-pillar audit by default. To enable the optional
7th **Brand** pillar -- which scores content alignment against your site's brand voice:

1. In the backoffice **AI > Contexts** section, create a Context with at least one
   `brand-voice` resource (populate `toneDescription`, `targetAudience`, and
   `styleGuidelines`)
2. Open `App_Data/AgentRun.Umbraco/workflows/umbraco-content-audit/workflow.yaml`
3. Set `config.brand_voice_context:` to the alias of the Context you created (leaving it
   as `""` keeps the 6-pillar audit)
4. Restart the site so the workflow manifest refreshes
5. Run the audit -- the scanner validates the alias before starting, calls `get_ai_context`
   to load the brand voice, and scores a 7th Brand pillar against the prose extracted
   from each node's body-copy property

If the alias doesn't resolve, the scanner halts with a clear configuration error rather
than silently falling back. See the
[Workflow Authoring Guide](docs/workflow-authoring-guide.md#conditional-pillars-via-config)
for the full pattern, including how to use it in your own workflows.

## Creating Your Own Workflows

Workflows are YAML definitions paired with agent prompt markdown files. See the
[Workflow Authoring Guide](docs/workflow-authoring-guide.md) for the full field reference,
tool documentation, and a step-by-step walkthrough of creating a custom workflow.

The shipped example workflows in `App_Data/AgentRun.Umbraco/workflows/` are also useful
references -- read them alongside the guide.

## Configuration

AgentRun works out of the box with sensible defaults. Most users only need an Umbraco.AI
profile configured. For advanced use cases, add an `AgentRun` section to `appsettings.json`:

```json
{
  "AgentRun": {
    "DefaultProfile": "your-profile-alias",
    "DataRootPath": "App_Data/AgentRun.Umbraco/instances/",
    "WorkflowPath": "App_Data/AgentRun.Umbraco/workflows/",
    "ToolDefaults": {
      "FetchUrl": {
        "MaxResponseBytes": 1048576,
        "TimeoutSeconds": 15
      },
      "ReadFile": {
        "MaxResponseBytes": 262144
      },
      "ToolLoop": {
        "UserMessageTimeoutSeconds": 300
      }
    },
    "ToolLimits": {
      "FetchUrl": {
        "MaxResponseBytesCeiling": 5242880,
        "TimeoutSecondsCeiling": 120
      }
    }
  }
}
```

All paths are relative to your site's content root. `DefaultProfile` sets the fallback LLM
profile; individual workflows and steps can override it.

`ToolDefaults` sets site-wide default tuning values. `ToolLimits` sets hard ceilings that
workflows cannot exceed -- any workflow declaring values above the ceiling is rejected at load
time. See the [Workflow Authoring Guide](docs/workflow-authoring-guide.md#configuring-tool-tuning-values)
for the full resolution chain.

## Known Limitations

- Single concurrent instance per workflow
- Disk-based persistence only (YAML + JSONL) -- no database
- Interactive mode is the primary UX; autonomous mode is functional but secondary
- Anthropic `cache_control` marker translation through Umbraco.AI is a planned follow-up;
  caching still works automatically on OpenAI / Azure OpenAI once prompts cross the provider
  size threshold
- Provider compatibility: Anthropic is the most thoroughly exercised provider to date.
  OpenAI and Azure OpenAI are supported through Umbraco.AI and work in practice; please
  report any provider-specific error scenarios you hit

## Uninstalling

AgentRun creates no database tables, writes no configuration entries, and stores all runtime
data under `App_Data/AgentRun.Umbraco/`. To remove it cleanly:

```bash
dotnet remove package AgentRun.Umbraco
```

Then optionally delete the data folder:

```bash
rm -rf App_Data/AgentRun.Umbraco/
```

No other cleanup is needed.

## Licence

AgentRun.Umbraco is licensed under the [Apache License 2.0](./LICENSE)
(SPDX: `Apache-2.0`).

Copyright 2026 Adam Shallcross.

Third-party components included or referenced by this package are acknowledged
in [`NOTICE`](./NOTICE), all used under permissive licences compatible with
Apache 2.0.
