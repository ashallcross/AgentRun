# Upgrade Report: Umbraco 17.2.2 → 17.3.2 + Umbraco.AI 1.7.0 → 1.8.0

**Date:** 2026-04-10
**Source:** Story 9.3 beta test plan — fresh install smoke test
**Author:** Paige (tech writer, documenting findings from live testing)

## Problem

AgentRun.Umbraco 1.0.0-beta.1 was built against Umbraco 17.2.2 and Umbraco.AI 1.7.0.
A fresh `dotnet new umbraco` now scaffolds Umbraco **17.3.2**, which depends on
`Umbraco.AI.Core 1.8.0`. This creates two issues for consumers:

### 1. Assembly load failure

The AgentRun DLL references `Umbraco.AI.Core` types. When the consumer's site resolves
Umbraco.AI.Core 1.8.0 (via Umbraco 17.3.2), the runtime throws:

```
System.IO.FileNotFoundException: Could not load file or assembly
'Umbraco.AI.Core, Version=1.8.0.0'
```

This crashes the site on startup — not a degraded experience, a hard crash.

### 2. Provider discovery failure (silent)

Even if the assembly version mismatch is worked around by pinning `Umbraco.Cms` to 17.2.2
in `Directory.Packages.props`, `Umbraco.AI.Anthropic 1.2.2` fails to register its provider
silently when `Umbraco.AI.Core 1.8.0` is present. The backoffice shows "No providers
available" with no error in logs.

### 3. Central Package Management complicates pinning

`dotnet new umbraco` generates a `Directory.Packages.props` with
`ManagePackageVersionsCentrally=true`. Versions in this file override the csproj. A consumer
running `dotnet add package Umbraco.Cms --version 17.2.2` edits the csproj, but
`Directory.Packages.props` still says 17.3.2 and wins. This makes the pinning workaround
non-obvious.

## What Needs to Change

### Main project (AgentRun.Umbraco.csproj)

| Package | Current | Target |
|---------|---------|--------|
| `Umbraco.Cms.Web.Website` | 17.2.2 | 17.3.2 |
| `Umbraco.Cms.Web.Common` | 17.2.2 | 17.3.2 |
| `Umbraco.Cms.Api.Common` | 17.2.2 | 17.3.2 |
| `Umbraco.Cms.Api.Management` | 17.2.2 | 17.3.2 |
| `Umbraco.AI` | 1.7.0 | 1.8.0 (or whatever 17.3.2 expects) |

### TestSite project (AgentRun.Umbraco.TestSite.csproj)

| Package | Current | Target |
|---------|---------|--------|
| `Umbraco.Cms` | 17.2.2 | 17.3.2 |
| `Umbraco.Cms.DevelopmentMode.Backoffice` | 17.2.2 | 17.3.2 |
| `Umbraco.AI` | 1.7.0 | 1.8.0 |
| `Umbraco.AI.Agent` | 1.6.0 | Check compatible version |
| `Umbraco.AI.Anthropic` | 1.2.2 | 1.3.0 (or latest for 1.8.0 core) |
| `Umbraco.AI.OpenAI` | 1.1.3 | Check compatible version |

### Tests project (AgentRun.Umbraco.Tests.csproj)

Check for any transitive Umbraco dependencies that need version alignment.

### Documentation

- `_bmad-output/project-context.md` — update Technology Stack section with new versions
- `README.md` — remove version pin from `Umbraco.AI.Anthropic` install command (consumers
  can use latest)
- `docs/beta-test-plan.md` — remove version pin, simplify install steps

### Post-upgrade verification

1. `dotnet test AgentRun.Umbraco.slnx` — all 465 backend tests pass
2. `cd Client && npm test` — all 162 frontend tests pass
3. `dotnet pack` — produces valid nupkg
4. Fresh install smoke test (beta-test-plan.md) — passes without version pinning

## Risk Assessment

**Low risk.** This is a minor version bump within the same major release (17.x). Breaking
API changes are unlikely. The main risk is:

- Umbraco.AI 1.8.0 may have changed interfaces that AgentRun uses (e.g., `IAIChatService`,
  `IChatClient`). Check for compilation errors after bumping.
- The `Umbraco.AI.Agent` package (used in TestSite only, not in AgentRun itself) may have
  a new version requirement.

## Additional Finding: Umbraco.AI Startup Order Bug

During testing, we discovered that `Umbraco.AI` crashes on startup if installed before the
Umbraco install wizard has run. Its `RunAIMigrationNotificationHandler` tries to run EF
migrations on `UmbracoApplicationStartedNotification` without checking if a database exists.

This is an Umbraco.AI bug, not an AgentRun bug. The README and beta test plan now document
the correct install order: run the Umbraco wizard first, then install packages. This applies
regardless of whether we upgrade.
