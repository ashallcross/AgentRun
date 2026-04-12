# Cogworks Agent Runner -- Architecture Summary
*For review by application agents*

---

## What This Document Is

This is a summary of design decisions and architectural findings for the **Cogworks Agent Runner**, an Umbraco-native multi-step agent orchestration package. It is intended to be passed to agents reviewing the runner's design for consistency, feasibility, and completeness.

---

## 1. What the Runner Is

The Cogworks Agent Runner is a **NuGet-installed Umbraco package** that provides a structured, YAML-driven workflow execution engine for multi-step AI agent workflows. It is designed for Umbraco v17+ exclusively and treats that as a baseline assumption throughout.

It is **not** a generic agent framework. It is an Umbraco-native orchestration layer with first-class knowledge of Umbraco's APIs, authentication model, data formats, and tooling ecosystem.

Its primary purpose is to allow consultancy teams and Umbraco agencies to define and run repeatable, multi-step AI workflows that interact with Umbraco CMS instances -- reading content, analysing it, producing artefacts, and optionally writing results back.

---

## 2. The Core Design Principle

The runner knows **how to talk to Umbraco** and **how to manage execution**. It does not know **why** it is talking to Umbraco, **what the agents are trying to achieve**, or **what the outputs mean**.

Concretely:

| The runner knows | The runner does not know |
|---|---|
| How to authenticate via Umbraco API users | What a site audit is |
| How to paginate the Management API | What brand governance means |
| How to normalise property value responses | What makes content "good" |
| How to manage step sequencing and phase pauses | What an upgrade impact analysis involves |
| How to store and route artefacts between steps | Whether any given artefact is correct |
| How to manage conversational turn loops | What the agent is asking the user |
| How to spawn and collect sub-agents | Why parallelisation is needed |

All Umbraco domain knowledge, workflow logic, and agent expertise lives in **workflow YAML definitions** and **agent prompt files** -- not in the runner package itself.

---

## 3. Execution Modes

The runner supports two agent execution modes. A workflow step declares which mode it uses.

### 3.1 Step Mode

The agent receives a defined input, executes once, produces a defined output artefact, and signals completion. The runner advances to the next step.

```yaml
- id: content-inventory
  type: step
  agent: content-inventory-agent
  input:
    capabilities: [umbraco.content.list, umbraco.schema.document_types]
  output:
    artefact: content-inventory.md
```

### 3.2 Conversational Mode

The agent enters a turn-based loop. The runner manages message passing between the agent and the user. The step completes when the agent emits a defined completion signal or a turn/timeout limit is reached.

```yaml
- id: discovery-interview
  type: conversation
  agent: discovery-agent
  max_turns: 20
  completion_signal: brief_approved
  output:
    artefact: project-brief.md
```

Mixed behaviour -- a conversational agent that also makes API calls mid-conversation -- is handled within the conversational mode. The agent requests capabilities mid-turn; the runner fulfils them and returns results in the next agent input. The user does not see the capability calls.

---

## 4. The Agent Contract

Every agent, regardless of mode, communicates with the runner through a uniform input/output contract. The runner validates this contract but does not inspect the content of messages or artefacts.

### Agent Input
```yaml
agent_input:
  context: {}               # Session context: credentials, config, prior state
  message: ""               # Current input: user turn or initial trigger
  artefacts: []             # Named artefacts available from prior steps
  capabilities: []          # Capabilities the runner is making available this step
  conversation_history: []  # Empty for step agents; populated for conversational
```

### Agent Output
```yaml
agent_output:
  message: ""               # Response to user (conversational) or null (step)
  artefacts: []             # New or updated artefacts produced
  signals: []               # Completion, pause, branch, spawn requests
  capability_requests: []   # fetch_url calls, sub-agent spawns, etc.
```

---

## 5. Umbraco-Native Capability Layer

The runner ships with a set of named capabilities that encapsulate Umbraco API interaction. Workflows reference these by name. The runner handles authentication, pagination, error handling, and response normalisation internally.

