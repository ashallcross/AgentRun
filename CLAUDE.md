# Project Instructions for Claude Code

AgentRun.Umbraco — AI-powered workflow engine for Umbraco CMS.

## Two-Repo Development Split (Non-Negotiable)

This project is developed across **two repositories**:

- **Public repo** at `~/Documents/AgentRun/` (or the current working directory if named differently) — contains package source, tests, docs, and what ships to NuGet + the Umbraco Marketplace
- **Private repo** at `~/Documents/AgentRun-planning/` — contains all BMAD planning artefacts, story specs, retrospectives, sprint status, deferred work, Claude skills, and agent definitions

The folders `_bmad-output/`, `_bmad/`, `.agents/`, and `.claude/` inside the public repo are **symlinks** pointing at the private repo. The public repo's `.gitignore` excludes those paths.

### Commit routing (apply automatically)

When the user asks to commit or push, route changes to the correct repo:

| What changed | Which repo to commit in |
|---|---|
| Files under `AgentRun.Umbraco/`, `AgentRun.Umbraco.Tests/`, `AgentRun.Umbraco.TestSite/` | Public |
| `docs/`, README.md, LICENSE, NOTICE, csproj files, umbraco-marketplace.json, .gitignore | Public |
| Anything under `_bmad-output/`, `_bmad/`, `.agents/`, `.claude/` (reached via symlink) | **Private** — cd to `~/Documents/AgentRun-planning/` to commit |

A typical story completion produces **two separate commits** across the two repos. This is correct and expected. Never combine them.

### Red flags

- `git status` in the public repo showing any `_bmad-output/`, `_bmad/`, `.agents/`, or `.claude/` content → symlinks or `.gitignore` are broken; stop and investigate before staging
- An agent proposing to copy files between the repos "for simplicity" → violates the split; follow the split
- Committing to the public repo and finding planning artefacts in the changelist → immediately unstage, don't force through

Full rules and rationale live in `~/.claude/projects/-Users-adamshallcross-Documents-Umbraco-AI/memory/feedback_two_repo_split.md` (auto-loaded every session). Further technical context in `_bmad-output/project-context.md`.

## Other Rules

- **Tests**: `dotnet test AgentRun.Umbraco.slnx` — never bare `dotnet test` (multi-project repo; bare call fails with MSB1011).
- **Frontend tests**: `npm test` from `AgentRun.Umbraco/Client/`.
- **Frontend build before commit**: `npm run build` from `AgentRun.Umbraco/Client/` so `wwwroot/` is current.
- **Full technical rules**: see `_bmad-output/project-context.md` for the comprehensive AI-agent rule set (technology stack, language conventions, architecture boundaries, security invariants, lessons learned).
