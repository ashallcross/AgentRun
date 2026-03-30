---
title: 'Umbraco V17 Local Install'
slug: 'umbraco-v17-local-install'
created: '2026-02-05'
status: 'completed'
stepsCompleted: [1, 2, 3, 4]
tech_stack: ['.NET 10', 'Umbraco 17.1.0', 'SQLite']
files_to_modify: ['UmbracoAI.csproj', 'appsettings.Development.json', 'Program.cs']
code_patterns: ['dotnet new umbraco template']
test_patterns: []
reviewed: true
---

# Tech-Spec: Umbraco V17 Local Install

**Created:** 2026-02-05

## Overview

### Problem Statement

Need a working Umbraco V17 backoffice environment to test the Umbraco.AI package. This is a POC/demo setup - no production requirements.

### Solution

Install Umbraco V17 using the dotnet new template with SQLite database and the starter kit. Minimal config, just get it running.

### Scope

**In Scope:**
- Umbraco V17 installation via dotnet CLI
- SQLite database (lightweight, no external dependencies)
- Starter kit for basic content structure
- Get the backoffice running and accessible

**Out of Scope:**
- SQL Server or other database engines
- Production configuration (HTTPS, security hardening, etc.)
- Umbraco.AI package installation (phase 2 - requires separate investigation)
- Custom themes or templates
- Deployment concerns

## Context for Development

### Codebase Patterns

**Confirmed Clean Slate** - project folder contains only BMAD scaffolding (`_bmad/`, `_bmad-output/`, `.claude/`). Standard Umbraco V17 project structure will be created via template.

### Files to Reference

| File | Purpose |
| ---- | ------- |
| N/A | Fresh install - no existing files to reference |

### Technical Decisions

- **Database:** SQLite via `--development-database-type SQLite`
- **Location:** Project created as `UmbracoAI/` subfolder in `/Users/adamshallcross/Documents/Umbraco AI/`
- **Starter Kit:** `Umbraco.TheStarterKit` via `--starter-kit` flag (confirmed available in V17 template)
- **Template Version:** Umbraco.Templates 17.1.0
- **Install Mode:** Unattended install using CLI flags (no manual wizard)

### Investigation Findings

