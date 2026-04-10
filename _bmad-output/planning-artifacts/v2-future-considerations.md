# Shallai Umbraco Agent Runner — V2 / Future Considerations

_Living document for architectural decisions, limitations, and ideas that need addressing beyond the current V1 implementation. Add thoughts freely — they'll be triaged and formalised when we plan the next cycle._

**Last updated:** 2026-04-07
**Status:** Active ideation — not yet prioritised or committed

---

## 1. Storage & Persistence

### Current State (V1)

Instance data (YAML metadata + JSONL conversation files) is written to `{ContentRootPath}/App_Data/Shallai.UmbracoAgentRunner/instances/` using atomic file writes. This works reliably on single-instance Umbraco deployments with local disk access.

### Known Limitations

| Scenario | Impact | Severity |
|----------|--------|----------|
| **Load-balanced / multi-node** | Instance files written on node A are invisible to node B. Workflows started on one node cannot be resumed on another. | Blocker |
| **Umbraco Cloud** | App_Data persists across deployments (excluded from deploy payload) but Cloud can scale out to multiple instances, hitting the same multi-node problem. | Blocker at scale |
| **Azure Blob Storage / AWS S3** | Orgs that have moved Umbraco media to blob storage have already accepted local disk is unreliable. Our file writes become the odd one out — an inconsistency in their infrastructure model. | Friction / support burden |
| **Container deployments (Docker/K8s)** | Ephemeral filesystems by default. App_Data may not persist across container restarts unless a volume mount is explicitly configured. | Blocker without config |
| **Hardened IIS / restricted permissions** | Some hosting environments lock down write access. App_Data is usually writable by convention, but hardened setups or non-standard deployments may not grant it. | Support headache |

### Proposed Solution: Storage Provider Abstraction

Introduce an `IInstanceStorageProvider` interface that abstracts all instance I/O behind a clean contract.

**Phase 1 — Abstraction (low effort, high payoff)**
- Define `IInstanceStorageProvider` with methods covering all current file operations (read/write instance metadata, append conversation entry, list instances, delete instance, etc.)
- Move current disk implementation behind this interface (`DiskInstanceStorageProvider`)
- Register via DI — swappable by consuming applications
- No behaviour change, pure refactor

