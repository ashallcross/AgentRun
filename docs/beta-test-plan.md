# Beta Test Plan: Fresh Install Smoke Test

Manual verification that `AgentRun.Umbraco` installs, runs, and uninstalls cleanly on a fresh
Umbraco 17 site. Execute against the `.nupkg` produced by `dotnet pack` (for a v1.2
pre-release, e.g. `AgentRun.Umbraco.1.2.0-rc.1.nupkg`) or against the published v1.2.0
package on NuGet once released.

## Prerequisites

- .NET 10 SDK installed
- A valid Anthropic or OpenAI API key (Anthropic is the most thoroughly exercised provider)
- The local `.nupkg` file -- place it in a folder of your choice, e.g.
  `./nupkg/AgentRun.Umbraco.1.2.0-rc.1.nupkg`, and note the absolute path for step 3
- **Optional** (only needed for the Brand-pillar + web-search checks in steps 8b and 9c):
  an Umbraco.AI Context created in the backoffice, plus a Brave or Tavily API key

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
dotnet add package AgentRun.Umbraco --version 1.2.0-rc.1 --source /absolute/path/to/nupkg
```

Replace `/absolute/path/to/nupkg` with the absolute path to the folder containing the
`.nupkg` file. Relative paths resolve from the consumer project directory, not the folder
you launched `dotnet` from, so absolute is safer. For a released build, drop the
`--source` flag and use `--version 1.2.0` against NuGet.org.

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

1. Click **Profiles > Create > Chat**
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
6. **Check (v1.2 body extraction):** At least one `get_content` response includes a
   non-empty `body_text` field sampled from a page that has Block List, Block Grid, or
   Rich Text body copy -- readability / accessibility / brand-pillar scoring in later
   steps should reference prose or block-level findings, not just the node name / URL.
   Layout-only nodes (navigation, redirects) may legitimately return `body_text: null` --
   that's the documented three-state semantic.
7. **Check:** The workflow progresses through all three steps (scanner, analyser, reporter)
8. **Check:** The audit report references actual site content (node names, content types,
   real property values, and any prose sampled from `body_text`)

### 8b. Content Audit v2 — Brand Pillar (requires an Umbraco.AI Context)

The Umbraco Content Audit is a conditional 6-or-7 pillar audit. By default
(`brand_voice_context: ""`) the audit scores six pillars and matches the v1.1 byte-compat
output. Set a non-empty `brand_voice_context` to enable the Brand pillar.

1. In the backoffice **Contexts** section, create (or reuse) an Umbraco.AI Context named
   something like `corporate-brand-voice`, with at least one resource of type
   `brand-voice` whose `toneDescription`, `targetAudience`, and `styleGuidelines` are
   populated
2. Edit `App_Data/AgentRun.Umbraco/workflows/umbraco-content-audit/workflow.yaml` -- set
   `config.brand_voice_context:` to the alias of the Context you created. Restart the
   site so the workflow manifest refreshes
3. Start the Umbraco Content Audit and let the scanner's **Brand Pillar
   Pre-Validation Gate** run -- the scanner should call `get_ai_context(alias: …)`
   before any `list_content` / `get_content` call, confirming the Context resolves
4. **Check (7-pillar variant):** The scanner's opening line, capability answer, and
   confirmation template all reference seven pillars (the documented 7-pillar variant);
   the Brand pillar appears in the final report with a score grounded in the
   brand-voice resources the agent read via `get_ai_context`
5. **Check (get_ai_context tool shape):** The `get_ai_context` call response contains
   `resources: [...]` with `resourceTypeId: "brand-voice"` and the `settings` sub-object
   carries `toneDescription` / `targetAudience` / `styleGuidelines` (the Umbraco.AI 1.8
   brand-voice shape)
6. **Check (v1.2 `search_content` discovery — when the scanner infers deprecated
   terms):** `search_content` is an *additive discovery probe*, not a mainline tool call.
   With the Brand pillar enabled, the scanner may call `search_content` for
   orphan-terminology discovery — sampling pages that mention deprecated brand terms it
   inferred from the Context's `avoidPatterns` / style guidance, or terms the user named
   explicitly when they started the workflow. If the scanner does call it, confirm at
   least one `search_content` call appears in the step's tool-call history and its hits
   are recorded under a `## Search Discoveries` section in `scan-results.md`. Silent
   skipping is valid — on a default run with no inferrable deprecated terms and no user
   keyword direction, the scanner won't call `search_content` and that is **not** a
   regression.
