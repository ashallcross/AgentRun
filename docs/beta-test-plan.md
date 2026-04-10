# Beta Test Plan: Fresh Install Smoke Test

Manual verification that `AgentRun.Umbraco` installs, runs, and uninstalls cleanly on a fresh
Umbraco 17 site. Execute against the `.nupkg` produced by `dotnet pack`.

## Prerequisites

- .NET 10 SDK installed
- A valid Anthropic or OpenAI API key
- The local `.nupkg` file (at `nupkg/AgentRun.Umbraco.1.0.0-beta.1.nupkg`)

## Steps

### 1. Create a Fresh Umbraco Site

```bash
dotnet new umbraco -n TestInstall
cd TestInstall
```

### 2. Install AgentRun from Local Package

```bash
dotnet add package AgentRun.Umbraco --version 1.0.0-beta.1 --source ../nupkg
```

### 3. Install an Umbraco.AI Provider

```bash
dotnet add package Umbraco.AI.Anthropic
```

### 4. Copy Example Workflows

NuGet links content files from cache -- copy them to disk so the engine can discover them:

```bash
cp -r ~/.nuget/packages/agentrun.umbraco/1.0.0-beta.1/contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/ \
      App_Data/AgentRun.Umbraco/workflows/
```

### 5. Run the Site

```bash
dotnet run
```

Complete the Umbraco install wizard in the browser.

### 6. Configure an AI Profile

1. Go to **Settings > AI** in the backoffice
2. Create a profile for your provider (e.g., Anthropic with your API key)
3. Note the profile alias

### 7. Update Workflow Profiles

Edit the `default_profile` field in both workflow files to match your profile alias:

- `App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml`
- `App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/workflow.yaml`

Restart the site after editing (or wait for the manifest cache to refresh).

### 8. Verify Workflows Appear

1. Navigate to the **Agent Workflows** section in the backoffice
2. **Check:** Both "Content Quality Audit" and "Accessibility Quick-Scan" appear in the list

### 9. Run a Workflow

1. Click **Content Quality Audit**
2. Click **Start**
3. Paste a URL when prompted (e.g., your own site's homepage)
4. **Check:** The scanner step fetches the page and writes scan results
5. **Check:** The workflow progresses through all three steps (scanner, analyser, reporter)
6. **Check:** A final report artifact is produced

### 10. Verify User Permissions

If the Agent Workflows section is not visible:

1. Go to **Users > User Groups > Administrators**
2. Check that the AgentRun section permission is granted
3. Save and refresh

### 11. Uninstall the Package

```bash
dotnet remove package AgentRun.Umbraco
```

### 12. Verify Clean Uninstall

1. Run the site: `dotnet run`
2. **Check:** Site starts without errors related to AgentRun
3. **Check:** No orphaned DLLs, configuration sections, or database tables
4. **Check:** `App_Data/AgentRun.Umbraco/` still exists (runtime data) -- this is expected
   and can be deleted manually if desired

## Pass Criteria

- [ ] Both example workflows appear in the dashboard after install
- [ ] At least one workflow runs successfully end-to-end
- [ ] No errors on site startup after uninstall
- [ ] No database tables, config entries, or files outside `App_Data/AgentRun.Umbraco/` created