**Umbraco V17 Requirements:**
- **.NET 10 LTS required** (Umbraco 17 is built on .NET 10)
- Source: [Umbraco 17 LTS release](https://umbraco.com/blog/umbraco-17-lts-release/)

**Umbraco V17 Template Options (verified):**
```
dotnet new umbraco
  --development-database-type SQLite    # Embedded SQLite - no external DB needed
  --starter-kit Umbraco.TheStarterKit   # Basic content structure (confirmed in V17)
  --friendly-name <name>                # Admin display name (unattended)
  --email <email>                       # Admin email (unattended)
  --password <password>                 # Admin password (unattended)
```

**Current Environment Status:**
- .NET SDK installed: 9.0.203 (INSUFFICIENT - need .NET 10)
- Umbraco Templates: 17.1.0 (installed)

## Implementation Plan

### Tasks

- [x] **Task 0: Install .NET 10 SDK (PREREQUISITE)**
  - Command: `brew install dotnet@10` OR download from https://dotnet.microsoft.com/download/dotnet/10.0
  - Action: Install .NET 10 SDK required for Umbraco V17
  - Verify: `dotnet --list-sdks` shows 10.x version
  - Notes: Umbraco 17 requires .NET 10 LTS. Current .NET 9 will NOT work.
  - **Result:** Installed 10.0.102 via Homebrew

- [x] **Task 1: Create Umbraco project from template (unattended)**
  - Command:
    ```
    dotnet new umbraco -n "UmbracoAI" \
      --development-database-type SQLite \
      --starter-kit Umbraco.TheStarterKit \
      --friendly-name "Admin" \
      --email "admin@localhost.local" \
      --password "Admin1234!" \
      --force
    ```
  - Location: `/Users/adamshallcross/Documents/Umbraco AI/`
  - Output: Creates `UmbracoAI/` subfolder with complete project
  - Action: Generate Umbraco V17 project with SQLite, starter kit, and admin user pre-configured
  - Notes: Unattended install skips the wizard. **Password updated to 10 chars (Umbraco V17 requirement).**
  - **Result:** Project created successfully

- [x] **Task 2: Build the project**
  - Command: `dotnet build UmbracoAI/UmbracoAI.csproj`
  - Action: Restore packages and compile the project
  - Notes: First build will restore NuGet packages automatically
  - Troubleshooting: If build fails, check `dotnet --version` shows 10.x
  - **Result:** Build succeeded, 0 errors, 2 warnings (package version resolution)

- [x] **Task 3: Run the site**
  - Command: `dotnet run --project UmbracoAI/UmbracoAI.csproj`
  - Action: Start the Umbraco site on localhost
  - Expected output: Site available at `https://localhost:44391` or similar (check console output)
  - Troubleshooting: If port conflict, check output for actual URL or use `--urls "http://localhost:5000"`
  - **Result:** Running at https://localhost:44317

- [x] **Task 4: Verify backoffice access**
  - Action: Navigate to `{site-url}/umbraco` in browser
  - Credentials: `admin@localhost.local` / `Admin1234!`
  - Verify: Dashboard loads with starter kit content visible in Content section
  - Verify: Can navigate Content, Media, Settings sections
  - **Result:** Backoffice accessible (HTTP 200), starter kit installed (30 content items)

### Acceptance Criteria

- [x] **AC 1:** Given .NET 10 SDK is installed, when `dotnet --list-sdks` is run, then version 10.x is listed

- [x] **AC 2:** Given the template command is run, when it completes, then `UmbracoAI/UmbracoAI.csproj` exists with `<TargetFramework>net10.0</TargetFramework>`

- [x] **AC 3:** Given the project exists, when `dotnet build` is run, then build succeeds with 0 errors

- [x] **AC 4:** Given the project is built, when `dotnet run` is executed, then console shows site URL and "Application started"

- [x] **AC 5:** Given the site is running, when navigating to `{site-url}/umbraco` and logging in with `admin@localhost.local` / `Admin1234!`, then the Umbraco dashboard is displayed

- [x] **AC 6:** Given logged into backoffice, when navigating to Content section, then starter kit sample content is visible

## Additional Context

### Dependencies

- .NET 10 SDK (**REQUIRED** - must install, currently only have 9.0.203)
- Umbraco.Templates 17.1.0 (installed)
- No external database required (SQLite is embedded)
- No Docker or containerization needed

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Build fails with TFM error | Verify .NET 10 installed: `dotnet --list-sdks` |
| Port already in use | Add `--urls "http://localhost:5050"` to run command |
| NuGet restore fails | Check internet connection, try `dotnet nuget locals all --clear` |
| Login fails | Verify using exact credentials: `admin@localhost.local` / `Admin1234!` |

### Testing Strategy

**Manual verification only** - this is a POC install:

1. `dotnet --list-sdks` - expect 10.x listed
2. `dotnet build` - expect success with 0 errors
3. `dotnet run` - expect site starts, URL displayed
4. Open browser to site URL - expect frontend or redirect to backoffice
5. Navigate to `/umbraco` - expect login page
6. Login with admin credentials - expect dashboard with starter kit content

### Notes

- This is a throwaway POC environment for testing Umbraco.AI package integration
- No production hardening needed
- SQLite database will be created in `UmbracoAI/umbraco/Data/` on first run
- To reset: Stop site, delete `UmbracoAI/umbraco/Data/` folder, re-run
- **Next phase:** Investigate and install Umbraco.AI package (requires separate research - package details TBD)
- Admin credentials stored in plain text in appsettings - acceptable for local POC only

### Review Notes

- Adversarial review completed: 17 findings (1 Critical, 1 High, 5 Medium, 10 Low)
- Resolution approach: Auto-fix
- Fixed: F3 (telemetry reduced to Basic), F16 (MacroErrors changed to Inline)
- Acknowledged for POC: F1, F2 (credentials intentional for local dev), F4-F7, F12-F15 (acceptable for POC scope)

### References

- [Umbraco 17 LTS release announcement](https://umbraco.com/blog/umbraco-17-lts-release/)
- [Umbraco CMS Releases](https://releases.umbraco.com/)