**Phase 2 — Database Provider**
- `DatabaseInstanceStorageProvider` using Umbraco's own database connection (no additional infrastructure for the user)
- Every Umbraco site has a database — this is the most universal answer
- Solves load balancing in one move (shared state)
- Needs schema migration strategy (Umbraco's `IMigrationPlan` or raw SQL migrations)
- Consider: do we store conversation JSONL as a blob column or normalise into rows? Blob is simpler and preserves the append-only model; rows enable querying but add complexity.

**Phase 3 — Blob Storage Provider (optional)**
- `BlobInstanceStorageProvider` for Azure Blob Storage / AWS S3
- Appeals to orgs already on cloud-native infrastructure
- Requires configuration for connection strings / credentials
- Consider: append operations on blob storage are not atomic in the same way as local files. May need to rethink the JSONL append-only model for blob targets (e.g., block blob append, or buffered writes).

### Open Questions

- [ ] Should storage provider selection be via Umbraco configuration (appsettings.json) or DI registration? Configuration is simpler for end users; DI is more flexible for developers.
- [ ] Do we need a migration path from disk to database (i.e., import existing instance data)? Or is it acceptable to start fresh when switching providers?
- [ ] Should the database provider use Umbraco's NPoco/PetaPoco or raw ADO.NET? Using Umbraco's data layer ties us to their abstractions but gives us migration tooling for free.
- [ ] For conversation history specifically — is append-only JSONL the right model long-term, or should we consider a more structured format that plays better with databases?

---

## 2. Deployment Topology

### Scenarios to Support

| Topology | V1 Status | Notes |
|----------|-----------|-------|
| **Single server** | Supported | The default. Works today. |
| **Headless (separate backoffice)** | Supported | We only run in the backoffice host. Content delivery API / frontend doesn't touch our code. |
| **Backoffice on separate box from frontend** | Supported | Same as above — we attach to the backoffice host, that's our world. |
| **Load balanced (multiple backoffice nodes)** | Not supported | Blocked by disk storage. Solved by database provider (see above). |
| **Umbraco Cloud** | Partially supported | Works single-instance. Breaks if Cloud scales out. |
| **Dev / Staging / UAT / Prod** | Works but needs documentation | Workflow YAML files deploy with the code. Instance data is runtime state — it shouldn't travel between environments. This separation is actually clean, but needs documenting clearly. |
| **Container / Kubernetes** | Not supported without volume config | Ephemeral filesystem. Database provider solves this. |

### Multi-Environment Considerations

- Workflow definitions are code artefacts — they deploy through the normal CI/CD pipeline
- Instance data (running/completed workflow executions) is environment-specific runtime state
- API keys / LLM provider configuration is per-environment (already handled by Umbraco.AI's config model)
- Question: do we need environment-aware workflow definitions? (e.g., different tool configurations per environment, different allowed models). Probably not for V2, but worth noting.

---

## 3. Network & Security

### LLM Connectivity

- Outbound HTTPS to the configured LLM provider (e.g., `api.anthropic.com`, `api.openai.com`) must be open
- This is fundamentally Umbraco.AI's concern, not ours — if they've installed and configured a provider, the network path already works
- We inherit the working connection via `IAIChatService.GetChatClientAsync()`
- **Document clearly** but don't try to solve — it's upstream of us

### Firewall Considerations

- Corporate environments may restrict outbound traffic
- Proxy support — does `HttpClient` in the Umbraco.AI providers respect system proxy settings? Likely yes (via `HttpClientFactory`), but worth verifying and documenting
- Some orgs may need to allowlist specific domains — we should list what domains each provider needs
- Air-gapped environments — cannot use cloud LLM providers at all. Future consideration: support for local/self-hosted models (Ollama, vLLM, etc.) via Umbraco.AI providers

### Tool Security (already addressed in V1 architecture)

- SSRF protection on URL fetching tools
- Path sandboxing on file I/O tools
- Tool whitelisting per workflow step
- These are solid — maintain and extend as new tools are added

---

## 4. Tool Extensibility

### Current State

- `IWorkflowTool` interface with per-step whitelist model
- Built-in tools: read, write, fetch_url
- Tools registered via DI and discovered by the engine
- Strict whitelist — only tools explicitly listed in a workflow step's `tools` array may execute

### What's Needed

#### 4.1 Custom Tool Registration (Priority: High)

Orgs will want to add their own tools to talk to their systems — CRM, PIM, DAM, ERP, custom APIs, etc.

**Proposed approach:**
- The `IWorkflowTool` interface is already the contract
- Developers implement the interface, register via DI (Umbraco's `IComposer`)
- Tools are automatically discoverable by alias in workflow step definitions
- Need: clear documentation and examples for third-party tool development
- Need: a way to package and distribute tools (NuGet packages that register themselves)
- Consider: should custom tools have a separate namespace/prefix to avoid collisions with built-in tools?

#### 4.2 Generic HTTP/API Tool (Priority: High)

A configurable HTTP tool that can call arbitrary APIs with:
- Configurable URL, method, headers, body template
- Authentication support (API key, Bearer token, Basic auth)
- Response mapping (extract specific fields from JSON responses)
- SSRF protections inherited from existing fetch_url safeguards
- Timeout and retry configuration

This covers a huge number of integration scenarios without bespoke tool development.

#### 4.3 MCP (Model Context Protocol) Support (Priority: Medium-High)

The industry is converging on MCP as the standard for LLM tool connectivity. Supporting MCP would let users connect to any MCP-compatible service without bespoke integrations.

**Architecture implications:**
- MCP uses JSON-RPC over stdio (local processes) or SSE (remote servers)
- This is a different protocol pattern from our current tool execution model
- Need an MCP client that can connect to configured MCP servers
- MCP tools would need to be bridged into our `IWorkflowTool` model, or we need a parallel execution path
- Configuration: which MCP servers to connect to, per-workflow or globally?
- Security: MCP servers can expose arbitrary tools — how does this interact with our whitelist model? Do we whitelist individual MCP tools, or trust/block entire servers?
- Lifecycle: MCP server connections need to be managed (startup, health checks, reconnection)

**Considerations:**
- Do we implement MCP client ourselves or use an existing .NET MCP client library?
- How do MCP tool descriptions map to our `AIFunction` model?
- Should MCP be a separate package (`Shallai.UmbracoAgentRunner.Mcp`) to keep the core lean?

#### 4.4 Tool Marketplace / Registry (Priority: Low — Future)

- A way for the community to discover and share tools
- Could be as simple as a curated list of NuGet packages
- Or as ambitious as a UI in the backoffice for browsing/installing tools
- Very much a V3+ consideration

### Open Questions

- [ ] Should custom tools be registered globally or per-workflow? Global is simpler; per-workflow gives more control.
- [ ] Do we need a tool versioning model? What happens when a tool's interface changes and existing workflows reference the old version?
- [ ] Should tools declare their own security requirements (e.g., "I need outbound HTTP", "I need file system access") so admins can audit?
- [ ] MCP: stdio vs SSE — do we need to support both transport modes, or pick one?

---

## 5. Product Positioning & Limitations

### V1 — Be Honest

Ship with a clear, no-fluff compatibility statement:

**Supported:**
- Single-instance Umbraco 17+ deployments
- Local disk storage (App_Data)
- Outbound HTTPS to configured LLM provider
- Windows / Linux / macOS hosting (anywhere .NET 10 runs)

**Not yet supported:**
- Load-balanced / multi-node deployments
- Azure Blob Storage / AWS S3 for instance data
- Custom tool registration (API available but undocumented)
- MCP server connectivity
- Air-gapped / offline LLM providers

**Requires:**
- Write access to App_Data directory
- Outbound HTTPS (port 443) to LLM provider endpoints
- Umbraco.AI 1.7+ with at least one configured LLM provider
- API key for the configured LLM provider

### V2 — Expand the Envelope (Pro Package Launch)

Target additions (shipped as the Pro NuGet package):
- Database storage provider (works everywhere Umbraco works)
- Documented custom tool API with examples
- Generic HTTP/API tool
- MCP client support
- Clear multi-environment guidance
- Workflow analytics and cost tracking

Free package additions:
- `IInstanceStorageProvider` abstraction (enables pro storage providers)
- Improved documentation and onboarding
- Community-contributed workflow examples

### V3+ — Platform Play

- Tool marketplace / community registry
- Blob storage providers
- Workflow versioning and migration
- Multi-tenant support
- Audit logging and compliance features
- Community tool ecosystem

---

## 6. Commercial Model & Licensing Architecture

### Strategic Context

The uSync model is the proven playbook in this ecosystem. uSync ships a genuinely useful free/open-source package, then sells `uSync.Complete` as a separate licensed NuGet package that adds power features. It works because:

- The free version builds adoption and community trust
- The paid version adds *capabilities*, not artificial limits
- No DRM, no file counting, no licence key validation at runtime
- You either have the pro NuGet package installed or you don't

**We should follow the same pattern.** This is also a speed advantage — no licence server infrastructure to build, no billing integration to maintain. Ship the free package on NuGet.org, ship the pro package on a licensed feed. Fast to market.

### Competitive Positioning

Umbraco HQ is building their own version. Our advantages:

- **First-mover** — ship something useful while HQ is still in preview
- **Community-driven** — closer to what real developers need, not what fits HQ's commercial strategy
- **Simpler** — less ceremony, less config, just works (the uSync playbook)
- **Open core** — free version is genuinely useful, pro adds enterprise capabilities

HQ's version will likely be tied to Umbraco Cloud pricing or their own commercial model. We can be the community alternative that works everywhere.

### Proposed Tier Structure

#### Free / Community Edition (NuGet.org — open source)

Everything needed to run AI agent workflows on a single-instance Umbraco site:

- Unlimited workflows, unlimited steps (no artificial limits — this is critical for adoption)
- Built-in tools: read, write, fetch_url
- Disk storage provider (`DiskInstanceStorageProvider`)
- Full workflow engine with SSE streaming
- Backoffice UI: workflow runner, conversation panel, tool call display
- Community support (GitHub issues)

**Why unlimited workflows:** restricting workflow/step counts is trivial to bypass (they're YAML files on disk), annoying for legitimate users, and creates a hostile first impression. The free version must feel generous, not crippled. This is how you win community love.

#### Pro / Commercial Edition (licensed NuGet feed — paid)

Enterprise capabilities for orgs running Umbraco at scale:

**Storage & Infrastructure**
- Database storage provider (`DatabaseInstanceStorageProvider`) — enables multi-node, load-balanced, container, and cloud deployments
- Blob storage provider (`BlobInstanceStorageProvider`) — Azure Blob Storage, AWS S3
- Instance data migration tool (disk → database)

**Tool Extensibility**
- MCP (Model Context Protocol) client — connect to any MCP-compatible service
- Generic HTTP/API tool — configurable endpoint calling with auth, templating, response mapping
- Custom tool registration API — documented, supported, with examples and a developer guide
- Tool packaging guidance — how to distribute custom tools as NuGet packages

**Operations & Compliance**
- Workflow analytics dashboard — execution times, failure rates, tool call frequency
- LLM cost tracking — per-workflow, per-step token usage and estimated costs
- Audit log export — structured events for compliance, with retention policy configuration
- Health check endpoints — LLM provider connectivity monitoring

**Support**
- Priority support channel
- Migration assistance for complex deployments

### Architectural Enforcement Model

**How it works technically:**

The free package ships the *interfaces and abstractions*:
- `IInstanceStorageProvider` (interface) — in the free package
- `DiskInstanceStorageProvider` (implementation) — in the free package
- `IWorkflowTool` (interface) — in the free package
- Built-in tool implementations — in the free package

The pro package ships *additional implementations*:
- `DatabaseInstanceStorageProvider` — in the pro package
- `BlobInstanceStorageProvider` — in the pro package
- `McpToolBridge` — in the pro package
- `HttpApiTool` — in the pro package
- Analytics and audit services — in the pro package

**This means:**
- No runtime licence checks, no feature flags, no DRM
- The pro package registers its services via its own `IComposer` — install the NuGet package and it just works
- Someone *could* theoretically build their own database provider using the free package's interfaces — and that's fine. The value of pro is that it's already built, tested, and supported.
- Clean separation of concerns — the free package never references the pro package

**Package structure:**
```
Free:  Shallai.UmbracoAgentRunner          → NuGet.org (open source)
Pro:   Shallai.UmbracoAgentRunner.Pro       → Licensed feed (paid)
```

Or if MCP becomes large enough:
```
Free:  Shallai.UmbracoAgentRunner          → NuGet.org
Pro:   Shallai.UmbracoAgentRunner.Pro       → Licensed feed
MCP:   Shallai.UmbracoAgentRunner.Mcp       → Licensed feed (part of Pro licence)
```

### Pricing Considerations

_Final pricing is a product/analyst decision — these are architectural constraints on pricing models:_

| Model | Pros | Cons | Infrastructure Needed |
|-------|------|------|-----------------------|
| **One-off per-site licence** (uSync model) | Simple, proven in Umbraco ecosystem, no billing infra | No recurring revenue, harder to fund ongoing development | Licensed NuGet feed only |
| **Annual subscription per-site** | Recurring revenue, funds ongoing development | Needs licence management (expiry, renewal), more support overhead | Licensed feed + licence server or simple key validation |
| **One-off with paid annual support/updates** | Best of both — upfront revenue + renewal incentive | More complex to communicate | Licensed feed + version-gated access |

**Recommendation from architecture perspective:** Start with the one-off per-site model (lowest infrastructure overhead, fastest to market). Move to annual subscription later if the product matures enough to justify ongoing feature delivery. Don't build billing infrastructure before you have paying customers.

**Price point ballpark:** £499–£999 per site feels right for the Umbraco ecosystem (uSync.Complete is in this range). Mary/analyst should validate against the market.

### What This Means for V1 Development (Now)

To keep the commercial option open without slowing V1 down:

1. **Keep building V1 as-is** — don't split packages yet, don't add feature gates
2. **Ensure clean interface boundaries** — `IInstanceStorageProvider`, `IWorkflowTool` must be stable, well-defined contracts. This is already in progress.
3. **Engine stays dependency-free** — already a rule, now doubly important. The engine is the core that both free and pro build on.
4. **Don't put pro-only logic in V1** — when we build the database provider, MCP support, etc., build them as separate classes that could live in a separate assembly. Don't tangle them into the core.

The actual package split can happen when we're ready to ship V2. It's a refactor (move files to a new project, add a project reference), not an architecture change — *as long as the interfaces are clean now*.

### Open Questions

- [ ] Product naming — "Shallai" doesn't communicate what this does. Needs a name that resonates with the Umbraco community. (Analyst/brand decision)
- [ ] Open source licence for the free package — MIT? Apache 2.0? Needs to allow commercial use while protecting the pro package.
- [ ] Licensed NuGet feed provider — self-hosted (MyGet, private Azure Artifacts feed) or a service like Gumroad/Paddle for licence key generation?
- [ ] Do we offer a trial period for the pro package? If so, how? Time-limited NuGet feed access is simplest.
- [ ] Community contributions — can the community contribute to the pro package, or only the free one? uSync keeps the pro package closed-source.

---

## 7. Additional Considerations

### Cost & Usage Tracking

- LLM API calls cost money — orgs will want visibility into usage
- Per-workflow, per-step, per-instance token counts and estimated costs
- Umbraco.AI may or may not surface this — check what's available upstream
- At minimum, log token usage; ideally, surface in the backoffice UI

### Workflow Versioning

- What happens when a workflow definition changes while instances are in-flight?
- Currently: undefined behaviour (instance references workflow by alias, loads current definition)
- Needed: version pinning — an instance locks to the workflow version it was started with
- Could be as simple as snapshotting the workflow YAML into the instance folder at creation time

### Audit & Compliance

- Regulated industries need audit trails — who ran what workflow, when, with what inputs, what did the LLM produce?
- Conversation JSONL files are actually a good raw audit log
- May need: structured audit events, retention policies, export capabilities
- GDPR consideration: conversation data may contain PII. Need a deletion/anonymisation mechanism.

### Observability

- Structured logging is in place (V1), but production deployments need more:
  - Health checks for LLM provider connectivity
  - Metrics: workflow execution times, failure rates, tool call frequency
  - Alerting hooks for failed workflows
  - OpenTelemetry integration?

---

## Ideas Backlog

_Drop raw ideas here. No format required. They'll be triaged into the sections above periodically._

### Writing memory files when the agent isn't allowed out of the instance folder

Right now our agents don't have any sidecar or memory files, but more advanced ones do. the agents are instructed for security reasons to not go out of the instance folder, but to save memories they have to. we need to find a secure way to allow this

### Permissions and allowing per person workflows

we should think about permissoins too actually as right now anyone can go in and trigger a flow and also go in a look at one somone else has triggered. this isn't a high priority, but for enterprise level apps, it will be a consideration, especially an audit trail of who has done what and why. maybe we can tap into the umbraco audit logger?

### Tree Navigation UX Overhaul (captured 2026-04-02)

Move the Agent Workflows listing page into the Umbraco section tree (left nav panel). Workflows become tree navigation items — click a workflow name to see its detail page in the main panel. Removes the need for a separate `/workflows` listing page.

**Benefits:**
- Follows Umbraco's native navigation pattern (Content, Media, etc. all use tree nav)
- Frees up the main panel for richer workflow detail: workflow type, step count, instance history, and later — permissions, settings, workflow management
- Could add a section dashboard (landing page when you click the section root) for overview/stats

**Scope:**
- Manifests: register a section tree with `ManifestSectionSidebarApp` or `ManifestTree`
- Backend: tree endpoint returning workflow aliases as tree nodes
- Frontend: remove `shallai-workflow-list.element.ts` (or repurpose), update routing to load workflow detail from tree node click
- Instance list becomes the workflow detail page content (or a tab within it)
- Touches routing for every existing view — significant but clean refactor

**Recommendation:** Scope as a dedicated UX story in a future epic (not V1 MVP). It's a quality-of-life improvement, not a blocker. Could pair with the dashboard idea for a "UX polish" epic post-V1.

---

---

## Engine Flexibility Audit — Hardcoded Policy Values

**Added:** 2026-04-07
**Triggered by:** Story 9.6 (Workflow-Configurable Tool Limits) and the broader architectural realisation that the engine has been making policy decisions that belong to workflow authors. The 100 KB HTML fetch limit hardcoded in `FetchUrlTool.cs` was the trigger — a perfectly reasonable workflow (audit a real website) collided with an engine assumption (HTML pages are small) and broke. The pattern is wider than that one constant.

### The principle (record this as the architectural rule)

An engine for running arbitrary workflows must separate **mechanism** (engine concern) from **policy** (workflow concern):

- **Mechanism stays in the engine.** Tool dispatch, sandboxing behaviour, prompt assembly, state persistence atomicity, SSE streaming protocol, authentication, the *existence* of safety limits.
- **Policy belongs to the workflow.** The *values* of safety limits, which tools a step can call, which model to use, what counts as completion, prompt content, timeout values, retry policies.
- **Safety invariants are non-negotiable.** SSRF blocking rules, path sandboxing rules, the *fact* that some size/iteration limit exists. These are the only hardcoded policy decisions the engine should make. The values are configurable. The existence is not.

### The catalogue

This is the master list of every hardcoded policy value in the engine as of 2026-04-07. Each is classified, scoped, and (where applicable) flagged for migration.

| # | Location | Value | What it controls | Classification | Status |
|---|---|---|---|---|---|
| 1 | `FetchUrlTool.cs:19-20` | 200 KB / 100 KB | Max fetch_url response size (split by content-type) | **Tuning value** — must be configurable | **Story 9.6 (BETA)** — collapsed to single `max_response_bytes`, workflow → step → site default → engine default with site-level hard cap |
| 2 | `AgentRunComposer.cs:60` | 15 seconds | HTTP fetch_url timeout | **Tuning value** — must be configurable | **Story 9.6 (BETA)** — same resolution chain |
| 3 | `ToolLoop.cs:12` | 5 minutes | User message wait timeout (how long to wait for user input before exiting tool loop) | **Tuning value** — must be configurable | **Story 9.6 (BETA)** — same resolution chain |
| 4 | `ToolLoop.cs:11` | 100 iterations | Max LLM round-trips per step before bail | **Tuning value** — must be configurable | **V2** — not blocking the beta. Migrate to same resolution chain as 9.6 establishes. Pattern is mechanical. |
| 5 | `FetchUrlTool.cs:92-99` | content-type → limit mapping | Which HTTP content types get which limit | **Tuning value, but obsolete** | **Story 9.6 (BETA)** — collapsed by removing the split (single limit applies regardless of content type). The mapping function disappears. |
| 6 | `PromptAssembler.cs:40-41` | `sidecars/{stepId}/instructions.md` | Default sidecar instruction file path | **Convention vs. config** — debatable | **V2** — consider whether sidecar layout should be workflow-configurable (e.g. shared sidecars across steps, alternative folder layouts). Not blocking. |
| 7 | `WorkflowValidator` / `CompletionChecker` | `files_exist` only | The completion check mechanism is hardcoded to one strategy | **Mechanism gap** | **V2** — consider pluggable completion strategies (file existence, tool-call signal, output marker, custom predicate). Significant design work. Not blocking. |
| 8 | `ToolLoop` exit conditions | Hardcoded "no more tool calls = done" semantics | The engine decides what "step done" means, not the workflow | **Mechanism gap** | **V2** — overlaps with the Story 9.0 fix and item 7. The engine should distinguish: stalled, awaiting user, awaiting work, complete. Story 9.0 partially addresses this; deeper rework is V2. |
| 9 | SSE event names (various) | `text.delta`, `tool.start`, `tool.result`, `tool.end`, `step.finished`, `input.wait`, `run.error` | Fixed event taxonomy | **Convention** — could become extensible | **V2** — workflows can't emit custom events to the chat panel today. Low priority unless real demand emerges. |
| 10 | `WriteFileTool` / `ReadFileTool` | Instance folder only | File tools are sandboxed to instance folder; no workflow-folder reads | **Safety invariant for v1, policy gap for v2** | **V2** — relates to data_files, variants, and sample data discussions. Requires careful sandbox design (declared paths only). |

### Items NOT in the catalogue (intentionally hardcoded — safety invariants)

These should NEVER become workflow-configurable. Recording them explicitly so future contributors don't try.

- **SSRF protection rules** in `SsrfProtection.cs` — RFC1918, loopback, link-local IP blocking. A workflow author who needs internal IPs should NOT be able to disable this via YAML. It's a confused-deputy attack vector.
- **Path sandboxing canonical-path checking** in `PathSandbox.cs` — the rule "canonicalise paths and reject anything outside the allowed root" is not negotiable. Only the *allowed root* itself can become configurable (and even that is a v2 architectural decision, not a config knob).
- **Atomic write semantics** (temp file + rename) — implementation invariant for crash safety.
- **The fact that *some* size limit exists for fetch_url** — `max_response_bytes` is configurable; setting it to "unlimited" is not allowed. There must always be a finite ceiling, even if the workflow author makes it large.
- **The fact that *some* iteration cap exists in ToolLoop** — `max_iterations` is configurable; setting it to "unlimited" is not allowed. Misbehaving agents must hit a wall.
- **Backoffice authentication** — the `BackOfficeAccess` policy on all endpoints is non-negotiable.

### The resolution chain pattern (established by Story 9.6)

Story 9.6 establishes the pattern that all future tunable values follow. Recording it here as the canonical reference:

```
effective_value = (
    step.tool_overrides[tool].setting
    ?? workflow.tool_defaults[tool].setting
    ?? site.tool_defaults[tool].setting       // appsettings.json AgentRun:ToolDefaults
    ?? engine_default                          // hardcoded fallback if nothing else set
)

// Then enforce hard cap:
if site.tool_limits[tool].setting_max is set:
    effective_value = min(effective_value, site.tool_limits[tool].setting_max)
```

**Hard cap is a hard cap.** Workflow YAML cannot exceed the site-level ceiling under any circumstances. Decision recorded 2026-04-07: hard caps preferred over soft defaults for v1 — easier to relax later, impossible to tighten retroactively without breaking deployed workflows.

### Migration strategy for v2 catalogue items

When migrating remaining items (4, 6, 7, 8, 9, 10) in v2:

- Each item gets its own story, not a bulk migration
- Each story uses the resolution chain pattern from Story 9.6 unchanged
- Items 7, 8, 9, 10 require design work first — they're not just "make this configurable", they're "rethink what this engine concern actually is"
- Item 4 (MaxIterations) is mechanical and can ship in any cleanup epic

### Why this audit exists

Two reasons:

1. **It's the master checklist for v2 engine work.** When v2 planning starts, this catalogue is the input. Each item gets a yes/no/defer decision and the yeses become stories.
2. **It's the test for new engine PRs.** When future story specs propose adding a hardcoded value to the engine, the reviewer should ask "is this a safety invariant or a tuning value? if tuning, why isn't it in the catalogue with a resolution chain?" This prevents the same architectural mistake from recurring.

### Trigger to revisit

After Story 9.6 ships, do a follow-up audit pass on the codebase looking for any new constants or TimeSpan literals that crept in during beta polish work. Add them to the catalogue.

---

## ToolLoop Stall Recovery — Retry-with-Nudge Alternative

**Added:** 2026-04-07
**Context:** Story 9.0 (beta blocker) implements "fail fast" stall recovery in the ToolLoop — when the LLM produces no tool call and no text after a tool result, the step is marked errored immediately and the user retries via the chat. This was chosen for the beta because it is simpler, more debuggable, and relies on the existing retry button as the recovery path.

**Alternative deferred to V2+:** Retry-with-nudge. When a stall is detected, the engine automatically injects a system message along the lines of "Continue your task. You have not yet written the required output file." and re-invokes the LLM once. If the model stalls a second time, then fail.

**Potential benefits:**
- More forgiving for intermittent model non-responses (network blip, provider hiccup, token budget edge)
- Hides transient issues from the user without them needing to manually retry
- Could dramatically reduce surface-level flakiness on longer workflows

**Costs / risks:**
- Additional complexity in ToolLoop state machine (retry counter, nudge message construction, conversation log semantics)
- Hidden retries that the user doesn't see — makes debugging harder when things go wrong
- Wastes tokens on a nudge cycle that may not help
- Can mask genuine prompt quality issues that should be fixed at the prompt layer, not papered over at the engine layer

**Trigger to revisit:** If beta telemetry (or real-world usage) shows stalls happening more than occasionally despite the prompt-quality fixes in Story 9.1b. If stalls are rare, fail-fast remains the right call. If they become a recurring friction point, promote this to a V2 story.

**Design note:** The fail-fast implementation should keep the stall detection logic cleanly separable from the recovery action, so a future retry-with-nudge implementation is a pluggable strategy swap rather than a rewrite.

---

_This is a living document. Add thoughts freely, triage periodically, formalise when planning the next cycle._
