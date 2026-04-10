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

### 2. Run the Site and Complete the Install Wizard

```bash
dotnet run
```

Open the site in the browser and complete the Umbraco install wizard (create admin account,
choose SQLite). Stop the site after setup completes.

### 3. Install AgentRun and an Umbraco.AI Provider

```bash
dotnet add package Umbraco.AI
dotnet add package Umbraco.AI.Anthropic
dotnet add package AgentRun.Umbraco --version 1.0.0-beta.1 --source /path/to/Umbraco-AI/nupkg
```

Replace `/path/to/Umbraco-AI/` with the absolute path to the repo root (e.g.,
`/Users/adamshallcross/Documents/Umbraco AI/`). Relative paths resolve from the consumer
project directory, not the repo root.

All three packages auto-register via Umbraco's composer system -- no `Program.cs` changes needed.
Installing `Umbraco.AI` as a direct dependency ensures it survives if you later remove AgentRun
(see uninstall step).

**Important:** Install these packages *after* the Umbraco install wizard has run. Umbraco.AI
runs database migrations on startup and will crash if no database is configured yet.

### 4. Copy Example Workflows

NuGet links content files from cache -- copy them to disk so the engine can discover them:

```bash
mkdir -p App_Data/AgentRun.Umbraco/workflows
cp -r ~/.nuget/packages/agentrun.umbraco/1.0.0-beta.1/contentFiles/any/any/App_Data/AgentRun.Umbraco/workflows/ \
      App_Data/AgentRun.Umbraco/workflows/
```

### 5. Run the Site Again

```bash
dotnet run
```

### 6. Grant User Group Permissions

Neither the AI section nor Agent Workflows will work until permissions are granted:

1. Go to **Users > User Groups > Administrators**
2. Grant both the **AI** section and the **AgentRun** section permissions
3. Save, then **log out and back in** (the auth token needs refreshing)

### 7. Create a Connection

1. Go to the **AI** section in the backoffice
2. Click **Connections > Create Connection**
3. Select the Anthropic provider, give it a name and alias, and enter your API key

### 8. Create a Profile

1. Click **Profiles > Create Profile**
2. Select the connection you just created, choose a model, and set an alias
3. Note the profile alias -- this is what goes in `workflow.yaml`

### 9. Update Workflow Profiles

Edit the `default_profile` field in both workflow files to match your profile alias:

- `App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml`
- `App_Data/AgentRun.Umbraco/workflows/accessibility-quick-scan/workflow.yaml`

Restart the site after editing (or wait for the manifest cache to refresh).

### 10. Verify Workflows Appear

1. Navigate to the **Agent Workflows** section in the backoffice
2. **Check:** Both "Content Quality Audit" and "Accessibility Quick-Scan" appear in the list

### 11. Run a Workflow

1. Click **Content Quality Audit**
2. Click **Start**
3. Paste a URL when prompted (e.g., your own site's homepage)
4. **Check:** The scanner step fetches the page and writes scan results
5. **Check:** The workflow progresses through all three steps (scanner, analyser, reporter)
6. **Check:** A final report artifact is produced

### 12. Uninstall AgentRun

```bash
dotnet remove package AgentRun.Umbraco
```

Since `Umbraco.AI` was installed as a direct dependency in step 3, it remains after removing
AgentRun. The site should continue to work without AgentRun.

### 13. Verify Clean Uninstall

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