7. Revert the workflow.yaml to `brand_voice_context: ""`, restart, re-run the audit
8. **Check (6-pillar variant):** The scanner no longer calls `get_ai_context`, no Brand
   Pillar pre-validation runs, the final report has six pillars and no Brand score --
   equivalent to the v1.1 audit

### 9. Run a URL-Based Workflow (Secondary Verification)

1. Click **Content Quality Audit**
2. Click **Start**
3. Paste a URL when prompted (e.g., your own site's homepage)
4. **Check:** The scanner step fetches the page and writes scan results
5. **Check:** The workflow progresses through all three steps (scanner, analyser, reporter)
6. **Check:** A final report artifact is produced

### 9b. Verify `web_search` Tool Wiring (optional — requires Brave or Tavily API key)

The three shipped example workflows don't call `web_search`; it's available for custom
workflows. This check verifies the tool's configuration-reading and provider selection
works end-to-end without needing a custom workflow.

1. Stop the site
2. Add a `WebSearch` section to `appsettings.json` with a real Brave or Tavily API key:

   ```json
   {
     "AgentRun": {
       "WebSearch": {
         "DefaultProvider": "Brave",
         "Providers": {
           "Brave": { "ApiKey": "<your-brave-key>" }
         }
       }
     }
   }
   ```

3. Start the site
4. **Check:** The site starts without configuration-read errors referencing `WebSearch`
5. **Check:** No secrets appear in any log line (`ApiKey` values should never be echoed
   to console or log file)
6. **Optional end-to-end:** Create a one-step test workflow that declares `tools: [web_search]`
   and an agent prompt that calls `web_search(query: "umbraco cms")`. Run it; confirm the
   tool returns a populated `results: [...]` array with title / url / snippet fields

### 9c. Verify Provider Error Handling

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
- [ ] Umbraco Content Audit runs successfully and produces an audit report referencing
      actual site content
- [ ] v1.2 body extraction: at least one `get_content` response carries a non-empty
      `body_text` sampled from Block List / Block Grid / Rich Text content, and downstream
      pillar scoring references that prose
- [ ] v1.2 `search_content`: when Brand pillar is enabled and the scanner can infer
      deprecated terms from the brand voice (or the user explicitly asks for
      keyword-scoped analysis), at least one `search_content` call appears in the
      scanner's tool-call history. Silent skipping is valid on a default 6-pillar audit
      with no keyword direction
- [ ] v1.2 Brand pillar: with `brand_voice_context` set, a 7-pillar audit runs and the
      Brand pillar score is grounded in `get_ai_context` resources; with
      `brand_voice_context: ""`, a 6-pillar audit runs byte-compatibly with v1.1
- [ ] v1.2 `get_ai_context`: at least one call returns a `resources` array with a
      `brand-voice` resource carrying the expected `toneDescription` /
      `targetAudience` / `styleGuidelines` settings
- [ ] v1.2 `web_search` (optional, if a Brave or Tavily key is configured): site starts
      without `WebSearch` config-read errors and no secrets appear in logs. End-to-end
      search via a custom test workflow returns populated results
- [ ] At least one URL-based workflow (CQA or Accessibility) runs successfully end-to-end
- [ ] Provider misconfiguration (bad API key) produces a clear chat error, not a blank
      screen or hang
- [ ] No errors on site startup after uninstall
- [ ] No database tables, config entries, or files outside `App_Data/AgentRun.Umbraco/`
      created
