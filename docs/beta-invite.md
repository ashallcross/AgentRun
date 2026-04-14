# AgentRun.Umbraco — Private Beta

I've been building an AI workflow engine for Umbraco and I'd like you to try it out before it goes public. It lets you define multi-step agent workflows in YAML that run inside the Umbraco backoffice.

It ships with three example workflows — the headline one is a **Content Audit** that reads the content in your Umbraco instance and produces a prioritised audit report. No external URLs needed, it works against your own content tree. There are also two URL-based workflows: a Content Quality Audit and an Accessibility Quick-Scan.

## Requirements

- Umbraco 17+
- .NET 10
- An Anthropic or OpenAI API key

## Install

If this is a new site, run the Umbraco install wizard first (`dotnet run`, complete setup in the browser, then stop the site). Umbraco.AI needs an existing database before it can start.

```bash
dotnet add package Umbraco.AI
dotnet add package Umbraco.AI.Anthropic
dotnet add package AgentRun.Umbraco --version 1.0.0-beta.1
```

All three packages auto-register — no `Program.cs` changes needed.

## Copy the example workflows

NuGet links content files from its cache rather than copying them into your project. You need to copy them manually:

```bash
mkdir -p App_Data/AgentRun.Umbraco/workflows
cp -r ~/.nuget/packages/agentrun.umbraco/1.0.0-beta.1/contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/ App_Data/AgentRun.Umbraco/workflows/
```

On Windows, replace `~/.nuget` with `%USERPROFILE%\.nuget` and use `xcopy` or `robocopy`:

```powershell
mkdir App_Data\AgentRun.Umbraco\workflows
xcopy /E /I "%USERPROFILE%\.nuget\packages\agentrun.umbraco\1.0.0-beta.1\contentFiles\any\any\App_Data\AgentRun.Umbraco\workflows" "App_Data\AgentRun.Umbraco\workflows"
```

## Set up permissions

1. Run the site: `dotnet run`
2. Go to **Users > User Groups > Administrators**
3. Grant both the **AI** and **AgentRun** section permissions
4. Save, then **log out and back in** (the auth token needs refreshing)

## Configure an AI provider

1. Go to the **AI** section in the backoffice
2. Click **Connections > Create Connection**
3. Select your provider (e.g. Anthropic), give it a name and alias, and enter your API key
4. Click **Profiles > Create Profile**
5. Select the connection, choose a model, and set an alias
6. Note the profile alias — you'll need it next

## Point the workflows at your profile

Edit the `default_profile` field in each workflow file to match your profile alias:

- `App_Data/AgentRun.Umbraco/workflows/umbraco-content-audit/workflow.yaml`
- `App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml`
- `App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/workflow.yaml`

Restart the site after editing.

## Try it

Navigate to the **Agent Workflows** section in the backoffice. I'd suggest starting with the **Umbraco Content Audit** — it reads your published content directly, so you'll see results straight away.

## Known limitations

- Content audit on large sites (100+ nodes) may stall — use content type filters to audit in sections
- Cancel is cosmetic — it updates the label but doesn't stop the in-flight LLM call
- If you close the browser tab during a run, it gets marked as Failed even if it partially completed
- Don't navigate away from an instance while it's running — the chat input breaks if you return after a step completes
- Don't run two instances of the same workflow simultaneously

## Feedback

I'd really appreciate any feedback — what worked, what didn't, what was confusing. Bug reports, enhancement ideas, questions, all welcome.

This is a private beta — please don't share it publicly yet.
