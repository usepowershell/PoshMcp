# Hermes Work History
- **20260512T210000Z**: ✓ Research — PowerShell help → MCP tool description mapping. Two distinct paths: (1) In-process (McpToolFactoryV2 + PowerShellSchemaGenerator) NEVER calls Get-Help; tool description = `"{commandName} {parameterSetSyntax}"` from `CommandParameterSetInfo.ToString()` (McpToolFactoryV2.cs L123-145); parameter description = literal `"Parameter of type {Type.Name}"` (PowerShellSchemaGenerator.cs L98). (2) Out-of-process host (oop-host.ps1 L760-771, oop-host-pool.ps1 L824-832) calls `Get-Help` and uses ONLY `.Synopsis`, falling back to empty string if synopsis equals command name; remote schema (RemoteToolSchema.cs) carries NO per-parameter description and OutOfProcessToolAssemblyGenerator.cs L304 emits parameters with name only. NOT used anywhere: `.DESCRIPTION` long body, `.EXAMPLE`, `.NOTES`, `.LINK`, `.PARAMETER <name>`, `[Parameter(HelpMessage=...)]`, parameter aliases (no AliasAttribute usage). Surprise: in-process and OOP paths produce visibly different MCP descriptions for the same command — OOP gives the SYNOPSIS sentence, in-process gives raw parameter-set syntax. Authors targeting in-process get no value from comment-based help; authors targeting OOP get value only from `.SYNOPSIS`.
- **20260403T135630Z**: ✓ Docker fixes & scripts reviews compiled and merged into decision ledger.
- **20260408T000000Z**: ✓ Reviewed/recorded deploy.ps1 hardening for transient ACR OAuth EOF failures: bounded retry loops, transient error classification, and improved failure diagnostics.
- **20260418T000000Z**: ✓ Rebased feature/002-tests onto main; resolved 5 add/add conflicts (McpResources + McpPrompts config classes, kept main implementation); removed Skip attrs from 16 integration tests (8 McpResources + 8 McpPrompts); all 16 passed; force-pushed.
# Hermes Work History
## Project Context
**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski
**Key Files:**
- `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs` - Singleton runspace management
- `PoshMcp.Server/PowerShell/PowerShellRunspaceImplementations.cs` - Runspace implementations
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` - Dynamic assembly generation
- `PoshMcp.Server/PowerShell/PowerShellCleanupService.cs` - Cleanup lifecycle
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` - Configuration model
## Pre-2026-05-06 Summary (archived to history-archive.md)
- 2026-04-03: Multi-tenant impl review (Amy) approved 9/10; PowerShell streams refactoring closed.
- 2026-04-08: Serializer migration — scalar PSObject.BaseObject leaf-value path; nested PS/CLR objects normalized before System.Text.Json.
- 2026-04-10/11: OOP execution plan filed; oop-host.ps1 created (Issue #57 phases 2-4); OOP environment customization (#67).
- 2026-04-18: Rebased feature/002-tests, resolved 5 add/add conflicts, removed Skip on 16 integration tests, all green.
- Get-Process hang analysis: ExecutePowerShellCommandTyped → ExecuteThreadSafeAsync (sync lambda) → InvokePowerShellSafe.Invoke() (no CT). Singleton runspace + SemaphoreSlim(1,1) blocks all subsequent calls. Serializer reflects ALL props on CLR objects (Process has ~50, several block on Win32). Tee-Object pipeline retains live Process objects through serialization.
- PropertySetDiscovery + serializer refinement work.
- Recovery learnings: module layout and host-script safety.
- Unserializable parameter type filtering (Issue #89).
- doctor command resolution diagnostics (Issue #91): IsolatedPowerShellRunspace per doctor call; ConfiguredFunctionStatus positional record.
### 2026-05-07: v0.11.0 release shipped (cross-agent note from Scribe)
Your work landed in v0.11.0 (csproj 0.10.0 → 0.11.0, CHANGELOG entry, release notes at docs/release-notes/0.11.0.md). The release narrative credits the OOP maturity wave: Pool default flip (#196/#208), cancellation propagation across all modes (#207), benchmarks harness + findings (#193/#194/#195/#205), OOP host extraction (#190/#198), bug fixes (#203/#189), CWE-117 log-injection hardening, minimum workflow permissions, and SECURITY.md. Tag/push deferred to Steven.

### 2026-05-12: Spec 010 revision (Reviewer Rejection Protocol — Hermes as designated revision author)
**Requested by:** Brady
**Artifact:** specs/010-tool-self-documentation/spec.md (Status: Draft, awaiting Brady's promotion to Accepted)
**Original author:** Farnsworth (locked out from self-revision per strict-lockout rule)
**Reviewer:** Cubert (APPROVE WITH CHANGES — 5 required changes)

**Cubert's 5 required changes — all addressed:**
1. FR-521 parity test specified concretely: PoshMcp.Tests/Integration/ToolDescriptionParityTests.cs, fixture corpus at PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/HelpParityFixture.psm1 with 5 named functions covering each precedence step, equality scope narrowed to `description` + `inputSchema.properties.<name>.description`, both modes within single test session, pre-warm Get-Help to bound flake.
2. FR-550 made testable: snapshot mechanism — pre-change baseline at specs/010-tool-self-documentation/baseline/{mode}-tools-list.json, post-change assertion is equal-or-superset for non-empty originals sourced from .Synopsis.
3. FR-530/FR-531 REMOVED entirely (Brady's OQ-1 directive: skip aliases). Added Non-Goal entry. Pruned alias references from Edge Cases, SC list (SC-208/209/210 removed), Approach Options A pros/cons, Approach Option B pros, Recommendation rationale #5, Sequencing step 5.
4. FR-572 baseline artifact named: bench-runs/run-N-pre-spec010/ (capture before implementation), bench-runs/run-N-post-spec010/ (commit alongside impl PR). Threshold computed against pre-spec010 baseline specifically.
5. SC-205/206 culture/host carve-out resolved via FR-540 strengthening (Cubert's option b): collapse all whitespace runs within paragraphs to single space, preserve \n\n separators, strip control chars. Spec now states explicitly this is what makes byte-identical comparison robust across in-process console host vs OOP subprocess with redirected I/O.

**Brady's 7 OQ resolutions baked in:**
- OQ-1 aliases: out of scope, FR-530/531 removed, Non-Goal added
- OQ-2 length caps: 1024 tools / 512 params, not configurable in v1 (left a clarifying note in Resolved Questions in case Brady meant 512 for both)
- OQ-3 description body: join MamlParaText[] with \n\n, sanitization preserves separators
- OQ-4 cache invalidation: per-path resolution in FR-571 (in-process: runspace lifetime; OOP: subprocess recycle, optional .NET-side setup-hash cache)
- OQ-5 doctor field: FR-583 added, field name `descriptionSource` with 4+4 string literals (synopsis|description|syntax|name for tools, helpParameter|helpMessage|validateSet|typeFallback for params)
- OQ-6 ValidateSet phrasing: singleton "One of: A, B, C" / array "Each item is one of: A, B, C"
- OQ-7 telemetry: FR-590 added, two OTel counters (poshmcp.tool_description.source, poshmcp.parameter_description.source) with `step` tag matching FR-583 vocabulary

**Cubert's non-blocking suggestions also applied:**
- Background "What authors expect" table: added third row "Both paths (post-spec 010)" with what spec 010 delivers
- Added Scenario 3 (P3) and SC-208 covering FR-511 multi-parameter-set consistency
- Sequencing step 11 commits to docs/articles/exposing-tools.md (no "or new file" choice)
- Sequencing list re-headed to note detailed step-by-step belongs in tasks.md when promoted; numbered 1-11 with pre-change baseline captures (FR-572 bench, FR-550 snapshots) explicitly first

**Open Questions section replaced with "Resolved Questions"** (matches spec 009 pattern). All 7 OQs listed with their resolutions and the FRs that bake them in.

**Section structural changes:**
- Status stays Draft (Brady promotes)
- Added "Revised: 2026-05-12 (Hermes)" line under Created
- Renumbered SCs: removed SC-208/209/210 (aliases), reused SC-208 for the new multi-parameter-set consistency check
- FR-530/FR-531 numbers gapped (removed; not renumbered to keep all back-references stable)
- Added FR-583 (doctor field naming) and FR-590 (telemetry counters); kept all other FR numbers unchanged
- Updated SC-207 to reference FR-583 literals directly instead of the informal "description-body / syntax-fallback / command-name-fallback" placeholders the draft used

**Patterns worth keeping for future specs:**
- When an FR contains "implementation decision" or "implementation choice", it's punting and not testable. Cubert's catch on FR-530 is the canonical example.
- Cross-mode byte-identical claims need either a culture/host precondition OR aggressive normalization. We chose normalization (FR-540) because it's enforced by the implementation, not by the test environment, so it survives CI environment drift.
- Doctor JSON field names should be coordinated with the metric tag vocabulary at spec time, not at impl time. FR-583 + FR-590 use the exact same string literals (synopsis|description|syntax|name and helpParameter|helpMessage|validateSet|typeFallback) so doctor output, OTel metrics, and the parity test all speak the same language.
- Snapshot tests for "no regression" claims need a concrete fixture path AND a clearly stated comparison rule (equal-or-superset, not just equal). Without both, the FR is unfalsifiable.
- Per-path cache lifetimes (in-process vs OOP) should be spelled out FR-by-FR even when the high-level rule is the same — the underlying lifecycle objects differ enough that "for the lifetime of the runspace/process" hides important nuance.


### 2026-05-12 — OOP IToolMetadataSource wiring (#228 / PR #241)
- The seam from #225 was already wired to OOP via McpToolFactoryV2.CreateRemoteCommandMetadataMapping, but only the Synopsis (schema.Description) was being passed through the ToolDescriptionRequest. The new RemoteToolSchema fields from #239 (FullDescription, HelpDescription, HelpMessage, ValidateSetValues) sat unused on the .NET side.
- Did NOT need a separate `IToolMetadataSource` impl for OOP: `HelpAwareToolMetadataSource` is already a pure, side-effect-free resolver — it does not call Get-Help itself. Both modes share the same impl; the per-mode adapter is in McpToolFactoryV2 (in-process: `BuildParameterDescriptionMap` + `SetParameterSetDescription`; OOP: new `BuildRemoteParameterDescriptionMap` + enriched `CreateRemoteCommandMetadataMapping`). Matches Bender's pattern in #226.
- Mirrored Bender's IL pattern in `OutOfProcessToolAssemblyGenerator`: added `s_descriptionAttributeCtor` + `[Description]` emission on parameters, gated by `i < commandParamCount` to skip framework params (`_AllProperties` etc) and `CancellationToken`.
- For FR-500 step 3 (parameter-set syntax fallback), the OOP host does NOT emit `CommandParameterSetInfo.ToString()` over the wire; synthesized it on the C# side from `RemoteParameterSchema` entries (`[-Param <ShortType>]` / `-Param <ShortType>` / bare `-Param` for switches). Best effort; not byte-identical to in-process syntax for complex cases.
- Snapshot verification: tool descriptions for HelpParityFixture commands match in-process (Synopsis-derived for fixtures with proper `.SYNOPSIS`; syntax-fallback for those without — both modes converge on the same string). Parameter descriptions show empty in BOTH modes for the fixtures — suggests Get-Help isn't returning param description text for the fixture, OR the MCP SDK isn't reflecting `[Description]` on the auto-schema. Either way, the OOP path now produces the same output as in-process — wiring parity achieved. Param-text resolution gaps are #229's territory.
- `oop-host.ps1` left untouched: it already emits the raw fields from #239, and the C# consumer no longer depends on `Description` being non-empty since the precedence chain handles fall-through.
- Hygiene win: kept all temp output in `\C:\Users\stmuraws\AppData\Local\Temp\hermes-228-*` — no stray `.txt` files in the worktree.


## 2026-05-13 — PR #222 review/merge: SwitchParameter MCP round-trip

- **Rebasing external PRs across spec 010**: PR #222 (youyuanwu) added converter/schema-options registration on `McpServerToolCreateOptions` in `BuildToolCreateOptions`. Spec 010 (#231/#232/#234) added `IToolMetadataSource` and OTel counter wiring nearby. Layers are orthogonal — converter/schema-options vs description-source resolution. Rebase needed zero manual conflict resolution. Lesson: when an external PR touches the same factory file as recent in-house work, check WHICH options-bag fields they each set before assuming conflict.
- **SwitchParameter MCP serialization gotcha**: `SwitchParameter` is a struct with getter-only `IsPresent`. Default System.Text.Json reflection produces `default(SwitchParameter)` regardless of payload — every `[switch]` cmdlet param silently arrived as `IsPresent=false`. The MCP SDK's auto-generated schema is also `{type:[object,null], properties:{isPresent:{type:boolean}}}`, which most clients reject when the model emits a plain bool. Fix is two-layered: `JsonConverter<SwitchParameter>` for runtime binding + `AIJsonSchemaCreateOptions.TransformSchemaNode` rewriting the schema node to `anyOf [boolean | {isPresent} | null]`. Both required — schema fix without converter still fails to bind; converter without schema fix still fails client-side validation.
- **JsonConverter registration pattern for MCP tools**: register on `McpServerToolCreateOptions.SerializerOptions` (runtime) AND `SchemaCreateOptions` (advertisement). Both need a shared static instance to avoid per-tool allocation. Note: `JsonSerializerOptions` on .NET 10 requires an explicit `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` because the MCP SDK calls `MakeReadOnly()` which throws otherwise.
- **Test coverage anchor**: 12 converter cases (Theory) + bare-STJ regression guard documenting the silent-false bug + 2 schema assertions (anyOf present, non-switch params untouched) + 5 e2e PowerShell invocations through `CreateParameterArray` + `method.Invoke` proving the runtime actually saw `IsPresent=true`. Pattern worth reusing: the regression guard verifies the broken behavior still exists in bare STJ — guarantees the converter can't be silently dropped.
- **External PR merge protocol**: never force-push to contributor's branch. Use `gh pr merge --squash --delete-branch` so GitHub does the rebase server-side and squashes to one commit. Rebasing locally is just for verification (build + test) before pulling the trigger.
### 2026-05-14: v0.13.0 released from main (tag pending CI)
**By:** Scribe (cross-agent note from coordinator)
**What:** v0.13.0 commits landed on origin/main: housekeeping `5847efb` + release `a2b9c3e` (csproj 0.12.3 → 0.13.0, CHANGELOG, docs/release-notes/0.13.0.md). Tests 777/0/7. Tag NOT yet created — pending CI green on `a2b9c3e`.
**Marquee:** Spec 010 — Help-aware tool descriptions. In-process + OOP byte-identical schemas, `IToolMetadataSource` seam, FR-500/510/540 precedence, `HelpAwareToolMetadataSource` as default, doctor `descriptionSource` reporting, OTel counters, parity tests. Includes #222 (SwitchParameter round-trip) and #248 (parameter descriptions on inputSchema).

### 2026-05-14 — #219 spec 009 temp directory hygiene
- Created `PoshMcp.Tests/Shared/TempDirectory.cs` as the canonical hygiene helper. Pattern: `using var tmp = new TempDirectory("label"); // tmp.Path`. Prefix `poshmcp-test-` + `Guid:N` ensures uniqueness; `Dispose()` is best-effort + idempotent; static `AuditLeftoverDirectories()` lets later agents sweep CI residue.
- Fixed real audit hits: `OutOfProcessCommandExecutorTests.ResolveModulePaths_DeduplicatesCaseInsensitively` (Farnsworth PR #256 flag), `ProgramTests.ResolveConfigurationPath_*` (two cases writing to bare temp root).
- Representative refactors only — did NOT touch `OopTestPaths`; it's a deliberate cross-test cache, not a hygiene violation. Document this in PRs that audit it again.
- Lesson: when changing a field type from `string` to `TempDirectory?`, ALWAYS grep usages first. I broke 12 call sites in OutOfProcessIntegrationTests and recovered with a companion `_testTempDirHolder` field plus a `string _testTempDir` view, keeping the diff small.
