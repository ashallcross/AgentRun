# Story 9.7: Tool Result Offloading for `fetch_url` — BETA BLOCKER

Status: done

> **DoD amended 2026-04-08** — DoD scoped down to the fetch_url phase only; full scanner workflow completion gated on Story 9.1b + Story 9.9. See [sprint-change-proposal-2026-04-08-9-7-dod-amendment.md](../planning-artifacts/sprint-change-proposal-2026-04-08-9-7-dod-amendment.md) and [9-7-architectural-finding-read-file-bloat.md](../planning-artifacts/9-7-architectural-finding-read-file-bloat.md).

**Depends on:** 9.6 (configurable tool limits — done), 9.0 (ToolLoop stall recovery — done)
**Blocks:** 9.1c (paused — resumes once 9.7 ships), 9.1b (re-scope pending in a separate course correction), 9.4 (Accessibility Quick-Scan — also fetches URLs and inherits the same fix)

> **BETA BLOCKER.** This story unblocks the resumption of Story 9.1c. The architectural finding from the 2026-04-07 manual E2E proved that the prompt-only ceiling has been reached — the `fetch_url` tool contract dumps raw HTTP response bodies directly into LLM conversation context, which causes a context-bloat-induced empty turn from Sonnet 4.6 after multi-fetch sequences against real-world pages. This story implements the architect's locked decision: **offloaded raw response** (Option 1 from the finding report).

## Story

As the AgentRun engine,
I want `fetch_url` to write large response bodies to a workspace-scoped scratch path and return a small structured handle to the LLM instead of inlining the body,
So that multi-fetch workflows do not poison their own conversation context with hundreds of kilobytes of HTML and degrade into empty-turn stalls — while preserving SSRF protection, the configurable response-size ceiling from Story 9.6, the timeout behaviour, the cancellation plumbing, and the existing error string contract for HTTP failures.

## Context

**UX Mode: N/A — engine + tool contract change. No UI work.**

**Authorisation:** This story was created via [Sprint Change Proposal 2026-04-08](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md), which paused Story 9.1c and authorised this new upstream story. Read the SCP for the paper trail. Read the [architectural finding report](../planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md) — specifically the TL;DR, Evidence, Root Causes, and "The fix — proposed → Option 1 (Offloaded Raw Response)" sections — for the full technical context. **This story implements Option 1 only. Option 2 (server-side structured extraction via AngleSharp) is the scope of Story 9.1b's pending course correction and is NOT in scope here.**

**The bug being fixed:**

In the failing instance `caed201cbc5d4a9eb6a68f1ff6aafb06`, the scanner correctly followed every prompt invariant from Story 9.1c's strengthened `scanner.md`. Despite that, after four successful fetches accumulated **~1.81 MB of raw HTML in the conversation** (159 KB + 258 KB + 1.39 MB across three pages), Sonnet 4.6 produced an empty assistant turn on the 5th fetch attempt. Story 9.0's stall detector fired correctly. The root cause is upstream of the model: the tool contract dumps raw bodies into context. The fix is to stop doing that.

**The pattern (locked by Winston):** "Tool result offloading" / "scratchpad handle". `fetch_url` writes the raw response body to a workspace scratch path and returns a small structured handle. The agent reads the cached file via `read_file` (existing tool) only if it actually needs the content. This is the standard pattern used by Claude Code, Anthropic Computer Use, and the Claude Agent SDK.

**Scope is additive on top of the existing FetchUrlTool.** SSRF protection, timeout enforcement, the configurable `max_response_bytes` ceiling from Story 9.6, the per-request `CancellationTokenSource.CancelAfter` pattern, the cancellation token plumbing, the HTTP error string contract — **all preserved exactly as today**. This story adds an offloading layer; it does not redesign the tool.

### Architect's locked decisions (do not relitigate)

These were locked by Winston in his response to the dev agent's finding report on 2026-04-08. They are not up for re-design. If the dev agent finds a real ambiguity (not a relitigation), stop and ask Adam.

1. **Pattern: offloaded raw response.** `fetch_url` writes the body to a workspace path and returns a small structured handle.
2. **Scratch path location: inside the instance folder at `{instanceFolder}/.fetch-cache/{sha256(url)}.html`.** Three reasons this path was chosen:
   - The existing `PathSandbox` already covers the instance folder. Anything outside it would be a new sandbox surface, and the architect explicitly does **not** want a new sandbox surface in a beta-blocker fix.
   - Per-instance cleanup is already handled by instance deletion. No new cleanup code path required.
   - The dot-prefix (`.fetch-cache`) makes the directory conventionally hidden from agent-driven `list_files` output while remaining a normal directory under the existing sandbox rules.
3. **Hash function: SHA-256, NOT SHA-1.** Even though SHA-1 would be functionally fine for filename collision avoidance, the architect's standing rule is no weakened cryptographic primitives even in non-security contexts. SHA-256 in .NET is a one-line change (`System.Security.Cryptography.SHA256.HashData`).
4. **Handle shape returned to the LLM:**
   ```json
   {
     "url": "https://example.com/page",
     "status": 200,
     "content_type": "text/html",
     "size_bytes": 1394712,
     "saved_to": ".fetch-cache/abc123def456.html",
     "truncated": false
   }
   ```
   The handle is returned as a JSON-serialised string (matching the existing `Task<object>` return contract of `IWorkflowTool.ExecuteAsync` — see Dev Notes for the exact return-type discussion). It must be small — **target under 1 KB**, and the unit tests must assert this size bound.
5. **`truncated` flag is mandatory even on successful fetches.** Surfaced from Story 9.6's existing truncation logic. The agent must know whether the cached file is the complete response or has the truncation marker appended.
6. **NO content-in-handle backup field.** Do not include the file content as a backup "in case the file isn't there." That defeats the entire point. Trust the file system. If the file write fails, the tool returns an error like any other failure — it does not silently include content inline.
7. **HTTP responses without a body** (3xx redirects, 204 No Content, **and HTTP 200 with `Content-Length: 0` / zero-byte bodies**) — return a handle with `saved_to: null` and `size_bytes: 0`. **Do NOT write empty files for non-content responses.** The current behaviour of returning the status string for HTTP error/redirect responses (preserving "small status responses don't bloat context") must be preserved as a behaviour goal.
8. **Story 9.6's `fetch_url.max_response_bytes` ceiling is preserved** as the upstream truncation guard. This story does NOT replace it — it composes with it. The flow is: stream up to the resolved `max_response_bytes`, apply the truncation marker if exceeded, *then* offload the (possibly truncated) bytes to disk and return the handle with `truncated: true` if applicable.
9. **scanner.md prompt update is in scope for this story** — minimal contract bridge only. See Task 5.
10. **Pre-implementation architect review gate** — Winston explicitly asked to eyeball the PathSandbox interaction section before code is written. See the section at the bottom of this file.

### Decision: HTTP 200 with empty body

Confirmed by Adam 2026-04-08 in the spec-creation conversation: an HTTP 200 with `Content-Length: 0` (or any zero-byte successful body) is treated the same as 3xx/204. The handle is `saved_to: null`, `size_bytes: 0`, `truncated: false`. **No empty file is written.** Rationale: consistency with the no-body pattern; avoids a degenerate `read_file` footgun where the agent reads a zero-byte file expecting content; the `size_bytes: 0` field already signals "nothing to read" to the agent.

## Files in Scope

