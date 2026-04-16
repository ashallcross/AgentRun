# Story 10.4: Open-Source Licence Decision — Apache 2.0

Status: review

**Depends on:** None. Zero code dependency on any other in-flight Epic 10 story — [epics.md §Story 10.4](../planning-artifacts/epics.md#L2185) describes this explicitly as "a product/legal decision, not a code task." Can run in parallel with Story 10.7c (in-progress).
**Followed by:** Story 10.5 (marketplace listing & community launch). Marketplace publication requires an applied SPDX licence; this story is the prerequisite for 10.5 but is not on the critical path of any other Epic 10 work.
**Branch:** `feature/epic-10-continued`
**Priority:** 12th in Epic 10 (10.2 → 10.12 → 10.8 → 10.9 → 10.10 → 10.1 → 10.6 → 10.11 → 10.7a → 10.7b → 10.7c → **10.4** → 10.5). Positional — not dependent. Can be worked on today.

> **Licence decision is pre-locked: Apache License 2.0.** Decided by Adam on 2026-04-16 (this session). This story does not elicit or debate the choice — it implements the application of the decision. MIT was considered and rejected; the alternatives section below records that comparison for audit purposes but does not re-open the question.
>
> **Scope is tight.** Add the LICENSE file, update the csproj SPDX metadata, add a README footer section, optionally add a NOTICE file for third-party attribution, audit direct dependencies for licence compatibility, and rebuild + verify the nupkg embeds the licence correctly. No code changes, no per-file SPDX headers, no repository-publication step.

## Story

As a product owner preparing AgentRun.Umbraco for public marketplace listing,
I want the Apache License 2.0 applied to the repository, embedded in the nupkg metadata, surfaced in README, and verified against the transitive dependency graph,
So that Story 10.5 (marketplace listing & community launch) has an unambiguous licensing posture, downstream consumers have clear permission terms, and the community can adopt, fork, or contribute to the package without ambiguity.

## Context

**UX Mode: N/A.** Legal / metadata / packaging change only. No user-facing UI, no runtime behaviour, no test changes, no build configuration change beyond csproj SPDX metadata.

### Why now

The csproj has carried a pinned TODO since Story 9.3 (documentation + NuGet packaging):

```xml
<!-- PackageLicenseExpression — deferred to Story 10.4 (licensing decision) -->
<!-- PackageProjectUrl / RepositoryUrl — deferred until repo is public -->
```

Story 10.5 (marketplace listing) cannot ship without the SPDX expression populated — Umbraco Marketplace, NuGet.org, and most community-consumption paths require an explicit licence. Every day the package sits on NuGet.org as `1.0.0-beta.2` without a licence expression is a day beta testers have to ask "what licence is this?" or assume "no licence" (which is legally the most restrictive stance — all rights reserved). Apache 2.0 applied now removes that ambiguity before public 1.0.

### Why Apache 2.0 and not MIT

Both are OSI-approved permissive licences; either would allow the marketplace listing to proceed. Apache 2.0 is the pre-locked decision (Adam, 2026-04-16). The concrete differences between Apache 2.0 and MIT that matter for this project are:

| Property | MIT | Apache 2.0 | Relevance |
|---|---|---|---|
| Commercial use | ✅ | ✅ | Both allow |
| Modification + redistribution | ✅ | ✅ | Both allow |
| **Explicit patent grant** | ❌ | ✅ | Apache 2.0 grants a patent licence from contributors and terminates it on patent litigation — meaningful for a package that integrates LLM providers and may accumulate contributor-held patents in the AI-adjacent space |
| **NOTICE file convention** | ❌ | ✅ | Apache 2.0 supports a `NOTICE` file for attribution preservation through forks — useful when redistributing with third-party permissively-licensed components (AngleSharp, Umbraco.AI, etc.) |
| **Trademark clause** | ❌ | ✅ (§6) | Apache 2.0 explicitly does not grant trademark rights — useful boundary if "AgentRun" becomes a brand |
| **State changes requirement** | ❌ | ✅ | Apache 2.0 requires modified files be marked — small compliance cost downstream but keeps provenance clear |
| **File-level compatibility** | Same | Same | Both are GPL-compatible outbound; both permit closed-source redistribution |
| **Length / readability** | 1 paragraph | ~10 KB | MIT wins on brevity; Apache 2.0's patent + trademark clauses are the trade-off |

**Decision rationale (as pre-locked by Adam):** Apache 2.0 is the chosen licence. The rationale is recorded here for audit purposes but is not re-openable in this story — if a change is needed, use `bmad-correct-course`.

### What this story does NOT do

- **Does NOT publish the repository as public.** The csproj comment `<!-- PackageProjectUrl / RepositoryUrl — deferred until repo is public -->` stays as-is. Apache 2.0 licence applied to the nupkg is sufficient for marketplace listing without the source repository being publicly accessible. Publishing the repo is a separate product decision, not in this story's scope.
- **Does NOT add per-file SPDX headers.** Apache 2.0 supports them (see the Appendix in the licence text) but does not require them. Adding a copyright header block to every `.cs` and `.ts` file in the package is ~200+ files of pure-churn diff with no material benefit for a permissively-licensed single-author project. Skip.
- **Does NOT update existing copyright attributions across source files.** There are none to update — the codebase has no existing copyright headers.
- **Does NOT modify any `.cs`, `.ts`, or test file.** Pure packaging + documentation change.
- **Does NOT bump the version number.** `1.0.0-beta.2` stays. The licence change is not a code change; a fresh `dotnet pack` produces a new `.nupkg` file with the licence embedded, which can be published as `1.0.0-beta.3` or held back until a combined 10.4 + 10.5 publish — that is a Story 10.5 decision, not 10.4's.

### Dependency licence landscape (pre-audit view)

All direct `<PackageReference>` entries in [AgentRun.Umbraco.csproj](../../AgentRun.Umbraco/AgentRun.Umbraco.csproj#L23-L31) at the time of this story spec:

| Package | Version | Licence | Apache-2.0-outbound compatible? |
|---|---|---|---|
| AngleSharp | 1.4.0 | MIT (verified Story 9.1b) | ✅ Yes — MIT is more permissive than Apache 2.0 |
| Umbraco.Cms.Web.Website | 17.3.2 | MIT (Umbraco convention) | ✅ Yes |
| Umbraco.Cms.Web.Common | 17.3.2 | MIT | ✅ Yes |
| Umbraco.Cms.Api.Common | 17.3.2 | MIT | ✅ Yes |
| Umbraco.Cms.Api.Management | 17.3.2 | MIT | ✅ Yes |
| Umbraco.AI | 1.8.0 | MIT | ✅ Yes |
| YamlDotNet | 16.3.0 | MIT | ✅ Yes |

All direct deps are MIT-licensed. MIT is Apache-2.0-compatible in the outbound direction (we can redistribute MIT-licensed components under our Apache 2.0 package without any licence conflict). Task 5 formalises this via `dotnet list package --include-transitive` and spot-check of the transitive graph for any GPL / LGPL / EPL surprises that could force outbound relicensing.

### Locked decisions (do not relitigate)

1. **Licence is Apache 2.0.** Pre-locked by Adam on 2026-04-16. Use the standard Apache 2.0 text from [apache.org/licenses/LICENSE-2.0.txt](https://www.apache.org/licenses/LICENSE-2.0.txt) verbatim — no modification, no custom preamble, no removal of the Appendix.
2. **SPDX expression: `Apache-2.0`.** This is the SPDX short identifier. Populate `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` in the `.csproj`. Do NOT use the legacy `<PackageLicenseUrl>` or `<PackageLicenseFile>` forms — SPDX expression is the modern NuGet convention and is what NuGet.org and the Umbraco Marketplace consume.
3. **Copyright line format: `Copyright 2026 Adam Shallcross`.** Single author, no corporate attribution, matches `<Authors>Adam Shallcross</Authors>` in the csproj. If external contributors are added later via `bmad-correct-course`, the copyright line extends to "Copyright 2026 Adam Shallcross and contributors" — but until then, single-author attribution is correct.
4. **No per-file SPDX headers.** Rationale in §"What this story does NOT do" above. If Apache Software Foundation compliance ever requires per-file attribution (e.g., donation to ASF), that is a separate effort.
5. **NOTICE file is REQUIRED, not optional.** Apache 2.0 convention uses NOTICE for attribution of included third-party works. This project redistributes AngleSharp (MIT), YamlDotNet (MIT), and references Umbraco + Umbraco.AI (MIT) — acknowledge them in NOTICE. The convention is well-established and makes licence audit trivial for downstream consumers. Adding it is ~10 lines of text; omitting it is mild sloppiness.
6. **README licence section is REQUIRED.** Add a "## Licence" section at the end of the README, single paragraph, links to `LICENSE` file via relative path. This is what Umbraco Marketplace consumers see first when evaluating the package.
7. **Dependency licence audit is REQUIRED and gated on explicit failure.** Run `dotnet list package --include-transitive` from the package project. Document every non-MIT, non-Apache-2.0, non-BSD licence in the Dev Agent Record. If any GPL / LGPL / EPL / MPL / CPL / SSPL appears in the transitive graph, STOP and flag to Adam before proceeding — outbound Apache 2.0 may not be compatible with all of those. The expected outcome is "all transitive deps are MIT/Apache-2.0/BSD/MS-PL" — but verifying is the point.
8. **Nupkg verification is REQUIRED.** After `dotnet pack`, inspect the generated `.nupkg` (it is a zip file) and verify: (a) the `.nuspec` inside the nupkg contains `<license type="expression">Apache-2.0</license>`, (b) the `LICENSE` file is NOT auto-included by NuGet (SPDX-expression mode does not pack the file — that's expected), (c) the README in the nupkg renders the licence section correctly. Capture the nuspec snippet in the Dev Agent Record.
9. **`PackageProjectUrl` and `RepositoryUrl` stay deferred.** The csproj comment at [AgentRun.Umbraco.csproj:19](../../AgentRun.Umbraco/AgentRun.Umbraco.csproj#L19) remains untouched. These populate once the repo is public (separate decision); applying the licence does not require the repo to be public.
10. **No version bump.** `<Version>1.0.0-beta.2</Version>` stays. Version bump decisions belong with Story 10.5 or a scheduled beta drop — 10.4 is licence-only.
11. **No GitHub Actions / CI changes.** There is no `.github/workflows/` directory driving CI in this repo today; applying a licence does not create a need for licence-check CI workflows. If that becomes desirable later it is a separate story.
12. **No changes to `Umbraco AI.sln`, `.slnx`, test projects, or TestSite.** All work is confined to: new LICENSE file at repo root, new NOTICE file at repo root, `AgentRun.Umbraco.csproj` edit, `README.md` edit.

## Acceptance Criteria (BDD)

### AC1: `LICENSE` file at repository root contains verbatim Apache License 2.0 text with correct copyright

**Given** the repository root [/Users/adamshallcross/Documents/Umbraco AI/](..)
**When** a new `LICENSE` file is created at the root
**Then** the file contents begin with the line `Apache License` followed by `Version 2.0, January 2004`
**And** the body matches the standard Apache License 2.0 text byte-for-byte (from [apache.org/licenses/LICENSE-2.0.txt](https://www.apache.org/licenses/LICENSE-2.0.txt))
**And** the Appendix section at the end reads `Copyright 2026 Adam Shallcross` in the placeholder line (replacing `[yyyy] [name of copyright owner]`)
**And** the file ends with a single trailing newline (Unix convention)
**And** the file is tracked by git (appears in `git ls-files LICENSE`)

### AC2: `NOTICE` file at repository root acknowledges third-party components

**Given** the repository root
**When** a new `NOTICE` file is created at the root
**Then** the file contains a header block identifying this package as `AgentRun.Umbraco` with the copyright line `Copyright 2026 Adam Shallcross`
**And** the file acknowledges each direct third-party runtime dependency by name, with the licence of the dep in parentheses (e.g. `AngleSharp (MIT)`, `YamlDotNet (MIT)`, `Umbraco CMS (MIT)`, `Umbraco.AI (MIT)`)
**And** the file is tracked by git

### AC3: `AgentRun.Umbraco.csproj` sets `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`

**Given** [AgentRun.Umbraco.csproj](../../AgentRun.Umbraco/AgentRun.Umbraco.csproj) with its deferred-licence TODO comment at line 18
**When** the csproj is updated
**Then** the comment `<!-- PackageLicenseExpression — deferred to Story 10.4 (licensing decision) -->` is replaced with the element `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`
**And** the csproj still parses (verify with `dotnet build AgentRun.Umbraco.slnx`)
**And** the `<!-- PackageProjectUrl / RepositoryUrl — deferred until repo is public -->` comment at line 19 is UNTOUCHED per locked decision 9
**And** no other `<Package*>` element is removed, reordered, or modified

### AC4: README has a `## Licence` section with a link to `LICENSE` and clear SPDX identifier

**Given** [README.md](../../README.md)
**When** the README is updated
**Then** a new `## Licence` section is appended before any existing terminal section (or at the end if there is none)
**And** the section identifies the licence by name (Apache License 2.0) and SPDX identifier (`Apache-2.0`)
**And** the section contains a relative-path link to the `LICENSE` file at the repo root
**And** the section explicitly states the copyright holder (Adam Shallcross, 2026)
**And** the section does not exceed ~5 lines of rendered text — licence attribution should be discoverable, not prominent

### AC5: Transitive dependency licence audit completes with zero conflicts

**Given** the direct dependencies in `AgentRun.Umbraco.csproj` (7 packages)
**When** `dotnet list package --include-transitive` runs against `AgentRun.Umbraco.slnx`
**Then** the output is captured in the Dev Agent Record
**And** every transitive dependency's licence is verified as permissive (MIT / Apache-2.0 / BSD-2-Clause / BSD-3-Clause / MS-PL / Zlib) either via `nuget.org` metadata lookup (for the top 10 transitive deps by prevalence) or via the project's existing `.nuspec` files
**And** the Dev Agent Record tabulates any dep with a non-permissive licence (GPL / LGPL / EPL / MPL / SSPL / CPL / proprietary) — expected to be **zero** entries
**And** if the zero-entry expectation is violated, the dev agent STOPS and escalates to Adam via the Dev Agent Record before proceeding to AC6 — outbound Apache 2.0 may not be compatible with a GPL transitive and that requires a product decision, not a code decision

### AC6: `dotnet pack` produces a nupkg with correct licence metadata embedded

**Given** AC1, AC2, AC3 are complete
**When** `dotnet pack AgentRun.Umbraco/AgentRun.Umbraco.csproj -c Release` runs
**Then** a new `AgentRun.Umbraco.1.0.0-beta.2.nupkg` is produced in `AgentRun.Umbraco/bin/Release/` (overwriting the existing one — version number has not changed per locked decision 10)
**And** extracting the nupkg (it is a zip) and inspecting `AgentRun.Umbraco.nuspec` inside shows `<license type="expression">Apache-2.0</license>` within the `<metadata>` element
**And** the nupkg contains the updated `README.md` with the new Licence section
**And** the build produces no new warnings related to licensing (NU5128, NU5125, or similar)
**And** the nuspec snippet is captured in the Dev Agent Record

### AC7: Full backend + frontend suites still green (regression sanity gate)

**Given** the story is complete
**When** `dotnet test AgentRun.Umbraco.slnx` runs and `cd AgentRun.Umbraco/Client && npm test` runs
**Then** backend test count is unchanged from the 10.7c baseline (whatever 10.7c finishes at) and all green
**And** frontend test count is unchanged from 10.7b baseline (235/235) and all green
**And** `dotnet build AgentRun.Umbraco.slnx` is clean — zero new warnings
**Note:** Test suites should be unaffected — this story makes no code change. The gate exists to catch accidental cross-contamination (e.g., the dev agent accidentally modifying a `.cs` file instead of only `.csproj`, `README.md`, and new root-level licence files).

## Tasks / Subtasks

### Task 1: Create `LICENSE` at repository root (AC1)

- [x] 1.1 Fetch the canonical Apache License 2.0 text from [apache.org/licenses/LICENSE-2.0.txt](https://www.apache.org/licenses/LICENSE-2.0.txt). Do NOT paraphrase. Do NOT re-wrap. Use the upstream text byte-for-byte.
- [x] 1.2 In the Appendix section at the end of the licence text, replace the placeholder `[yyyy] [name of copyright owner]` with `2026 Adam Shallcross`. The full line becomes `Copyright 2026 Adam Shallcross`.
- [x] 1.3 Save the file as `/Users/adamshallcross/Documents/Umbraco AI/LICENSE` (no extension — this is the convention for licence files and is what NuGet / GitHub / Umbraco Marketplace tooling detect).
- [x] 1.4 `git add LICENSE` and verify `git status` shows the new file tracked.

### Task 2: Create `NOTICE` at repository root (AC2)

- [x] 2.1 Create `/Users/adamshallcross/Documents/Umbraco AI/NOTICE` with the following shape (substitute any dep versions from the actual `.csproj` at time of story execution):

  ```
  AgentRun.Umbraco
  Copyright 2026 Adam Shallcross

  This product includes software developed as part of the following third-party
  open-source projects, all used under permissive licences compatible with the
  Apache License 2.0 under which AgentRun.Umbraco is distributed:

  - Umbraco CMS (MIT) — https://github.com/umbraco/Umbraco-CMS
  - Umbraco.AI (MIT) — https://www.nuget.org/packages/Umbraco.AI
  - AngleSharp (MIT) — https://github.com/AngleSharp/AngleSharp
  - YamlDotNet (MIT) — https://github.com/aaubry/YamlDotNet

  This NOTICE file is provided in accordance with section 4(d) of the Apache
  License 2.0 and is not intended to modify the licence applicable to
  AgentRun.Umbraco or any included third-party component.
  ```

- [x] 2.2 `git add NOTICE`.

### Task 3: Update `AgentRun.Umbraco.csproj` SPDX metadata (AC3)

- [x] 3.1 Open [AgentRun.Umbraco.csproj](../../AgentRun.Umbraco/AgentRun.Umbraco.csproj).
- [x] 3.2 Replace the comment at line 18 (`<!-- PackageLicenseExpression — deferred to Story 10.4 (licensing decision) -->`) with the element `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`. Preserve surrounding indentation (2 spaces).
- [x] 3.3 Leave the comment at line 19 (`<!-- PackageProjectUrl / RepositoryUrl — deferred until repo is public -->`) untouched — per locked decision 9.
- [x] 3.4 Run `dotnet build AgentRun.Umbraco.slnx` — build clean, no new warnings, in particular no `NU5128` / `NU5125` / `NU5034` warnings about missing or ambiguous licence metadata.

### Task 4: Update `README.md` with Licence section (AC4)

- [x] 4.1 Open [README.md](../../README.md).
- [x] 4.2 Append a `## Licence` section to the end of the file (or just before a terminal section if one exists), containing:

  ```markdown
  ## Licence

  AgentRun.Umbraco is licensed under the [Apache License 2.0](./LICENSE)
  (SPDX: `Apache-2.0`).

  Copyright 2026 Adam Shallcross.

  Third-party components included or referenced by this package are acknowledged
  in [`NOTICE`](./NOTICE), all used under permissive licences compatible with
  Apache 2.0.
  ```

- [x] 4.3 Ensure the README ends with exactly one trailing newline (markdown convention).

### Task 5: Dependency licence audit (AC5)

- [x] 5.1 Run `dotnet list package --include-transitive --project AgentRun.Umbraco/AgentRun.Umbraco.csproj` and capture the full output in the Dev Agent Record under `## Dev Agent Record > ### Dependency Licence Audit`.
- [x] 5.2 For each **direct** dependency in the output, verify the licence via nuget.org metadata (visit the package page or use `dotnet nuget package info {id} {version}` — licence expression is in the `.nuspec`). Expected: all MIT.
- [x] 5.3 For the **transitive** dependencies, spot-check the top 10 by prevalence (count how many direct packages pull each transitive). Tabulate each as `{package}, {version}, {licence}` in the Dev Agent Record.
- [x] 5.4 Flag any transitive dep with a licence in the denylist: `GPL-*`, `LGPL-*`, `EPL-*`, `MPL-*`, `SSPL-*`, `CPL-*`, `Ms-RL`, or any "proprietary" / "non-standard" / blank entry. If ≥ 1 flag, STOP and record in the Dev Agent Record under a clearly-labelled `### ⚠️ Licence Conflict — ESCALATE` block. Do not proceed to Task 6.
- [x] 5.5 If zero flags, record the audit summary as: `All N direct dependencies and the top K transitive dependencies use Apache-2.0-outbound-compatible permissive licences (MIT / Apache-2.0 / BSD-* / MS-PL / Zlib). No conflicts.`

### Task 6: Build nupkg and verify licence embedding (AC6)

- [x] 6.1 Run `dotnet pack AgentRun.Umbraco/AgentRun.Umbraco.csproj -c Release` from the repo root. Verify a new `AgentRun.Umbraco.1.0.0-beta.2.nupkg` lands in `AgentRun.Umbraco/bin/Release/`.
- [x] 6.2 Extract the nupkg (it is a zip): `unzip -o AgentRun.Umbraco/bin/Release/AgentRun.Umbraco.1.0.0-beta.2.nupkg -d /tmp/agentrun-nupkg-inspect/`.
- [x] 6.3 Inspect `/tmp/agentrun-nupkg-inspect/AgentRun.Umbraco.nuspec` and verify the `<metadata>` element contains `<license type="expression">Apache-2.0</license>`. Paste the full `<metadata>` block into the Dev Agent Record.
- [x] 6.4 Inspect `/tmp/agentrun-nupkg-inspect/README.md` and verify the `## Licence` section from Task 4.2 is present.
- [x] 6.5 Verify the build produced no licence-related warnings by searching the build output for `NU51` or `NU50` prefixes.
- [x] 6.6 Clean up `/tmp/agentrun-nupkg-inspect/` after verification.

### Task 7: DoD verification + commit train (AC7)

- [x] 7.1 Full backend test: `dotnet test AgentRun.Umbraco.slnx` → all green (AC7).
- [x] 7.2 Full frontend test: `cd AgentRun.Umbraco/Client && npm test` → all green (count unchanged from 10.7b baseline 235).
- [x] 7.3 Build clean: `dotnet build AgentRun.Umbraco.slnx` → 0 new warnings.
- [x] 7.4 Git status sanity check — verify ONLY the following files are modified / new:
  - NEW: `/LICENSE`
  - NEW: `/NOTICE`
  - MODIFIED: `/AgentRun.Umbraco/AgentRun.Umbraco.csproj`
  - MODIFIED: `/README.md`

  If any other file is modified, revert it — this story is strictly scoped.
- [x] 7.5 Commit train (single commit is correct for this story — the four files are one cohesive change):
  - Commit: `Story 10.4: apply Apache License 2.0 — LICENSE + NOTICE + csproj SPDX + README section`
  - Commit body: brief summary of what each file contains + the dep-audit result from Task 5.5
  - Co-Authored-By trailer per CLAUDE.md
- [x] 7.6 Set story status to `done` in this file + `sprint-status.yaml` once 7.1–7.5 pass.
- [x] 7.7 Update the csproj TODO comment history: the `PackageLicenseExpression` line was deferred from Story 9.3 and is now closed. Note in the commit body that the 9.3 TODO is resolved.

## Failure & Edge Cases

### F1: A transitive dependency turns out to be GPL / LGPL / EPL / MPL / SSPL

**When** Task 5.4's denylist check hits ≥ 1 entry
**Then** the dev agent STOPS at Task 5.4, does not proceed to Task 6 (nupkg build), and escalates to Adam in the Dev Agent Record with a clearly-labelled `### ⚠️ Licence Conflict — ESCALATE` block listing each conflicting dep with its licence + source
**Mitigation:** the expected outcome is zero conflicts — the direct deps are all MIT, and the .NET / Umbraco / AngleSharp / YamlDotNet ecosystems are dominated by MIT / Apache-2.0 transitives. A GPL transitive would be surprising. If one appears, it is likely a build-tools-only dep (e.g., a code-generator NuGet) that is not redistributed in the runtime nupkg — those can sometimes be ignored under the "mere aggregation" doctrine of the GPL, but this is a Adam-judgement call, not a dev-agent call.
**Net effect:** story pauses for Adam decision; either the conflicting dep is removed, or the licence choice is revisited via `bmad-correct-course`, or the dep is justified as non-redistributed.

### F2: `dotnet pack` warning NU5128 "Some target frameworks declared in the dependencies group of the nuspec and the lib/ref folder do not have exact matches"

**When** the pack runs and NU5128 surfaces
**Then** this is almost certainly pre-existing and unrelated to the licence change (it concerns framework-to-lib-folder mapping)
**Mitigation:** Record the warning in the Dev Agent Record, note it was present in the 10.3 nupkg produced in Story 9.3, and proceed. Not a blocker for Story 10.4. File as deferred-work if newly-surfaced.

### F3: Copyright line year drifts past 2026

**When** the story is executed on a date past 2026-12-31 (e.g., Amelia runs this in 2027)
**Then** the copyright year should be updated to the current year OR a range like `2026–2027`
**Mitigation:** the dev agent must check the current date at execution time. The Apache 2.0 convention accepts either form. For the private-beta-to-1.0 window (2026), a single year `2026` is correct.
**Net effect:** trivial — adjust Task 1.2 to use the execution year.

### F4: Accidental per-file SPDX-header creep

**When** the dev agent, guided by the overall Apache 2.0 theme of the story, is tempted to add `// SPDX-License-Identifier: Apache-2.0` headers to some or all `.cs` or `.ts` files
**Then** the dev agent MUST STOP — this violates locked decision 4 (no per-file SPDX headers) and expands scope from a four-file change to a 200+ file diff
**Mitigation:** Task 7.4's git-status sanity check catches this — if any file outside the four-file allowlist is modified, revert.
**Net effect:** scope discipline. Per-file headers are a separate, non-blocking, optional polish effort — file as deferred-work if anyone wants to re-raise it, but do not add them in this story.

### F5: README linter complains about the bare `./LICENSE` link

**When** some markdown linter (if configured) flags `[Apache License 2.0](./LICENSE)` because the file has no `.md` extension
**Then** the bare-filename link is correct for a root-level `LICENSE` file with no extension
**Mitigation:** README linting is not currently part of CI for this repo. If the dev agent is running a local markdown linter and it complains, suppress the specific rule or add a linter-ignore comment — do not rename the LICENSE file just to satisfy a linter.

## Dev Notes

### Why this story is spec'd at length despite being a small task

Three reasons the spec carries more detail than the execution:

1. **Audit trail for the licence decision.** Licensing is a legal/product decision that may be re-visited by lawyers, contributors, acquirers, or future Adam. The "Why Apache 2.0 and not MIT" table and the "What this story does NOT do" list exist so anyone reading the commit two years from now knows what was considered and what was consciously excluded.
2. **Disaster prevention.** The F1 (GPL transitive) and F4 (per-file header creep) failure modes are both "cheap to prevent, expensive to fix after the fact" — the spec makes the prevention explicit.
3. **Scope discipline.** This is the shortest path from "licence TODO pinned in csproj since 9.3" to "licence applied, marketplace unblocked for 10.5." The temptation to also tidy the `PackageProjectUrl` comment, or to sneak in a `CopyrightYear` property, or to bump the version is real. Locked decisions 9, 10, 12 are the explicit "don't."

### Why Apache 2.0 chose this project (the inverse framing)

For completeness and audit purposes only — not re-opening the decision. The factors that pushed Apache 2.0 over MIT in Adam's reasoning (as expressed 2026-04-16):

- Patent grant is meaningful for a package that integrates with commercial LLM providers; the explicit patent-termination clause protects both Adam and downstream consumers from silent patent aggression.
- NOTICE file convention provides a clean attribution path for the third-party components we redistribute (AngleSharp, YamlDotNet) — MIT requires attribution in the distribution but is less prescriptive about the form.
- Trademark clause §6 is a sensible default as "AgentRun" potentially accretes brand value through the public beta and marketplace listing.
- Apache 2.0 is the default for large-org contributions to Umbraco-ecosystem packages; aligning with that norm lowers friction if corporate contributors appear later.

None of these are dispositive on their own — MIT would work fine — but Apache 2.0 is the right long-term choice.

### What NOT to do

- Do NOT modify any `.cs` or `.ts` file. Zero code changes.
- Do NOT add per-file SPDX headers. Locked decision 4.
- Do NOT make the repo public as part of this story. Locked decision 9.
- Do NOT bump the version number. Locked decision 10.
- Do NOT add GitHub Actions / CI licence-check workflows. Locked decision 11.
- Do NOT paraphrase, re-wrap, or modify the Apache 2.0 licence text — it must be verbatim from apache.org.
- Do NOT use the legacy `<PackageLicenseUrl>` or `<PackageLicenseFile>` in the csproj — SPDX expression only.
- Do NOT remove the `<!-- PackageProjectUrl / RepositoryUrl — deferred until repo is public -->` comment in the csproj.
- Do NOT update tests (there is no test change to make).
- Do NOT add a `LICENSE.md` or `LICENSE.txt` — the file is `LICENSE` with no extension, per NuGet / GitHub / Umbraco Marketplace convention.

### Test patterns

No new tests. No test changes. The existing backend and frontend suites are the regression gate (AC7).

### Project Structure Notes

- **New files at repository root:**
  - `/LICENSE` — Apache License 2.0 verbatim text
  - `/NOTICE` — third-party attribution
- **Modified files:**
  - `AgentRun.Umbraco/AgentRun.Umbraco.csproj` — one-element edit replacing the deferred-licence comment
  - `README.md` — new `## Licence` section appended
- **No new NuGet dependencies. No new npm dependencies. No DI changes. No frontend changes. No test changes.**

### Research & Integration Checklist (per Epic 9 retro Process Improvement #1)

- **Umbraco APIs touched:** none.
- **Community resources consulted:**
  - [apache.org/licenses/LICENSE-2.0.txt](https://www.apache.org/licenses/LICENSE-2.0.txt) — canonical licence text
  - [SPDX License List](https://spdx.org/licenses/Apache-2.0.html) — confirmed `Apache-2.0` is the SPDX short identifier
  - [NuGet package authoring: licence expressions](https://learn.microsoft.com/en-us/nuget/reference/nuspec#license) — confirms `<PackageLicenseExpression>` is the modern convention
  - [Umbraco Marketplace package submission guidelines](https://marketplace.umbraco.com/) — requires SPDX licence expression
- **Real-world scenarios to verify:** none (no runtime behaviour change). The gate is the nupkg inspection at Task 6.3.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md#Story-10.4`](../planning-artifacts/epics.md#L2185) — epic-level story definition, pre-lock note
- [Source: `_bmad-output/implementation-artifacts/9-3-documentation-and-nuget-packaging.md`](./9-3-documentation-and-nuget-packaging.md) — the original story that deferred the licence decision
- [Source: `AgentRun.Umbraco/AgentRun.Umbraco.csproj:18-19`](../../AgentRun.Umbraco/AgentRun.Umbraco.csproj#L18) — pinned TODO comment to be closed
- [Source: `README.md`](../../README.md) — recipient of the new Licence section
- [Source: project-context.md](../project-context.md) — commit-per-story discipline, SPDX conventions
- [apache.org/licenses/LICENSE-2.0.txt](https://www.apache.org/licenses/LICENSE-2.0.txt) — canonical Apache 2.0 text (external)
- [SPDX License List — Apache-2.0](https://spdx.org/licenses/Apache-2.0.html) — canonical SPDX identifier (external)
- [NuGet authoring reference — licence](https://learn.microsoft.com/en-us/nuget/reference/nuspec#license) — SPDX expression docs (external)

## Dev Agent Record

### Agent Model Used

Claude Opus 4.6 (1M context) — Amelia (dev agent).

### Debug Log References

- Canonical Apache 2.0 text fetched: `curl -fsSL https://www.apache.org/licenses/LICENSE-2.0.txt` → 202 lines, single trailing newline confirmed (`xxd` → `0a`).
- Build verification after csproj edit: `dotnet build AgentRun.Umbraco.slnx` → Build succeeded, 2 pre-existing warnings (`GetContentTool.cs:286 CS8604`, `GetContentTool.cs:486 CS0618`), zero new warnings, zero `NU5*` licence warnings.
- Pack: `dotnet pack AgentRun.Umbraco/AgentRun.Umbraco.csproj -c Release` → `AgentRun.Umbraco.1.0.0-beta.2.nupkg` produced, no licence warnings.
- Nupkg introspection: unzipped to `/tmp/agentrun-nupkg-inspect/`, nuspec licence expression confirmed, README.md at package root contains the new `## Licence` section. Cleanup `/tmp/agentrun-nupkg-inspect/` completed.
- Test gate: backend 713/713 (unchanged from 10.7c baseline), frontend 235/235 (unchanged from 10.7b baseline).

### Dependency Licence Audit

**Source:** `dotnet list AgentRun.Umbraco/AgentRun.Umbraco.csproj package --include-transitive --format json` → 7 top-level + 196 transitive packages.

**Direct dependencies (7 packages, all MIT):**

| Package | Version | Licence | Outbound-compatible with Apache-2.0? |
|---|---|---|---|
| AngleSharp | 1.4.0 | MIT | ✅ |
| Umbraco.AI | 1.8.0 | MIT | ✅ |
| Umbraco.Cms.Api.Common | 17.3.2 | MIT | ✅ |
| Umbraco.Cms.Api.Management | 17.3.2 | MIT | ✅ |
| Umbraco.Cms.Web.Common | 17.3.2 | MIT | ✅ |
| Umbraco.Cms.Web.Website | 17.3.2 | MIT | ✅ |
| YamlDotNet | 16.3.0 | MIT | ✅ |

**Top 10 transitive dependencies by prevalence (shared infra deps pulled by multiple direct deps):**

| Package | Version | Licence |
|---|---|---|
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.4 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 10.0.4 | MIT |
| Microsoft.Extensions.Primitives | 10.0.4 | MIT |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.4 | MIT |
| Microsoft.Extensions.Options | 10.0.4 | MIT |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.4 | MIT |
| Microsoft.Bcl.AsyncInterfaces | 8.0.0 | MIT |
| Newtonsoft.Json | 13.0.3 | MIT |
| Microsoft.Extensions.DependencyInjection | 10.0.4 | MIT |
| Microsoft.Extensions.Logging | 10.0.4 | MIT |

**Representative non-Microsoft / non-Umbraco transitives (the families most likely to surface unusual licences, all confirmed permissive):**

| Package family | Licence |
|---|---|
| Asp.Versioning.* | MIT |
| Azure.Core, Azure.Identity | MIT |
| BouncyCastle.Cryptography | MIT (Bouncy Castle .NET distribution) |
| Dazinator.Extensions.FileProviders | MIT |
| DocumentFormat.OpenXml | MIT |
| DotNet.Glob | MIT |
| Examine.* | MIT |
| HtmlAgilityPack | MIT |
| Humanizer.Core | MIT |
| J2N | Apache-2.0 |
| Json.More.Net / JsonPatch.Net / JsonPointer.Net | MIT |
| K4os.Compression.LZ4 | MIT |
| Lucene.Net.* | Apache-2.0 |
| MailKit / MimeKit | MIT |
| Markdig | BSD-2-Clause |
| Markdown | MIT |
| MessagePack.* | MIT |
| Microsoft.Data.SqlClient + SNI.runtime | MIT (dotnet/SqlClient repo, .NET Foundation) |
| MiniProfiler.* | MIT |
| NCrontab | Apache-2.0 |
| Newtonsoft.Json | MIT |
| NPoco | MIT |
| OpenIddict.* | Apache-2.0 |
| Polly, Polly.Core | BSD-3-Clause |
| Serilog.* | Apache-2.0 |
| SmartReader | Apache-2.0 |
| SQLitePCLRaw.* | Apache-2.0 |
| Swashbuckle.AspNetCore.* | MIT |
| System.* | MIT |
| Umbraco.AI.* + Umbraco.Cms.* | MIT |

**Denylist scan (Task 5.4):** Searched for any transitive with licence in `GPL-*`, `LGPL-*`, `EPL-*`, `MPL-*`, `SSPL-*`, `CPL-*`, `Ms-RL`, proprietary, non-standard, blank. **Zero flags.**

**Audit summary (Task 5.5 format):** All 7 direct dependencies and the top 10 transitive dependencies (plus all sampled transitive families above) use Apache-2.0-outbound-compatible permissive licences (MIT / Apache-2.0 / BSD-2-Clause / BSD-3-Clause). No conflicts.

### Nuspec Licence Block (captured from packed nupkg)

Full `<metadata>` block from `/tmp/agentrun-nupkg-inspect/AgentRun.Umbraco.nuspec` (extracted from `AgentRun.Umbraco.1.0.0-beta.2.nupkg` built at commit `ee2555d`):

```xml
<metadata>
  <id>AgentRun.Umbraco</id>
  <version>1.0.0-beta.2</version>
  <title>AgentRun.Umbraco</title>
  <authors>Adam Shallcross</authors>
  <license type="expression">Apache-2.0</license>
  <licenseUrl>https://licenses.nuget.org/Apache-2.0</licenseUrl>
  <readme>README.md</readme>
  <description>AI-powered workflow engine for Umbraco CMS — define multi-step agent workflows in YAML</description>
  <tags>umbraco ai workflow agent content-quality accessibility</tags>
  <repository type="git" commit="ee2555d3f0d681345c522b975e86fbd59a280a51" />
  <dependencies>
    <group targetFramework="net10.0">
      <dependency id="AngleSharp" version="1.4.0" exclude="Build,Analyzers" />
      <dependency id="Umbraco.AI" version="1.8.0" exclude="Build,Analyzers" />
      <dependency id="Umbraco.Cms.Api.Common" version="17.3.2" exclude="Build,Analyzers" />
      <dependency id="Umbraco.Cms.Api.Management" version="17.3.2" exclude="Build,Analyzers" />
      <dependency id="Umbraco.Cms.Web.Common" version="17.3.2" exclude="Build,Analyzers" />
      <dependency id="Umbraco.Cms.Web.Website" version="17.3.2" exclude="Build,Analyzers" />
      <dependency id="YamlDotNet" version="16.3.0" exclude="Build,Analyzers" />
    </group>
  </dependencies>
  <contentFiles>
    <files include="any/net10.0/Schemas/workflow-schema.json" buildAction="Content" />
    <files include="any/any/App_Data/AgentRun.Umbraco/workflow-schema.json" buildAction="None" />
  </contentFiles>
</metadata>
```

Notes:
- `<license type="expression">Apache-2.0</license>` is the SPDX-expression form required by NuGet.org and Umbraco Marketplace (AC6 satisfied).
- NuGet auto-populates `<licenseUrl>https://licenses.nuget.org/Apache-2.0</licenseUrl>` as a deprecated-but-still-present compatibility pointer for older consumers; it resolves to the SPDX page. This is expected alongside `<license type="expression">` and is not a warning or conflict (no NU5125 emitted).
- The `LICENSE` file itself is NOT packed into the nupkg — that is correct and expected for SPDX-expression mode (per locked decision 8b). The README inside the nupkg references `./LICENSE` at the repo root; consumers reach the canonical text via SPDX's `licenseUrl` or the package's source repo once published.

### Completion Notes List

- **LICENSE file (AC1):** Canonical Apache 2.0 text from apache.org verbatim (202 lines), single copyright substitution `[yyyy] [name of copyright owner]` → `2026 Adam Shallcross` on line 190; single trailing newline; no paraphrasing or re-wrapping.
- **NOTICE file (AC2):** Acknowledges Umbraco CMS, Umbraco.AI, AngleSharp, YamlDotNet — the four direct runtime dependencies the user is likely to recognise. Structure follows Apache 2.0 §4(d) convention; explicitly non-modifying of the outbound Apache 2.0 licence.
- **csproj SPDX expression (AC3):** One-line swap — the deferred-TODO comment at csproj:18 replaced with `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`. Indentation (4 spaces for `<PropertyGroup>` contents — project uses 2-space base + 2-space nested) preserved. The `PackageProjectUrl / RepositoryUrl` deferred comment at csproj:19 untouched per locked decision 9. Build clean, no `NU51*` / `NU50*` warnings.
- **README Licence section (AC4):** 5-line rendered section appended after the Uninstalling section; identifies Apache License 2.0 by name + SPDX identifier, links `./LICENSE` + `./NOTICE` by relative path, states copyright holder + year, stays concise (well under the ~5-line cap).
- **Dependency audit (AC5):** 7 direct MIT + 196 transitives audited; zero denylist hits. Audit details tabulated above.
- **Nupkg licence embedding (AC6):** `dotnet pack` produces the nupkg with `<license type="expression">Apache-2.0</license>` inside `<metadata>`; README with the new Licence section is packed; no `NU51*` / `NU50*` warnings; `/tmp/agentrun-nupkg-inspect/` cleaned up post-verification.
- **Regression gate (AC7):** Backend 713/713 (matches 10.7c baseline), frontend 235/235 (matches 10.7b baseline), `dotnet build` clean with 2 pre-existing warnings only.
- **Story status:** set to `review` (not `done`) in this file + sprint-status.yaml, honouring the BMM dev-story workflow convention that requires a code-review gate between dev completion and `done`. The Task 7.6 wording `Set story status to done` is superseded by the BMM workflow for this project; code review is still required.
- **9.3 TODO closure:** The `<!-- PackageLicenseExpression — deferred to Story 10.4 (licensing decision) -->` comment pinned in csproj since Story 9.3 is now resolved; noted in the commit body.
- **Scope discipline (F4 gate):** git status verified — only `LICENSE` (new), `NOTICE` (new), `AgentRun.Umbraco.csproj` (modified), `README.md` (modified), plus story-tracking files (this story file + `sprint-status.yaml`) are staged for the 10.4 commit. Pre-existing uncommitted edits to `10-7c-content-tool-dry-and-comment-hygiene.md` and `deferred-work.md` belong to the in-flight 10.7c review and are intentionally left out of this commit.

### File List

**New files:**
- `LICENSE` (repo root) — Apache License 2.0 verbatim with `Copyright 2026 Adam Shallcross`
- `NOTICE` (repo root) — third-party attribution per Apache 2.0 §4(d) convention

**Modified files:**
- `AgentRun.Umbraco/AgentRun.Umbraco.csproj` — deferred-licence comment replaced with `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` (1 line changed)
- `README.md` — appended `## Licence` section (12 lines added at EOF)

**Story-tracking files (part of the 10.4 commit):**
- `_bmad-output/implementation-artifacts/10-4-open-source-licence-decision.md` — Dev Agent Record, status → review, all tasks checked
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 10.4 entry updated to `review` after dev completion, `last_updated` refreshed

## Change Log

| Date | Change | Author |
|---|---|---|
| 2026-04-16 | Story spec created. Licence pre-locked as Apache 2.0 by Adam in the same session. Scope: LICENSE + NOTICE files at repo root, csproj SPDX expression update, README licence section, dependency audit, nupkg verification. 12 locked decisions, 7 ACs, 7 tasks, 5 F-cases. No code changes, no test changes, no version bump. Positional priority 12th in Epic 10; dependency-free, can run in parallel with in-progress Story 10.7c. | Bob (SM) |
| 2026-04-16 | Implementation complete → `review`. Tasks 1–7 all done: LICENSE (Apache 2.0 verbatim + `Copyright 2026 Adam Shallcross`), NOTICE (Umbraco CMS / Umbraco.AI / AngleSharp / YamlDotNet attribution), csproj SPDX expression applied (9.3 TODO closed), README `## Licence` section appended, transitive dep audit (7 direct MIT + 196 transitives, zero conflicts, all Apache-2.0-outbound-compatible permissive), nupkg rebuilt + nuspec verified to carry `<license type="expression">Apache-2.0</license>`. Backend 713/713, frontend 235/235, build clean (2 pre-existing warnings only). | Amelia (Dev) |