### Built-in Umbraco Capabilities

| Capability | Description |
|---|---|
| `umbraco.content.list` | Paginate all published content with full property expansion |
| `umbraco.content.get` | Fetch a single content node by ID or path |
| `umbraco.content.update` | Write back to a content node using schema-validated payload |
| `umbraco.content.unpublished` | Fetch draft/unpublished content via Management API |
| `umbraco.schema.document_types` | Full document type schema map including v17.4+ JSON schemas |
| `umbraco.schema.data_types` | Data type configurations and JSON schemas |
| `umbraco.schema.document_type.json_schema` | Per-document-type JSON Schema from v17.4.0 endpoint |
| `umbraco.media.list` | Media inventory with metadata and alt text presence |
| `umbraco.media.get` | Single media item detail |
| `umbraco.health.checks` | Run and return Umbraco health check results |
| `umbraco.usync.export` | Trigger uSync export and retrieve resulting files |
| `umbraco.usync.import` | Import uSync artefacts with pre-import validation |
| `umbraco.indexer.rebuild` | Rebuild specified Examine indexes |
| `umbraco.models.rebuild` | Trigger Models Builder regeneration |

### Generic Capabilities (also available)

| Capability | Description |
|---|---|
| `fetch_url` | Arbitrary authenticated HTTP request (for non-Umbraco endpoints) |
| `spawn_sub_agent` | Run a child agent with given input; return its output |
| `read_artefact` | Read a named artefact from the current session |
| `write_artefact` | Write or update a named artefact |
| `read_memory` | Read from sidecar memory for this workflow or client |
| `write_memory` | Write to sidecar memory |
| `request_user_input` | Pause execution and request a user response |

---

## 6. Authentication

The runner ships with native support for both Umbraco authentication patterns. No workflow needs to describe the auth flow -- it provides credentials only.

**Management API:** OAuth2 client credentials via API users (v15+, baseline for v17+). The runner acquires a token at session start, stores it in session context, refreshes as needed, and injects it into all Management API capability calls.

**Content Delivery API:** API key header injection. Configured per session.

Auth credentials are provided at session creation as session inputs. They are stored in session context and never written to artefacts or logs.

---

## 7. The v17.4.0 JSON Schema Endpoints

Umbraco 17.4.0 introduces JSON Schema endpoints for data types and document types:

- `GET /umbraco/management/api/v1/data-type/{id}/schema`
- `GET /umbraco/management/api/v1/document-type/{id}/schema`

These endpoints expose the exact expected structure for any property value, including constraints, formats, and nested shapes for complex editors (Block List, Media Picker, Rich Text, etc.).

The runner treats these as a baseline capability. The `umbraco.schema.document_type.json_schema` capability calls these endpoints and returns the schema to the requesting agent. This is the foundation for reliable **write-back operations** -- agents can construct valid property payloads against the schema rather than guessing internal storage formats.

This removes the need for a companion NuGet package on the target site for write-back support. The schema contract is provided by Umbraco itself.

---

## 8. Session Model

A session is the unit of execution. It is workflow-agnostic -- the runner stores and routes data but does not interpret it.

```yaml
session:
  id: uuid
  workflow: content-health-monitor   # Workflow definition reference
  variant: full-audit                # Optional variant
  status: running | paused | complete | failed
  
  context:
    # Resolved at session start from workflow config and user input
    site_url: "https://client-site.com"
    management_api_credentials: {...}
    delivery_api_key: "..."
    client_profile: {...}
    
  artefacts:
    # Named outputs from completed steps -- content opaque to runner
    content-inventory.md: "..."
    schema-map.md: "..."
    seo-audit.md: "..."
    
  conversation_state:
    # Per-step conversation history for conversational agents
    discovery-interview:
      turns: [...]
      status: complete
      
  execution_pointer:
    current_phase: 2
    current_step: quality-scorer
    status: running
```

---

## 9. Phase and Pause System

Phases group related steps. Each phase boundary has a configurable completion policy.