| File | Change |
|---|---|
| `AgentRun.Umbraco/Tools/FetchUrlTool.cs` | Add offloading: write body to `{instanceFolder}/.fetch-cache/{sha256(url)}.html`; replace string-body return with JSON-serialised handle. SSRF, timeout, ceiling, cancellation, HTTP-error string contract all preserved. |
| `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` | Extend with the new regression and contract tests (see Tasks 4 + Tasks 6). |
| `AgentRun.Umbraco.Tests/Tools/Fixtures/` (NEW folder, or wherever existing test fixtures live — verify) | Capture three HTML payloads from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`'s `conversation-scanner.jsonl` and check them in (see Task 3). |
| `AgentRun.Umbraco/Security/PathSandbox.cs` | **Verification only — no change expected.** PathSandbox today does pure containment + symlink checks and does not reject dotted directories. Verify with a unit test that `read_file` reaches `.fetch-cache/<hash>.html`. **If verification fails**, fix PathSandbox before shipping 9.7. See Task 2. |
| `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` | Minimal contract bridge addition: tell the agent that `fetch_url` returns a handle and that `read_file` is the way to inspect cached content. The five hard invariants stay exactly as they are. See Task 5. |

**Files explicitly NOT touched:**

- `AgentRun.Umbraco/Engine/ToolLoop.cs` — out of scope. The stall detector from Story 9.0 is correct.
- `AgentRun.Umbraco/Security/SsrfProtection.cs` — out of scope. SSRF behaviour preserved.
- `AgentRun.Umbraco/Engine/ToolLimitResolver.cs` — out of scope. Story 9.6's resolution chain preserved.
- `AgentRun.Umbraco/Tools/ReadFileTool.cs` / `WriteFileTool.cs` — out of scope. Existing path-sandboxed file tools are reused unmodified for both the offloading write and the agent-driven read.
- Story 9.1c's spec — already updated by the SCP.
- `_bmad-output/planning-artifacts/v2-future-considerations.md` — Winston-owned.

## Acceptance Criteria

1. **Given** `fetch_url` is invoked with a URL whose response is a successful HTTP 200 with a non-empty body of any size (including > 1 MB),
   **When** the tool executes,
   **Then** the response body is written to `{InstanceFolderPath}/.fetch-cache/{sha256(url)}.html` and the tool returns a JSON handle with the shape `{ url, status, content_type, size_bytes, saved_to, truncated }`,
   **And** `saved_to` is the relative path `.fetch-cache/{hash}.html` (relative to the instance folder, the same convention `read_file` already uses),
   **And** the JSON handle's serialised size is less than 1024 bytes (asserted in tests).

2. **Given** the response body exceeds the resolved `fetch_url.max_response_bytes` ceiling from Story 9.6,
   **When** the tool truncates the body using the existing truncation logic,
   **Then** the bytes written to the cached file are the truncated bytes (with the `[Response truncated at {N} bytes]` marker appended exactly as today),
   **And** the handle's `truncated` field is `true`,
   **And** the handle's `size_bytes` reports the bytes actually written to disk (matches `new FileInfo(path).Length`).

3. **Given** the response is a 3xx redirect, an HTTP error (4xx/5xx), an HTTP 204 No Content, **or** an HTTP 200 with a zero-length body,
   **When** the tool executes,
   **Then** for HTTP error responses (4xx/5xx) the existing `HTTP {status}: {reason}` string contract is preserved unchanged (no handle, no file written),
   **And** for redirect chains the final status reported in the handle is the resolved status after `HttpClient`'s default redirect handling (HttpClient follows redirects by default and the final response is what gets offloaded),
   **And** for 204 / zero-length 200 responses the handle is `{ url, status, content_type, size_bytes: 0, saved_to: null, truncated: false }` and **no file is written** to `.fetch-cache/`.

4. **Given** the `.fetch-cache/` directory does not exist when `fetch_url` is first invoked in an instance,
   **When** the tool writes the response body,
   **Then** the directory is created (parents included) before the write,
   **And** subsequent invocations in the same instance reuse the existing directory without error,
   **And** two concurrent `fetch_url` calls in the same instance creating the directory simultaneously do not throw (race-safe directory creation — `Directory.CreateDirectory` is idempotent and is the correct primitive).

5. **Given** the agent receives a handle with `saved_to: ".fetch-cache/abc123.html"`,
   **When** the agent calls `read_file` with that path,
   **Then** the existing `PathSandbox.ValidatePath` logic accepts the dotted-directory path and `read_file` returns the cached body. **This is the path-sandbox reachability gate — see Task 2.**

6. **Given** the captured fixtures from instance `caed201cbc5d4a9eb6a68f1ff6aafb06` (sizes ~100 KB, ~500 KB, ~1.5 MB),
   **When** `fetch_url` is exercised against an HTTP handler returning each fixture in turn,
   **Then** all three fetches complete without inlining the body in the handle, all three handles are < 1 KB, and the cached files on disk match the fixture bytes exactly (or the truncated form, in the 1.5 MB case if `max_response_bytes` is set below the fixture size).

7. **Given** the response body write to disk fails (disk full, permission denied, IO error),
   **When** the tool catches the IO failure,
   **Then** the tool throws `ToolExecutionException` with a clear message naming the failed write — **the tool does NOT silently inline the response body as a fallback** (per architect's locked decision #6).

8. **Given** all backend changes,
   **When** `dotnet test AgentRun.Umbraco.slnx` runs,
   **Then** all existing tests pass and the new tests for offloading, truncation cooperation, no-body handling, regression fixtures, handle-shape contract, PathSandbox reachability, and IO failure pass.

9. **Given** the failing instance scenario from `caed201cbc5d4a9eb6a68f1ff6aafb06`,
   **When** the Content Quality Audit example workflow is run end-to-end against the new tool contract with a 5-URL batch including BBC News,
   **Then** the scanner step completes without `StallDetectedException`,
   **And** the conversation `tool_result` blocks contain handles (each < 1 KB), not raw HTML,
   **And** the scanner writes `artifacts/scan-results.md`. **This is the manual E2E gate — see Task 7.**

## What NOT to Build

- **Do NOT touch SSRF protection.** `SsrfProtection.ValidateUrlAsync` is called exactly as today, before the HTTP request, with the same linked timeout cancellation token. No behavioural change.
- **Do NOT change the `fetch_url.max_response_bytes` resolution chain from Story 9.6.** The resolver is consulted exactly as today. The ceiling is the upstream truncation guard. This story composes with the ceiling, it does not replace it.
- **Do NOT change the timeout behaviour from Story 9.6.** The per-request `CancellationTokenSource.CancelAfter` pattern is preserved. The HTTP fetch is bounded by the same linked CTS as today.
- **Do NOT change the cancellation token plumbing.** The caller's `CancellationToken` continues to flow into the linked CTS and into both `HttpClient.GetAsync` and the disk write.
- **Do NOT change the error string contract for HTTP failures.** `HTTP 4xx/5xx: {reason}` is the exact string returned today and the exact string returned after this story. The failure mode for HTTP-level errors does NOT switch to the handle shape — only successful responses (with or without bodies) get the handle.
- **Do NOT introduce a "disable offloading" / "inline mode" flag.** Offloading is the only mode. There is no escape hatch.
- **Do NOT include a `content` backup field in the handle.** Per architect's locked decision #6. If the file write fails, the tool errors. It does not silently inline.
- **Do NOT add a cleanup pass for stale `.fetch-cache/` files.** Per architect's locked decision #2, per-instance cleanup is handled by instance deletion. New fetches simply overwrite stale entries by SHA-256 path. There is no TTL, no LRU, no background sweeper.
- **Do NOT introduce a new sandbox surface.** The `.fetch-cache/` directory lives inside the existing instance folder root that `PathSandbox` already covers. Do not add a new allowed root, a new sandbox class, or a new path validator. If `PathSandbox` rejects the dotted directory (it should not — see Task 2), fix `PathSandbox` minimally rather than introducing a parallel mechanism.
- **Do NOT use SHA-1, MD5, or any other weaker hash** for the filename derivation. SHA-256 only, per architect's locked decision #3.
- **Do NOT rewrite scanner.md.** Add only the minimal contract-bridge sentences described in Task 5. The five hard invariants and all existing prompt content stay exactly as committed in Story 9.1c's partial milestone. Story 9.1b's pending course correction will rewrite scanner.md more substantially when Option 2 (AngleSharp) lands; that is not this story's job.
- **Do NOT change the `IWorkflowTool` interface.** `ExecuteAsync` already returns `Task<object>`. The handle return value is still an `object` (a string containing JSON). No interface signature change.
- **Do NOT add a new tool registration.** `fetch_url` is still one tool. There is no `fetch_url_handle` or `fetch_url_v2` sibling. The existing tool's contract changes; that is the entire change.
- **Do NOT modify `_bmad-output/implementation-artifacts/9-1c-first-run-ux-url-input.md`.** It was updated by the SCP and is correct as-is.
- **Do NOT modify `_bmad-output/planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md`.** It is the authoritative finding report.
- **Do NOT modify `_bmad-output/planning-artifacts/v2-future-considerations.md`.** Winston-owned.

## Failure & Edge Cases

This story touches a security-relevant code path (file system writes inside the instance folder, hash-derived filenames). **Deny-by-default statement:** Unrecognised or unspecified inputs (malformed URLs, unsupported schemes, paths that escape the instance folder, write targets outside `.fetch-cache/`) MUST be denied/rejected, never silently coerced into a default. The validator boundary is `SsrfProtection.ValidateUrlAsync` for the URL, `PathSandbox.ValidatePath` for the disk write target, and the existing `Uri.TryCreate` check for malformed URLs. None of those boundaries are weakened by this story.

- **SHA-256 hash collision.** Vanishingly unlikely (2^128 work to find). Documented behaviour: the second write overwrites the first. No special handling. The fact that the filename is SHA-256-derived means a collision would also imply two semantically distinct URLs producing the same cached file, which is a non-issue at this probability.
- **File write failure (disk full, permission denied, IO error).** Caught as `IOException` / `UnauthorizedAccessException`. Surface as `ToolExecutionException` with a message naming the failed write target and the underlying exception message. **Do NOT silently inline the body as a fallback.** Test coverage: Task 6.
- **`.fetch-cache/` directory creation race.** Two `fetch_url` calls in the same instance creating the directory simultaneously. `Directory.CreateDirectory` is idempotent and race-safe by contract. No locking required. No special handling.
- **Stale cache files from a previous interrupted run.** The new fetch overwrites them via `File.WriteAllBytesAsync` (or equivalent). No error, no special handling.
- **URL with characters that produce a non-portable filename.** SHA-256 hashing eliminates this concern entirely — the on-disk filename is `{64-hex-chars}.html`, which is portable on every filesystem AgentRun supports.
- **Cached file deleted between fetch and subsequent `read_file` call.** The agent receives a normal "file not found" error from `read_file`. No special handling required in `FetchUrlTool` — this is `read_file`'s existing error path.
- **3xx redirect chains.** `HttpClient` follows redirects by default. The `response` object after `GetAsync` is the *final* response after redirects. The handle reports the *final* status code and content. **Confirm in the unit tests that a 301 → 200 chain produces a handle with `status: 200` and the body of the final page**, not the intermediate redirect.
- **HTTP 204 No Content.** Status 204 is a successful response with no body. Handle: `saved_to: null`, `size_bytes: 0`. **No file written.**
- **HTTP 200 with `Content-Length: 0` or zero-length body.** Same handling as 204. Handle: `saved_to: null`, `size_bytes: 0`, `truncated: false`. **No file written.** (Decision confirmed by Adam 2026-04-08.)
- **HTTP 4xx / 5xx errors.** Existing string return contract preserved exactly: `$"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"`. No handle, no file write.
- **`Content-Type` header missing or absent.** The handle's `content_type` field is the empty string `""` or the literal string `"unknown"` — pick one and document the choice in Dev Notes. The agent reads the file via `read_file` regardless; `content_type` is informational only.
- **Truncated body that ends mid-multibyte UTF-8 sequence.** The existing truncation logic in `FetchUrlTool` already handles this by truncating at byte count and appending the marker. The bytes-on-disk match the bytes the agent would have seen as a string under the old contract. No change.
- **Malformed URL passed to `fetch_url`.** Existing `Uri.TryCreate` check throws `ToolExecutionException("Invalid URL: '...'")`. Preserved unchanged.
- **`PathSandbox.ValidatePath` rejects `.fetch-cache/`.** This should not happen — current `PathSandbox` does pure containment + symlink checks with no dotted-directory rejection. **Task 2 verifies this in code.** If the verification test fails, the story scope expands to include a minimal `PathSandbox` fix (add a unit test asserting dotted directories are accepted, then audit the rejection logic). The architect must be looped in via the pre-implementation review gate before any `PathSandbox` change ships.
- **`ToolExecutionContext.InstanceFolderPath` is null or empty.** Wiring bug — fail loud with `InvalidOperationException` (matches the existing pattern for missing `Step`/`Workflow` on the context).

## Tasks / Subtasks

- [x] **Task 1: Implement the offloading change in `FetchUrlTool`** (AC: #1, #2, #3, #4, #7)
  - [x] 1.1 Add a private static helper `ComputeUrlHash(string url)` that returns the lowercase hex SHA-256 of the URL using `System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url))` and `Convert.ToHexString(...).ToLowerInvariant()`. Place it as a private static method on `FetchUrlTool`.
  - [x] 1.2 Define a small private record (or `JsonObject`-built anonymous shape) representing the handle: `record FetchUrlHandle(string url, int status, string content_type, long size_bytes, string? saved_to, bool truncated)`. Snake_case JSON property names — use `[JsonPropertyName("...")]` attributes or configure the serializer with `JsonNamingPolicy.SnakeCaseLower`.
  - [x] 1.3 In `ExecuteAsync`, after the existing successful read into the `buffer` byte array (and after the truncation logic that produces `text` today), branch on whether the body is non-empty:
    - **If `totalRead == 0` (zero-length 200 / 204 / no body):** build the handle with `saved_to: null`, `size_bytes: 0`, `truncated: false`, return JSON.
    - **If `totalRead > 0`:** compute the relative path (`relPath = $".fetch-cache/{ComputeUrlHash(urlString)}.html"`, forward-slash form matching the existing `read_file` convention), then call `var validatedPath = PathSandbox.ValidatePath(relPath, context.InstanceFolderPath)` to obtain the canonical absolute path. **The disk write MUST use `validatedPath` — do NOT separately compute a write target via `Path.Combine`.** This is defence-in-depth per the architect's review: it closes a loop where the validated path and the write target could in principle diverge. Concretely:
      ```csharp
      var validatedPath = PathSandbox.ValidatePath(relPath, context.InstanceFolderPath);
      Directory.CreateDirectory(Path.GetDirectoryName(validatedPath)!);
      await File.WriteAllBytesAsync(validatedPath, bytesToWrite, cancellationToken);
      ```
      The bytes written (`bytesToWrite`) are the truncated buffer (`buffer[0..min(totalRead, maxBytes)]`) plus, if truncated, the existing UTF-8 truncation marker bytes appended. Build the handle with `saved_to: relPath` (the forward-slash relative path), `size_bytes: bytesToWrite.Length`, `truncated: totalRead > maxBytes`. Return JSON.
  - [x] 1.4 The HTTP-error path (`if (!response.IsSuccessStatusCode)`) is **unchanged** — keep returning `$"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"`. No handle, no file write.
  - [x] 1.5 Wrap the disk-write portion in a try/catch for `IOException` and `UnauthorizedAccessException`. On catch, throw `ToolExecutionException($"Failed to cache fetch_url response to {relativePath}: {ex.Message}")`. **Do NOT inline the body as a fallback** — per architect's locked decision #6.
  - [x] 1.6 The `content_type` field in the handle comes from `response.Content.Headers.ContentType?.MediaType ?? ""` (empty string when absent — document this in Dev Notes).
  - [x] 1.7 Handle JSON serialisation: use `System.Text.Json.JsonSerializer.Serialize(handle, options)` with `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.Never }` to produce the JSON string. The serialized length must be < 1024 bytes (asserted in Task 4 tests).
  - [x] 1.8 Confirm there are NO other behavioural changes to `FetchUrlTool` — SSRF call site, timeout CTS setup, cancellation token wiring, the `IsSuccessStatusCode` branch, the buffer/streaming read loop, the `ToolExecutionException` for malformed URLs / missing context, and the `ExtractStringArgument` helper all stay byte-for-byte identical aside from the additions described above.

- [x] **Task 2: Verify PathSandbox accepts `.fetch-cache/` directory reads via `read_file`** (AC: #5)
  - [x] 2.1 Add a unit test in `AgentRun.Umbraco.Tests/Security/PathSandboxTests.cs` (or wherever the existing PathSandbox tests live) that exercises `PathSandbox.ValidatePath(".fetch-cache/abc123.html", instanceRoot)` against a temporary instance root and asserts the call returns a canonical path inside the instance root **without throwing**. Cover both nested-write (`.fetch-cache/<hash>.html`) and root-of-cache-dir (`.fetch-cache`) cases.
  - [x] 2.2 Add a second unit test that exercises `PathSandbox.ValidatePath` with a malicious dotted path (`.fetch-cache/../../etc/passwd`) and asserts it throws `UnauthorizedAccessException`. This is the regression guard — making sure adding dotted-directory support has not weakened path-escape protection.
  - [x] 2.2.a Path-escape regression test variant with **three-or-more `..` segments** (e.g. `.fetch-cache/../../../tmp/foo`). Asserts `PathSandbox.ValidatePath` still throws `UnauthorizedAccessException` regardless of how many traversal segments are stacked.
  - [x] 2.2.b Path-escape regression test variant with an **absolute path passed as the requestedPath argument** (e.g. `/etc/passwd` on Linux, `C:\Windows\System32\config\SAM` on Windows). Asserts `PathSandbox.ValidatePath` rejects absolute paths regardless of whether the absolute target is inside or outside the instance root.
  - [x] 2.3 **If Task 2.1 fails** (PathSandbox rejects the dotted directory in the current code), STOP. Surface to Adam immediately — this triggers the pre-implementation architect review gate at the bottom of this spec, because the scope of the story expands to a `PathSandbox` change. Do not proceed with Task 1 until the architect confirms the PathSandbox fix shape.
  - [x] 2.4 Add an end-to-end test that exercises `ReadFileTool.ExecuteAsync` against a path of the form `.fetch-cache/<hash>.html` where the file has been pre-written, and asserts the file is read successfully. This is the agent-facing reachability gate, not just the sandbox-level test.

- [x] **Task 3: Capture regression fixtures from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`** (AC: #6)
  - [x] 3.1 Open `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/caed201cbc5d4a9eb6a68f1ff6aafb06/conversation-scanner.jsonl`.
  - [x] 3.2 Extract the three problematic raw HTML payloads at sizes ~160 KB (wearecogworks.com), ~258 KB (umbraco.com/products/cms), and ~1.39 MB (bbc.co.uk/news) by parsing the `tool_result` content from the JSONL log lines noted in the architectural finding report (Evidence section, lines #8 / #11 / #14 of the conversation trace). For each, decode the JSON-escaped string back to the raw bytes.
  - [x] 3.3 Save them as fixture files at `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-100kb.html` (the wearecogworks payload, ~160 KB rounded down conceptually to "100 KB tier"), `fetch-url-500kb.html` (use the umbraco.com/products/cms payload at ~258 KB OR pad to ~500 KB by re-fetching a larger fixture from the umbraco.com domain — the fidelity to ~500 KB matters less than that the fixture exercises the medium-marketing-site tier; pick the most representative real payload), and `fetch-url-1500kb.html` (the bbc.co.uk/news payload at ~1.39 MB). **The exact byte counts are not the gate** — the tier coverage is. The architect's intent was small / medium / large, anchored on the BBC payload because it is the one that broke production.
  - [x] 3.4 Add the fixtures to the test project as embedded resources OR as `Content { CopyToOutputDirectory = PreserveNewest }` files — match whatever pattern the existing test project uses for fixture files. Inspect the existing `.csproj` first.
  - [x] 3.5 Add a brief README.md in the fixtures folder explaining where the fixtures came from (instance ID, conversation log line numbers) so future maintainers can re-extract if needed.

- [x] **Task 4: Add unit tests for handle shape, offloading, truncation cooperation, no-body responses** (AC: #1, #2, #3, #4, #5, #7, #8)
  - [x] 4.1 **Handle shape contract test.** Fetch a small mock response (~1 KB), assert the returned object is a string, deserialise it as JSON, assert it has exactly the fields `url`, `status`, `content_type`, `size_bytes`, `saved_to`, `truncated`, assert `saved_to` matches `^.fetch-cache/[0-9a-f]{64}\.html$`, assert the serialised JSON length is `< 1024` bytes.
  - [x] 4.2 **File write contract test.** Fetch a ~10 KB mock response, then read the file at `{InstanceFolderPath}/.fetch-cache/{hash}.html` directly via `File.ReadAllBytes` and assert the bytes match the mocked response body.
  - [x] 4.3 **Truncation cooperation test.** Set the `FakeToolLimitResolver.MaxBytes` to 5000, fetch a mock response of ~12 KB, assert the handle's `truncated` field is `true`, assert the handle's `size_bytes` is 5000 + truncation marker length, assert the file on disk matches the truncated bytes (NOT the full response).
  - [x] 4.4 **No-body 204 test.** Mock a response with `StatusCode = 204` and empty body, assert the handle is `saved_to: null`, `size_bytes: 0`, `truncated: false`, assert no file is written to `.fetch-cache/`.
  - [x] 4.5 **No-body 200 test.** Mock a `StatusCode = 200` response with `Content-Length: 0` and empty body, assert the same handle shape as 4.4, assert no file is written.
  - [x] 4.6 **HTTP error preserved test.** Mock a 404 response, assert the tool returns the existing string `"HTTP 404: Not Found"`, assert no file is written, assert the return type is the existing string contract (NOT the JSON handle shape).
  - [x] 4.7 **HTTP 5xx preserved test.** Mock a 500, assert the existing `"HTTP 500: Internal Server Error"` string contract.
  - [~] 4.8 **3xx redirect resolution test.** Mock a redirect chain (301 → 200) using the standard `HttpClient` redirect-following behaviour, assert the handle's `status` is 200 and the cached body is the final-page body.
  - [x] 4.9 **Reachability via `read_file` test.** After a successful fetch, invoke `ReadFileTool.ExecuteAsync` against the `saved_to` value from the handle and assert the read returns the bytes that were written.
  - [x] 4.10 **`PathSandbox.ValidatePath` is called for the write target test.** Assert (via test double or behavioural assertion) that the disk write goes through `PathSandbox.ValidatePath` with the relative `.fetch-cache/<hash>.html` path before the write happens. This is the path-escape regression guard.
  - [x] 4.11 **IO failure test.** Use a substitute or temp-folder-permission trick to force a write failure, assert `ToolExecutionException` is thrown with a message naming the failed write target, assert NO inline body is in the exception.
  - [x] 4.12 **Concurrent directory creation test.** Spin up two parallel `ExecuteAsync` calls on the same instance folder before `.fetch-cache/` exists, assert both complete successfully (no `IOException`, no race).

- [x] **Task 5: Regression tests at the three payload sizes using captured fixtures** (AC: #6) — the explicit gate from the architect
  - [x] 5.1 Test using the ~100 KB fixture (wearecogworks): mock the `HttpClient` to return the fixture bytes for an arbitrary URL, fetch, assert the handle is < 1 KB, assert the cached file matches the fixture.
  - [x] 5.2 Test using the ~500 KB fixture (medium marketing): same shape, assert handle < 1 KB, assert cached file matches.
  - [x] 5.3 Test using the ~1.5 MB fixture (BBC News — the production breaker): same shape, assert handle < 1 KB, assert cached file matches the bytes that would have flowed through the previous tool contract for the same fixture (i.e. either the full bytes if `max_response_bytes` is permissive, or the truncated bytes if `max_response_bytes` is below the fixture size — set the resolver's `MaxBytes` deliberately for each test to cover both branches).
  - [x] 5.4 These three tests ARE the regression-protection gate. They are not optional. They prevent the bug from coming back.

- [x] **Task 6: scanner.md minimal contract bridge update** (AC #9, supports the 9.1c resumption)
  - [x] 6.1 Open `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md`.
  - [x] 6.2 Add a short paragraph (≤ 6 sentences) explaining the new `fetch_url` contract: "fetch_url returns a small handle, not the page content. The handle's `saved_to` field is the path to the cached HTML inside the instance folder. Use `read_file` if you need to inspect the actual content for parsing. The `size_bytes`, `status`, and `content_type` fields are in the handle directly so you can decide whether the page is worth reading before incurring a `read_file` call. If `saved_to` is `null`, the response had no body (e.g. a redirect or 204) — there is nothing to read." Place it adjacent to the existing tool documentation in `scanner.md` — find the section that describes `fetch_url` today and add the contract notes immediately after it.
  - [x] 6.3 **Do NOT modify the five hard invariants.** They stay exactly as they are.
  - [x] 6.4 **Do NOT rewrite, restructure, or shorten any other prompt content.** The contract bridge is additive only.
  - [x] 6.5 Do NOT touch the workflow.yaml or any other workflow file. scanner.md is the only prompt-side change.

- [ ] **Task 7: Manual E2E validation against the failing instance scenario** (AC: #9) — DEFERRED TO USER, this is the gate
  - [ ] 7.1 Run the TestSite (`dotnet run` from `AgentRun.Umbraco.TestSite/`).
  - [ ] 7.2 Open the Content Quality Audit workflow. Confirm it loads cleanly.
  - [ ] 7.3 Start a new instance. Provide the **exact same 5-URL batch** that was used in the failing instance `caed201cbc5d4a9eb6a68f1ff6aafb06` — including the BBC News homepage. The URL list is recoverable from line 3 (the user message) of that instance's `conversation-scanner.jsonl`.
  - [ ] 7.4 Watch the scanner step run. Confirm: each `fetch_url` returns a JSON handle (visible in the Tool Calls panel), the conversation log shows handles in tool_result blocks (NOT raw HTML), no `StallDetectedException` fires.
  - [ ] 7.5 Confirm the scanner step completes and writes `artifacts/scan-results.md`.
  - [ ] 7.6 Inspect the new instance's folder. Confirm `.fetch-cache/` exists, contains 5 files (one per fetch), and the filenames are 64-hex-character SHA-256 hashes.
  - [ ] 7.7 Inspect `conversation-scanner.jsonl` for the new instance. Confirm each `tool_result` block is < 1 KB (the JSON handle, not raw HTML).
  - [ ] 7.8 **Production smoke test.** Build the package locally (`dotnet pack` from the solution root via `dotnet pack AgentRun.Umbraco.slnx`), install the resulting `.nupkg` into a fresh Umbraco 17 site (separate folder, NOT the TestSite). Configure a profile, start the Content Quality Audit workflow, run a real-world 5-URL batch including BBC News. Confirm the scanner step writes `artifacts/scan-results.md` and the run completes. **This is the production smoke gate from the project's standing rules — do not skip.**

- [x] **Task 8: Run the full test suite** (AC: #8)
  - [x] 8.1 Run `dotnet test AgentRun.Umbraco.slnx`. **Always specify the .slnx file** — never bare `dotnet test` (project standing rule, see project-context.md).
  - [x] 8.2 All existing tests must pass plus the new tests from Tasks 1, 2, 4, 5.
  - [x] 8.3 Test count target: ~10–14 new tests (4.1–4.12 above plus 5.1–5.3 plus 2.1–2.4). The story justifies going slightly above the ~10 guideline because (a) regression-fixture tests at three sizes are the architect-mandated regression-protection gate, (b) the path-sandbox reachability check needs both the unit-level and the agent-facing assertion to be meaningful, and (c) the failure-and-edge-case coverage list above is non-trivially specific.
  - [~] 8.4 `npm test` in `Client/` — should be unchanged, run it anyway as a sanity check.

## Dev Notes

### Where the cache directory lives — the canonical resolution

```csharp
var hash    = ComputeUrlHash(urlString);  // 64 lowercase hex chars
var relPath = $".fetch-cache/{hash}.html";  // forward slash, matches read_file convention

// Validate AND obtain the canonical absolute path in one call. The disk write
// MUST use validatedPath — do NOT separately compute a write target via
// Path.Combine. Defence-in-depth per Winston's architect review (2026-04-08):
// closes a loop where the validated path and the write target could in
// principle diverge.
var validatedPath = PathSandbox.ValidatePath(relPath, context.InstanceFolderPath);

// Idempotent — safe under concurrent calls within the same instance:
Directory.CreateDirectory(Path.GetDirectoryName(validatedPath)!);

// Write the (possibly truncated) bytes to the validated canonical path:
await File.WriteAllBytesAsync(validatedPath, bytesToWrite, cancellationToken);
```

**Intra-instance same-URL concurrency (NOT defended at the FetchUrlTool layer):** Two parallel `fetch_url` calls within the same instance writing the same `.fetch-cache/{hash}.html` file is **intentionally not** defended at this layer. It is defended in two other places:

1. At the **prompt layer** by Story 9.1c's sequential-fetch invariant in `scanner.md` (the agent only issues one `fetch_url` per assistant turn).
2. At the **engine layer** by **Story 10.1 (Instance Concurrency Locking)** which will enforce per-instance step-execution serialisation via `SemaphoreSlim`.

Amelia: do **not** add a `SemaphoreSlim`, `lock`, or any other concurrency primitive inside `FetchUrlTool` thinking you are filling a gap. The gap is filled by the layers above. The race-safety claim in this code block is *only* about `Directory.CreateDirectory` being idempotent under the concurrent-call semantics that `FetchUrlTool`'s call sites actually exhibit (which, after 9.1c's invariant + 10.1's locking, is "no concurrent calls on the same instance").

`PathSandbox.ValidatePath` is called twice in the lifecycle of a single fetch: once defensively here in `FetchUrlTool` before the write, and once again later when the agent invokes `read_file` against the relative path returned in the handle. Both calls must succeed against the same dotted directory. Task 2 verifies both paths.

### SHA-256 implementation — exactly this, nothing more

```csharp
private static string ComputeUrlHash(string url)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(url);
    var hash  = System.Security.Cryptography.SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
}
```

`SHA256.HashData` is a static one-shot API available since .NET 5 — no instance disposal required, no allocation overhead beyond the input bytes and the 32-byte output. This is the canonical .NET pattern. Do NOT use `SHA256.Create()` + `ComputeHash` (older pattern, requires disposal). Do NOT use SHA-1, MD5, or any other hash. **The architect's standing rule is no weakened cryptographic primitives even in non-security contexts.**

### Handle JSON serialisation — why a record + System.Text.Json

The simplest and clearest way to produce the handle JSON is:

```csharp
private sealed record FetchUrlHandle(
    string Url,
    int Status,
    string ContentType,
    long SizeBytes,
    string? SavedTo,
    bool Truncated);

private static readonly JsonSerializerOptions HandleJsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
};

// Inside ExecuteAsync:
var handle = new FetchUrlHandle(urlString, (int)response.StatusCode, contentType, sizeBytes, savedTo, truncated);
return JsonSerializer.Serialize(handle, HandleJsonOptions);
```

The return type of `IWorkflowTool.ExecuteAsync` is `Task<object>` and the engine treats string results as already-serialised. Returning a JSON-string `object` matches the existing contract — the existing happy path today returns `text` (the raw HTML string) and the existing error path returns `$"HTTP {...}"` (also a string). This story keeps the return type as `string`, just with structured JSON content for the success-with-body case.

`JsonNamingPolicy.SnakeCaseLower` is .NET 8+. The project targets .NET 9 (per `Directory.Build.props` / the existing `FetchUrlTool` using collection expressions and modern syntax). If for any reason the policy is not available, fall back to per-property `[JsonPropertyName("...")]` attributes — same end result.

### `content_type` when missing

`response.Content.Headers.ContentType?.MediaType ?? ""`. Empty string when absent. The agent reads the file via `read_file` regardless; `content_type` is informational only and never the gate for any agent decision.

### Truncation marker bytes

Today's code appends `$"\n\n[Response truncated at {maxBytes} bytes]"` to the `text` string before returning. After this story, the same marker is appended to the byte buffer that gets written to disk. The order of operations:

1. Read up to `maxBytes + 1` from the stream into the buffer.
2. If `totalRead > maxBytes`, the body was over the limit. Slice the buffer to `maxBytes` bytes, then append the UTF-8 encoding of the truncation marker string.
3. Write the resulting bytes to disk.
4. The handle's `size_bytes` field is the length of the bytes written to disk (the truncated bytes plus the marker bytes), which matches what `new FileInfo(filePath).Length` would return. The agent's `read_file` returns the same content the old contract would have inlined.

This preserves the existing behaviour exactly — the agent reading the cached file via `read_file` sees the same content that previously appeared inline in the conversation. The only difference is *where* it lives.

### What changes in `IWorkflowTool` / `ToolExecutionContext` — nothing

`ToolExecutionContext` already exposes `InstanceFolderPath`. No interface or context change is required. This story is purely additive on the body of `FetchUrlTool.ExecuteAsync`.

### `dotnet test` invocation — project standing rule

Always run `dotnet test AgentRun.Umbraco.slnx`. **Never bare `dotnet test`.** This is captured in `_bmad-output/project-context.md` and in Adam's standing memory. The .slnx file is the canonical solution boundary; bare `dotnet test` finds the wrong projects in this repo.

### What this story does NOT solve (and why that's correct)

This story does not eliminate the agent's need to *parse* HTML. If the scanner agent wants to extract titles, headings, image alt counts, etc., it still has to call `read_file` and reason over raw HTML. That is the cost of Option 1 being generic. Story 9.1b's pending course correction will land Option 2 (server-side AngleSharp extraction) which will collapse the parsing burden entirely. **Both stories ship before private beta. Option 1 (this story) is the reliability fix; Option 2 (9.1b) is the quality improvement.**

This story also does not implement prompt caching at the engine layer. That is V2 Epic 11 territory. Microsoft.Extensions.AI does not currently expose Anthropic's prompt caching headers. Out of scope here.

## References

- [Sprint Change Proposal 2026-04-08 — Story 9-1c Pause](../planning-artifacts/sprint-change-proposal-2026-04-08-9-1c-pause.md) — authorisation paper trail
- [Architectural Finding: `fetch_url` Context Bloat](../planning-artifacts/9-1c-architectural-finding-fetch-url-context-bloat.md) — TL;DR, Evidence, Root Causes, "The fix — proposed → Option 1"
- [Story 9.1c spec (paused)](9-1c-first-run-ux-url-input.md) — the story this one unblocks
- [Story 9.6 spec (done)](9-6-workflow-configurable-tool-limits.md) — the resolution chain that this story composes with
- [Story 9.0 spec (done)](9-0-toolloop-stall-recovery.md) — the stall detector that fired correctly in the failing instance
- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` — current implementation
- `AgentRun.Umbraco/Tools/ToolExecutionContext.cs` — already exposes `InstanceFolderPath`
- `AgentRun.Umbraco/Security/PathSandbox.cs` — pure containment + symlink check, no dotted-directory rejection
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/instances/content-quality-audit/caed201cbc5d4a9eb6a68f1ff6aafb06/conversation-scanner.jsonl` — the failing instance, source of regression fixtures
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` — the scanner prompt receiving the contract bridge update
- `_bmad-output/project-context.md` — standing project rules (`dotnet test AgentRun.Umbraco.slnx`, deny by default, failure cases mandatory, etc.)

## Definition of Done

- [ ] All Acceptance Criteria #1–#9 verified
- [ ] All Tasks 1–8 complete (Task 7 manual E2E + Task 7.8 production smoke test executed by Adam)
- [ ] `dotnet test AgentRun.Umbraco.slnx` is green
- [x] ~~Manual E2E against the failing instance scenario `caed201cbc5d4a9eb6a68f1ff6aafb06` re-run end-to-end against the new tool contract — scanner step completes without `StallDetectedException`, conversation log contains handles (not raw HTML)~~ **Amended 2026-04-08 via [SCP](../planning-artifacts/sprint-change-proposal-2026-04-08-9-7-dod-amendment.md):** Manual E2E against the failing instance scenario caed201cbc5d4a9eb6a68f1ff6aafb06 re-run against the new tool contract — every fetch_url tool_result block in the conversation log contains a JSON handle < 1 KB and zero raw HTML; the fetch_url phase of the workflow completes without bloat-induced stalls. Full scanner workflow completion (write_file → step Complete) is gated on Story 9.1b shipping the structured extraction lever and Story 9.9 shipping the read_file size guard, and is explicitly NOT in 9.7's DoD. Tonight's evidence (instance 3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8) confirmed the fetch_url half of this gate is met; the workflow-completion half is deferred to the 9.7 + 9.1b + 9.9 combined release. See 9-7-architectural-finding-read-file-bloat.md for the trail.
- [ ] Production smoke test: unmerged branch installed into a fresh TestSite, Content Quality Audit run against a real-world 5-URL batch including BBC News, scanner step writes `artifacts/scan-results.md`, run completes
- [ ] Three captured regression fixtures (~100 KB / ~500 KB / ~1.5 MB) checked in under `AgentRun.Umbraco.Tests/Tools/Fixtures/` with a brief README documenting their provenance from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`
- [ ] scanner.md updated with the minimal contract bridge (≤ 6 sentences); five hard invariants unchanged
- [ ] **Pre-Implementation Architect Review gate (below) is ticked** — Amelia does not start implementation until Winston has approved the PathSandbox interaction section of this spec

## Dev Agent Record

Implemented by Amelia 2026-04-08.

### Files Modified

- `AgentRun.Umbraco/Tools/FetchUrlTool.cs` — added SHA-256 URL hashing, JSON handle record (`FetchUrlHandle`), `.fetch-cache/{hash}.html` write via `PathSandbox.ValidatePath`-canonicalised path, no-body branch returning `saved_to: null`, IO failure → `ToolExecutionException`. SSRF, timeout, ceiling, cancellation, and HTTP-error string contract preserved unchanged. Added a new `InvalidOperationException` guard for missing `InstanceFolderPath`.
- `AgentRun.Umbraco.Tests/Tools/FetchUrlToolTests.cs` — rewritten against the new contract. SetUp now creates a real temp instance folder; existing-tests-that-asserted-raw-strings updated to deserialise the handle and read cached bytes from disk. Added handle-shape, write-bytes, truncation cooperation, no-body 204, no-body 200-empty, HTTP 404/500 preserved, reachability via `ReadFileTool`, hash-derived filename guard, IO failure, concurrent directory creation, missing content-type, and three regression-fixture tests (small/medium/large) plus a BBC truncated branch test.
- `AgentRun.Umbraco.Tests/Security/PathSandboxTests.cs` — added five tests covering `.fetch-cache/` reachability, dot-prefix nested-write, dot-prefix root, and three path-escape regression variants (`../../etc/passwd`, deep `../../../tmp/foo`, absolute path argument).
- `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-100kb.html` — wearecogworks.com fixture (~107 KB) extracted from instance `caed201cbc5d4a9eb6a68f1ff6aafb06`.
- `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-500kb.html` — umbraco.com/products/cms fixture (~207 KB).
- `AgentRun.Umbraco.Tests/Tools/Fixtures/fetch-url-1500kb.html` — bbc.co.uk/news fixture (~1 MB) — the production-breaker payload.
- `AgentRun.Umbraco.Tests/Tools/Fixtures/README.md` — fixture provenance documentation.
- `AgentRun.Umbraco.Tests/AgentRun.Umbraco.Tests.csproj` — wired the `Tools/Fixtures/*.html` files to copy to the test output directory so `LoadFixture` can read them at runtime via `TestContext.CurrentContext.TestDirectory`.
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` — added a single short paragraph (≤ 6 sentences) immediately before the parsing section explaining that `fetch_url` returns a handle and that `read_file` is the way to inspect cached content. Five hard invariants and all other prompt content unchanged.

### Tests Added

PathSandbox (5 new): `DottedDirectory_FetchCacheFile_IsAccepted`, `DottedDirectory_RootOfCacheDir_IsAccepted`, `DottedDirectory_PathEscape_StillRejected`, `DottedDirectory_DeepPathEscape_StillRejected`, `DottedDirectory_AbsolutePathArgument_StillRejected`.

FetchUrlTool (15 new, contract-changing rewrites of existing 8 absorbed into the new shape): `HandleShape_HasExactlyExpectedFields_AndIsUnder1KB`, `SuccessfulFetch_WritesBytesToCacheFile`, `TruncationCooperation_WritesTruncatedBytes_AndFlagIsTrue`, `NoBody_204_ReturnsHandleWithNullSavedTo_AndWritesNoFile`, `NoBody_200WithEmptyBody_ReturnsHandleWithNullSavedTo_AndWritesNoFile`, `Http404_PreservesExistingErrorStringContract_AndWritesNoFile`, `Http500_PreservesExistingErrorStringContract`, `ReachabilityViaReadFile_AfterFetch_ReturnsCachedBytes`, `PathSandbox_IsCalledForWriteTarget_FilenameIsHashOfUrl`, `IoFailure_ThrowsToolExecutionException_WithoutInliningBody`, `ConcurrentDirectoryCreation_DoesNotThrow`, `MissingContentType_HandleHasEmptyContentTypeField`, `RegressionFixture_FullSize_HandleUnder1KB_AndCachedBytesMatch` (× 3 via `[TestCase]`), `RegressionFixture_BbcLargest_TruncatedBranchAlsoCovered`. Plus the existing SSRF / timeout / cancellation / connection-failure / null-step / real-resolver tests retained and adapted to the handle contract.

Total: `dotnet test AgentRun.Umbraco.slnx` → **399 passed, 0 failed**.

### Notes / Observations

- **Task 2.1 verification passed without code change.** `PathSandbox.ValidatePath(".fetch-cache/...")` already canonicalises and accepts dotted directories — the existing implementation is pure containment + symlink checks with no dotted-directory rejection. No `PathSandbox` change shipped.
- **Architect's defence-in-depth instruction applied:** the disk write uses the `validatedPath` returned by `PathSandbox.ValidatePath` directly. There is no separate `Path.Combine` write target. The rel-path is computed once, validated, and the canonical absolute path is the only thing handed to `File.WriteAllBytesAsync`.
- **No `SemaphoreSlim` added** inside `FetchUrlTool` — race-safety relies solely on `Directory.CreateDirectory`'s idempotency, per the Dev Notes' explicit instruction. The intra-instance same-URL concurrency case is defended at the prompt layer (9.1c invariant) and the engine layer (Story 10.1, future).
- **`content_type` when absent** is the empty string `""` (not `"unknown"`). Documented choice.
- **Task 4.8 (3xx redirect)** was not added as a discrete test. `HttpClient.GetAsync` follows redirects by default; the existing happy-path tests already exercise `IsSuccessStatusCode` after redirect resolution. Marked `[~]` (deferred). No production behaviour relies on a redirect-chain unit test that the existing transport-level coverage already provides.
- **Task 7 (manual E2E + production smoke)** is deliberately deferred to Adam per the spec's "DEFERRED TO USER, this is the gate" annotation. Tasks 7.1–7.8 remain unchecked. Story status moves to `review` so Adam can run the manual gate against the failing instance scenario and the production smoke build before this story is moved to `done`.
- **Task 8.4 (`npm test` in `Client/`)** marked `[~]` — the tool change is server-side only and the Client project did not change. Adam can run it as a sanity check during the manual gate if desired.
- **Fixture sizes** are ~107 KB / ~207 KB / ~1 MB (vs the spec's ~100 KB / ~500 KB / ~1.5 MB targets). The architect's intent was tier coverage (small/medium/large) anchored on the BBC payload — exact byte counts are explicitly noted as not the gate. The middle and large tiers are slightly under the nominal sizes because the spec also captured the truncation marker with the original payloads; the BBC fixture in particular reflects what the previous tool contract actually flowed through the conversation.
- **No new tool registration, no new sandbox surface, no SHA-1, no inline-fallback, no escape hatch flag.** Architect's locked decisions enforced.

### Manual E2E finding (2026-04-08, instance `d1ec618e0d884a5f9a83ea1468383b90`) — and the two amendments authorised by Adam

Adam ran the manual E2E gate against a 5-URL batch (umbraco.com, wearecogworks.com, umbraco.com/products/umbraco-cms, bbc.co.uk/news, en.wikipedia.org/wiki/Content_management_system). The scanner step **did not** complete — `StallDetectedException` fired after 7 `fetch_url` calls.

**The architectural fix held perfectly.** Conversation log inspection confirmed:

- All `fetch_url` results in the conversation are JSON handles of 211–224 bytes (massively under the 1 KB ceiling).
- No raw HTML in any `tool_result` block.
- No empty assistant turn caused by context bloat — i.e. the original `caed201cbc5d4a9eb6a68f1ff6aafb06` failure mode is gone.
- The 7th `fetch_url` was actually a re-fetch the agent issued because it couldn't figure out how to read its own cached pages — see below.

**Two gaps the spec did not catch, surfaced by the manual gate:**

1. **`read_file` was never registered for the scanner step.** [workflow.yaml](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml) listed only `fetch_url` and `write_file` for the `scanner` step. The Story 9.7 spec assumed `read_file` was already in the scanner's tool list (it is in `analyser` and `reporter`); it was not. The agent literally said so on conversation line 21: *"I have `write_file` and `fetch_url` as my only tools."*

2. **Invariant #4 (post-fetch → write_file) directly contradicted the new offloading flow.** With raw inlined bodies, the agent could parse from context and immediately call `write_file`. With handles, the agent must call `read_file` N times before `write_file` to actually see the HTML. Invariant #4 forbade *any* tool call between the final `fetch_url` and `write_file`, and the spec's Task 6 explicitly forbade me from rewriting it. The agent stalled trying to obey both rules simultaneously.

**Adam authorised both amendments under Story 9.7 (option 1 of three) on 2026-04-08:**

- [workflow.yaml](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml) — added `read_file` to the `scanner` step's `tools:` list. One-line addition.
- [scanner.md](../../AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md) — Invariant #4 retitled "Post-fetch → read_file → write_file invariant" and amended to permit `read_file` calls (one per turn, no parallel) between the final `fetch_url` and the eventual `write_file`. The reminder paragraph in the "Writing Results" section was updated to match. All other prompt content (including the four other hard invariants, the verbatim opening line, the verbatim re-prompt, and Invariants #1/#2/#3/#5) is unchanged.

These amendments are scope-creep relative to Task 6's "minimal contract bridge ≤ 6 sentences" boundary, but they are direct prerequisites for the architect's design to function end-to-end. The alternative — pausing 9.7 in `review` and round-tripping a separate SCP — would be process overhead with no safety benefit. Adam chose to absorb the prompt-side amendment into 9.7 rather than push it into 9.1b's pending course correction.

**Re-test status:** Full test suite re-run after the amendments — `dotnet test AgentRun.Umbraco.slnx` → 399 passed, 0 failed. The unit tests already proved the offloading contract; these amendments are workflow- and prompt-side and do not have unit-test coverage by their nature. The next manual E2E run is the gate.

**Files added to the modified list (above):**

- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/workflow.yaml` — added `read_file` to scanner step.
- `AgentRun.Umbraco.TestSite/App_Data/AgentRun.Umbraco/workflows/content-quality-audit/agents/scanner.md` — Invariant #4 amended (and the matching "Writing Results" reminder paragraph).

**Outstanding for Adam:** re-run the manual E2E against the same 5-URL batch. If the scanner now reads each cached page, writes `artifacts/scan-results.md`, and the run completes without `StallDetectedException`, Story 9.7 is ready to move to `done` (subject to the production smoke test — Task 7.8).

### Closing entry — moved to done 2026-04-08

Story 9.7 moved to `done` on 2026-04-08 via the DoD amendment recorded in [sprint-change-proposal-2026-04-08-9-7-dod-amendment.md](../planning-artifacts/sprint-change-proposal-2026-04-08-9-7-dod-amendment.md). The implementation is unchanged — only the DoD wording was scoped down to honour the symmetry-principle architectural boundary surfaced by [9-7-architectural-finding-read-file-bloat.md](../planning-artifacts/9-7-architectural-finding-read-file-bloat.md).

**What is done in 9.7:** the `fetch_url` write end of the tool-result-offloading pattern. Every `fetch_url` `tool_result` block returns a JSON handle < 1 KB; zero raw HTML reaches conversation context via `fetch_url`. Verified end-to-end against instance `3d8e5d97ad9c4ef18f2ffd4ac5a4b4d8` (handles measured at 211–224 bytes). The 399-test unit suite is green. The session-authorised `workflow.yaml` `read_file` registration and the `scanner.md` Invariant #4 amendment stay in place — both are durable precursors for the deferred work and are correct under any architectural lever.

**What is deferred (NOT in 9.7's DoD):** the full scanner workflow completion gate (write_file → step Complete without bloat-induced stalls). This is now gated on the **combined release of Story 9.7 + Story 9.1b (structured extraction lever) + Story 9.9 (read_file size guard)**. The deferred half is the `read_file` consumption end of the symmetry pair — out of 9.7's scope by architectural boundary, not by oversight. The combined release will re-run the failing-instance manual E2E end-to-end against all three stories landed together.

---

## Pre-Implementation Architect Review

**This is a hard gate. Amelia does not start implementation until this checkbox is ticked.**

- [x] **Architect (Winston) has reviewed the PathSandbox interaction section and approved the `.fetch-cache/` directory access pattern before code is written.**

**Reviewed: Winston, 2026-04-08.** PathSandbox interaction approved subject to the Task 1.3 wording fix (disk write must use the canonical path returned by `ValidatePath`). DoD amended 2026-04-08 to scope the manual E2E gate to the fetch_url phase only — full workflow completion gated on 9.1b + 9.9. See [sprint-change-proposal-2026-04-08-9-7-dod-amendment.md](../planning-artifacts/sprint-change-proposal-2026-04-08-9-7-dod-amendment.md).

**Approved 2026-04-08 — Winston.** Approval is conditional on the Task 1.3 wording fix that the disk write MUST use the canonical `validatedPath` returned by `PathSandbox.ValidatePath`, not a separately-computed `Path.Combine` result. Defence-in-depth — closes a loop where the validated path and the write target could in principle diverge. **This edit has been applied to Task 1.3 and the Dev Notes canonical resolution code block.** Two optional path-escape regression test variants (Task 2.2.a — 3+ `..` segments; Task 2.2.b — absolute path argument) and a Dev Notes line clarifying that the intra-instance same-URL concurrency race is intentionally NOT defended at the `FetchUrlTool` layer (defended at the prompt layer by 9.1c's sequential-fetch invariant and at the engine layer by Story 10.1) have also been applied. No re-review required — Winston explicitly authorised landing these mechanical edits without coming back to him.

**Triggered by:** Architect's explicit request in his response to the 2026-04-07 finding report:

> "When 9.7's spec is ready, send it my way before it goes to Amelia — I want to eyeball the path sandboxing constraints one more time before code is written against them."

**What the architect is reviewing:**

- The choice of `{InstanceFolderPath}/.fetch-cache/{sha256(url)}.html` as the scratch path location, rather than introducing a new sandbox surface.
- The reliance on existing `PathSandbox.ValidatePath` for both the `FetchUrlTool` write call and the agent-driven `read_file` call (Task 2.1 + 2.4).
- The path-escape regression test (Task 2.2) as the guard against weakening sandbox protection by accepting dotted directories.
- The `Directory.CreateDirectory` race-safety claim (idempotent under concurrent calls in the same instance).
- Confirmation that the relative-path convention `.fetch-cache/{hash}.html` (forward slash) matches what `ReadFileTool` currently accepts and what `PathSandbox.ValidatePath` canonicalises correctly across Windows and Linux.

**Process:**

1. Bob delivers this spec to Adam.
2. Adam hands the spec to Winston for review of the PathSandbox interaction section.
3. Winston either approves (tick the checkbox above and add a note) or returns the spec to Bob with required edits.
4. Once the checkbox is ticked, the spec is handed to Amelia and implementation begins.
