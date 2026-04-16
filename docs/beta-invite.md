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
dotnet add package AgentRun.Umbraco --version 1.0.0-beta.2
```

All three packages auto-register — no `Program.cs` changes needed.

## Run the site

```bash
dotnet run
```

On first startup, AgentRun automatically:

- Copies the three example workflows to `App_Data/AgentRun.Umbraco/workflows/`
- Grants the AgentRun section permission to the Administrators user group

Umbraco.AI does the same for its own AI section. No manual file copying or permission toggling needed.

**Log out and back in** once the site is running — the auth token needs to refresh to pick up the new section permissions.

## Configure an AI provider

1. Go to the **AI** section in the backoffice
2. Click **Connections > Create Connection**
3. Select your provider (e.g. Anthropic), give it a name and alias, and enter your API key
4. Click **Profiles > Create > Chat**
5. Select the connection, choose a model, and set an alias

That's it — AgentRun auto-detects your profile. No YAML editing needed for first run. If you have multiple profiles, you can set `default_profile` in each workflow's `workflow.yaml` for deterministic selection.

## Try it

Navigate to the **Agent Workflows** section in the backoffice. I'd suggest starting with the **Umbraco Content Audit** — it reads your published content directly, so you'll see results straight away.

## What's new in beta.2

- **Auto-setup on install** — no more manual file copying or permission grants
- **Profile auto-detection** — no YAML editing, just configure Umbraco.AI as normal
- **Clearer provider errors** — if your API key is invalid or out of credit, you'll see a proper error instead of a silent blank chat
- **Larger site audits** — context management prevents stalls on sites with lots of content

## Known limitations

- Cancel is cosmetic — it updates the label but doesn't stop the in-flight LLM call (fix planned)
- If you close the browser tab during a run, it gets marked as Failed even if it partially completed (fix planned)
- Don't navigate away from an instance while it's running — the chat input breaks if you return after a step completes (fix planned)
- Don't run two instances of the same workflow simultaneously — no concurrency locking yet

## Feedback

I'd really appreciate any feedback — what worked, what didn't, what was confusing. Bug reports, enhancement ideas, questions, all welcome.

This is a private beta — please don't share it publicly yet.