```yaml
phases:
  - id: ingestion
    steps: [auth-check, content-inventory, schema-map]
    on_complete: pause
    pause_message: "Content inventory complete. Review before analysis begins."
    allow_uploads: true    # User can upload additional context before resuming
    
  - id: analysis
    steps: [seo-analyst, accessibility-auditor, quality-scorer]
    on_complete: auto
    
  - id: synthesis
    steps: [recommendations-synthesiser]
    on_complete: pause
    pause_message: "Analysis complete. Review findings before generating final report."
```

The `allow_uploads` flag on a paused phase lets users inject additional context -- brand guidelines, competitor URLs, prior audit results -- before the next phase begins. The runner stores these uploads as named artefacts available to subsequent steps.

---

## 10. Workflow and Agent File Structure

```
/workflows/
  content-health-monitor/
    workflow.yaml              # Phase, step, and capability definitions
    variants/
      full-audit.yaml
      seo-focused.yaml
      
  site-audit/
    workflow.yaml
    variants/
      accessibility-focused.yaml

/agents/
  content-inventory-agent/
    system-prompt.md           # Umbraco domain knowledge lives here
    sidecar-memory.md          # Cross-session learned patterns
    
  seo-analyst-agent/
    system-prompt.md
    
/auth-profiles/
  # Not needed -- auth is handled natively by the runner
  # Custom external service auth configs live here if needed
```

The runner package itself contains none of these files. They are the workflow library that sits on top of the runner. For the managed service offering, Cogworks maintains this library. Client-specific customisations extend it without modifying it.

---

## 11. The Managed Service Opportunity

Because the runner is an Umbraco package and the workflow library is separate from it, the offering can be structured as:

**Runner package:** Installed once per Umbraco solution. Provides execution engine, Umbraco capabilities, auth handling, session management.

**Workflow library:** Maintained by Cogworks. A catalogue of Umbraco-specific workflow definitions and agent prompts. Agencies subscribe to the library and run workflows against their clients' sites.

**Variant system:** Each workflow can have source-specific variants. The Content Migration Planner has WordPress-to-Umbraco, Sitecore-to-Umbraco, and Drupal-to-Umbraco variants, each with source-specific agent prompts, without any code changes to the runner.

This positions the offering as the AI-powered delivery platform for Umbraco work -- not just a Umbraco development consultancy.

---

## 12. Known External Tools and Their Relationship to the Runner

| Tool | Relationship |
|---|---|
| **uSync.Cli** | The runner's `umbraco.usync.export` and `umbraco.usync.import` capabilities can invoke this tool when available, or call the uSync Management API endpoints directly |
| **Umbraco Developer MCP Server** | Separate product for interactive IDE use. Not used by the runner. The runner accesses the same Management API directly |
| **Umbraco Compose** | Monitored. When generally available, its GraphQL API becomes a candidate for a new capability: `umbraco.compose.query` |
| **Umbraco.AI** | The runner is provider-neutral at the LLM level. Workflows can specify which model/provider to use per step, independently of Umbraco.AI's provider abstraction |

---

## 13. Prioritised Build Order

Based on consultancy value, implementation complexity, and demonstration power:

1. **Site Audit Workflow** -- lowest effort, immediately billable, good runner proof-of-concept. Uses step mode only, Delivery API for content ingestion, no write-back.

2. **Content Model Scaffolding Workflow** -- strongest demo of the runner's value proposition. Introduces conversational mode (discovery interview phase) and uSync artefact generation.

3. **Content Migration Planner** -- highest long-term business value. Addresses the community's top pain point. Introduces the variant system (WordPress, Sitecore, Drupal source variants).

4. **Upgrade Impact Analysis** -- high value for the v13/v14 to v17 upgrade wave. Uses file upload (client's codebase) rather than live API access. No auth complexity.

5. **Content Health Monitor** -- recurring revenue model. Requires Management API access and sub-agent parallelisation for per-page scoring.

---

*Document prepared April 2026. Reflects Umbraco v17.4.0 capabilities and Cogworks Agent Runner design decisions as of this date.*
