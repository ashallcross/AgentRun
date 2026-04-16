# AgentRun.Umbraco

AI-powered workflow engine for Umbraco CMS -- define multi-step agent workflows in YAML.

<!-- Badges (post-beta): NuGet version, Umbraco compatibility -->

> AgentRun ships with three example workflows, including an **Umbraco Content Audit** that
> analyses the content in your Umbraco instance directly from the backoffice -- no external URLs
> needed. After installing the package and configuring an Umbraco.AI profile, open Agent
> Workflows, click the Content Audit, and click Start. The agent reads your published content
> tree and produces a prioritised audit report.
>
> Also included: **Content Quality Audit** and **Accessibility Quick-Scan** -- paste one or
> more public URLs and the agent fetches, scores, and reports on content quality or WCAG 2.1 AA
> accessibility issues.

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
dotnet add package AgentRun.Umbraco --version 1.0.0-rc.2
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

## Known Limitations (1.0 Release Candidate)

- Single concurrent instance per workflow
- Disk-based persistence only (YAML + JSONL) -- no database
- Interactive mode is the primary UX; autonomous mode is functional but secondary
- First-run testing on non-Anthropic providers (OpenAI, Azure OpenAI) is ongoing; please report
  provider-specific error scenarios if you hit them

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
