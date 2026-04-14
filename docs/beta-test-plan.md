# Beta Test Plan: Fresh Install Smoke Test

Manual verification that `AgentRun.Umbraco` installs, runs, and uninstalls cleanly on a fresh
Umbraco 17 site. Execute against the `.nupkg` produced by `dotnet pack`.

## Prerequisites

- .NET 10 SDK installed
- A valid Anthropic or OpenAI API key
- The local `.nupkg` file (at `nupkg/AgentRun.Umbraco.1.0.0-beta.2.nupkg`)

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
dotnet add package AgentRun.Umbraco --version 1.0.0-beta.2 --source /path/to/Umbraco-AI/nupkg
```

Replace `/path/to/Umbraco-AI/` with the absolute path to the repo root (e.g.,
`/Users/adamshallcross/Documents/Umbraco AI/`). Relative paths resolve from the consumer
project directory, not the repo root.

All three packages auto-register via Umbraco's composer system -- no `Program.cs` changes needed.
Installing `Umbraco.AI` as a direct dependency ensures it survives if you later remove AgentRun
(see uninstall step).

**Important:** Install these packages *after* the Umbraco install wizard has run. Umbraco.AI
runs database migrations on startup and will crash if no database is configured yet.

### 4. Run the Site Again

```bash
dotnet run
```

On first startup, AgentRun automatically copies the three example workflows to
`App_Data/AgentRun.Umbraco/workflows/` and grants the AgentRun section permission
to the Administrators user group.

Watch the console for migration output that confirms both ran:

- `Copied N files for workflow 'content-quality-audit'` (and likewise for
  `accessibility-quick-scan` and `umbraco-content-audit`)
- A log line confirming the AgentRun section was added to the Administrators group

If those lines don't appear, the migration did not run -- check that the package
is referenced and the site restarted cleanly.

Log out and back in to pick up the new section permission.

### 5. Create a Connection

1. Go to the **AI** section in the backoffice
2. Click **Connections > Create Connection**
3. Select the Anthropic provider, give it a name and alias, and enter your API key

### 6. Create a Profile

1. Click **Profiles > Create Profile**
2. Select the connection you just created, choose a model, and set an alias

AgentRun auto-detects your Umbraco.AI profile -- no YAML editing needed for first run.

### 7. Verify Workflows Appear

1. Navigate to the **Agent Workflows** section in the backoffice
2. **Check:** All three workflows appear in the list: "Umbraco Content Audit", "Content Quality Audit", and "Accessibility Quick-Scan"

### 8. Run the Umbraco Content Audit (Primary Smoke Test)

1. Click **Umbraco Content Audit**
2. Click **Start**
3. Confirm when prompted (this workflow reads from the site's own content -- no URL input needed)
4. **Check:** The scanner calls `list_content_types` and `list_content` to inventory the content model
5. **Check:** The scanner calls `get_content` for individual content nodes
6. **Check:** The workflow progresses through all three steps (scanner, analyser, reporter)
7. **Check:** The audit report references actual site content (node names, content types, real property values)

### 9. Run a URL-Based Workflow (Secondary Verification)

1. Click **Content Quality Audit**
2. Click **Start**
3. Paste a URL when prompted (e.g., your own site's homepage)
4. **Check:** The scanner step fetches the page and writes scan results
5. **Check:** The workflow progresses through all three steps (scanner, analyser, reporter)
6. **Check:** A final report artifact is produced

### 9b. Verify Provider Error Handling

This tests that provider misconfigurations surface clearly in the chat panel
rather than leaving the user staring at a blank screen.

1. Go to **AI > Connections** and edit the Anthropic connection you created in step 5
2. Change the API key to something obviously invalid (e.g., `sk-ant-api03-invalid`)
3. Save the connection
4. Start any workflow (Content Quality Audit is quickest)
5. **Check:** Within a few seconds, a clear error appears in the chat panel --
   something like "The AI provider rejected the API key", "billing or quota
   limits", or "The AI provider returned an empty response. Check your provider
   configuration and API credit."
6. **Check:** The step status is marked **Error** (not left in a loading spinner
   or blank state)
7. Restore the real API key on the connection
8. Start a fresh workflow run and confirm it proceeds normally

### 10. Uninstall AgentRun

```bash
dotnet remove package AgentRun.Umbraco
```

Since `Umbraco.AI` was installed as a direct dependency in step 3, it remains after removing
AgentRun. The site should continue to work without AgentRun.

### 11. Verify Clean Uninstall

1. Run the site: `dotnet run`
2. **Check:** Site starts without errors related to AgentRun
3. **Check:** No orphaned DLLs, configuration sections, or database tables
4. **Check:** `App_Data/AgentRun.Umbraco/` still exists (runtime data) -- this is expected
   and can be deleted manually if desired

## Pass Criteria

- [ ] All three example workflows appear in the dashboard after install
- [ ] Umbraco Content Audit runs successfully and produces an audit report referencing actual site content
- [ ] At least one URL-based workflow (CQA or Accessibility) runs successfully end-to-end
- [ ] Provider misconfiguration (bad API key) produces a clear chat error, not a blank screen or hang
- [ ] No errors on site startup after uninstall
- [ ] No database tables, config entries, or files outside `App_Data/AgentRun.Umbraco/` created
