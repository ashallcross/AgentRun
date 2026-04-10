# AgentRun.Umbraco

AI-powered workflow engine for Umbraco CMS -- define multi-step agent workflows in YAML.

<!-- Badges (post-beta): NuGet version, Umbraco compatibility -->

> AgentRun ships with two example workflows: Content Quality Audit and Accessibility Quick-Scan.
> After installing the package and configuring an Umbraco.AI profile, open the Agent Workflows
> section, click a workflow, and click Start. The agent will ask you for one or more URLs to
> audit -- paste URLs from your own site (or any public site you'd like a second opinion on)
> and watch the workflow run.

## Quick Start

### Prerequisites

- Umbraco 17+
- .NET 10
- At least one Umbraco.AI provider package ([Umbraco.AI.Anthropic](https://www.nuget.org/packages/Umbraco.AI.Anthropic) or [Umbraco.AI.OpenAI](https://www.nuget.org/packages/Umbraco.AI.OpenAI)) with a configured profile

### 1. Install the package

```bash
dotnet add package AgentRun.Umbraco --version 1.0.0-beta.1
```

### 2. Copy the example workflows

NuGet links content files from the package cache rather than copying them into your project.
The workflow engine discovers workflows on disk at `App_Data/AgentRun.Umbraco/workflows/`,
so you need to copy them manually on first install:

```bash
# Find your NuGet cache path (typically ~/.nuget/packages/)
cp -r ~/.nuget/packages/agentrun.umbraco/1.0.0-beta.1/contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/ \
      App_Data/AgentRun.Umbraco/workflows/
```

On Windows:

```powershell
Copy-Item -Recurse "$env:USERPROFILE\.nuget\packages\agentrun.umbraco\1.0.0-beta.1\contentFiles\any\any\App_Data\AgentRun.Umbraco\workflows\" `
    -Destination "App_Data\AgentRun.Umbraco\workflows\"
```

This gives you two ready-to-run workflows:

| Workflow | Steps | What it does |
|----------|-------|-------------|
| **Content Quality Audit** | Scanner, Analyser, Reporter | Fetches pages, scores content quality, generates an audit report |
| **Accessibility Quick-Scan** | Scanner, Reporter | Fetches pages, identifies WCAG 2.1 AA issues, produces a prioritised fix list |

### 3. Configure an Umbraco.AI profile

In the Umbraco backoffice, go to **Settings > AI** and create a profile for your chosen provider.
Note the profile alias -- each workflow's `default_profile` field in `workflow.yaml` must match it.
The shipped examples use `anthropic-sonnet-4-6`; update this to your profile alias.

### 4. Run and open Agent Workflows

Start your site and navigate to the **Agent Workflows** section in the backoffice. Both example
workflows should appear. Click one, click **Start**, and paste one or more URLs when prompted.

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

## Known Limitations (Beta)

- Single concurrent instance per workflow
- Disk-based persistence only (YAML + JSONL) -- no database
- No Umbraco Marketplace listing yet (planned for 1.0)
- Interactive mode is the primary UX; autonomous mode is functional but secondary

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
