# Bender Work History

**Status:** 42.8 KB (checked 2026-05-11: within 90-day retention, no archival required)
**Status:** 37.6 KB (checked 2026-05-03: within 90-day retention, no archival required)

## 2026-05-18 — Issue #283 Static and noun-derived resources coexist

### What I verified / fixed

**AC-283-1 (violated — fixed):** Both `StdioServerHost.cs` and `HttpServerHost.cs` merged static + noun resource lists with `Concat` but no deduplication. If a static resource and a noun-derived resource shared the same URI, both appeared in `resources/list`. Fixed by filtering noun resources whose URI already appears in the static list (static wins), logging a `Warning` for each collision.

**AC-283-2 (already correct):** Read routing checks `resourcesConfig.Resources.Any(r => URI match)` first → static handler; fallthrough → noun handler; noun handler throws `McpProtocolException(ResourceNotFound)` when its registry has no match. No change needed.

**AC-283-3 (already correct):** `else` branch wires only `resourceHandler.HandleListAsync` / `HandleReadAsync`. No change needed.

### Key gotcha

`Resource` is ambiguous between `ModelContextProtocol.Protocol.Resource` and `OpenTelemetry.Resources.Resource` in both host files. Avoid `new List<Resource>()` — use `.Where(...).ToList()` on the typed `nounResult.Resources` so the compiler infers `ModelContextProtocol.Protocol.Resource` without any explicit mention of the short name.

### Files touched
- `PoshMcp.Server/Server/StdioServerHost.cs` — deduplication in `RegisterMcpServerServices`
- `PoshMcp.Server/Server/HttpServerHost.cs` — same pattern

## 2026-05-18 — Issue #282 OOP mode support audit for NounRegistry / McpNounResourceHandler

### What I verified / fixed

**Verified correct:**
- `McpNounResourceHandler.HandleReadAsync` OOP path calls `_commandExecutor.InvokeAsync(canonicalCommand, emptyDict, ct)`. Returns `Task<string>` (pre-serialized JSON from subprocess `output` field). Used directly as `TextResourceContents.Text`. ✅
- `ResourceLinkInjector` only intercepts `CallToolResult` output to append `EmbeddedResourceBlock`. Never touches the underlying execution path. Works in both modes without changes. ✅

**Fixed:**
1. **Constructor guard** — `McpNounResourceHandler` now throws `InvalidOperationException` at construction if both `runspace` and `commandExecutor` are null. Previously the error only surfaced at first `HandleReadAsync` call.
2. **StdioServerHost wiring** — Changed `McpNounResourceHandler` construction to `commandExecutor is null ? runspace : null` for the runspace argument, enforcing exactly-one-non-null contract.
3. **HttpServerHost wiring** — Same fix; introduced `nounExecutor` local to avoid double-evaluating `executorLease?.Executor`, then `nounExecutor is null ? sharedSessionRunspace : null`.

### Key learnings
- The "executor takes precedence" pattern in HandleReadAsync was correct but masked the wiring bug. The constructor guard catches it much earlier.
- `executorLease?.Executor` should be materialized to a local before conditionals that evaluate it twice (avoids subtle null reference if the lease disposes between evaluations, and keeps intent clear).

## 2026-05-18 — Issue #281 ResourceLinkInjectorWrapper

### What I built
`ResourceLinkInjector` static class + `ResourceLinkInjectorTool : DelegatingMcpServerTool` in `PoshMcp.Server/McpResources/ResourceLinkInjector.cs`. Wired into both `StdioServerHost.cs` and `HttpServerHost.cs` inside the `EnableNounResources` guard, immediately after `NounRegistry.Build`.

### Key SDK gotchas

1. **`CallToolResult.IsError` is `bool?`** — check as `result.IsError != true`, NOT `!result.IsError` (CS0266 otherwise).
2. **`ContentBlock.Type` is `abstract string Type { get; }`** — read-only computed property. Do NOT include `Type = "resource"` in `EmbeddedResourceBlock` object initializers (CS0200). The class already overrides it to return `"resource"`.
3. **`DelegatingMcpServerTool` constructor** — `protected DelegatingMcpServerTool(McpServerTool innerTool)`. Pass inner via `base(innerTool)`.
4. **`InvokeAsync` return type** — must be `ValueTask<CallToolResult>`, not `Task<CallToolResult>`.
5. **`tool.ProtocolTool.Title`** — use this (not `.Name`) to get the original PowerShell command name for noun extraction. `.Name` is snake_case MCP name.

### Architecture notes
- `WrapToolsWithResourceLinks` returns a new list; never mutates the input.
- In `StdioServerHost`: introduced `var toolsToRegister = tools;` local so `.WithTools(toolsToRegister)` uses the wrapped list without shadowing the parameter.
- In `HttpServerHost`: `tools` is a local `var`, so direct reassignment works.
- Tools whose noun has no registry entry (or a conflicted entry) pass through unwrapped.

## 2026-05-18 — Issue #280 McpNounResourceHandler

### What I built
`McpNounResourceHandler` in `PoshMcp.Server/McpResources/McpNounResourceHandler.cs` + wired into both server hosts.

### Key decisions & learnings

1. **Execution backend precedence**: OOP executor takes precedence over in-process runspace when both are non-null. Constructor accepts both as nullable; a guard throws `InvalidOperationException` if neither is provided. This matches how `McpResourceHandler` works for command resources (in-process only) vs the OOP invocation path.

2. **ICommandExecutor.InvokeAsync returns pre-serialized JSON**: The OOP executor returns a `Task<string>` where the string is already JSON-serialized output from `output` field in the subprocess response. No further serialization needed for the OOP path — use it directly as `TextResourceContents.Text`.

3. **Extracting discovered command names from tools list**: Use `tool.ProtocolTool.Title` (not `.Name`) — `Title` holds the original PowerShell command name (e.g. `Get-Process`); `Name` is the snake_case MCP tool name (e.g. `get_process`). Pattern: `tools.Select(t => { try { return t.ProtocolTool.Title; } catch { return null; } })`. Matches `ExtractToolIdentity` in `DoctorService.cs`.

4. **Dispatch pattern for combined handlers**: The MCP SDK's `WithListResourcesHandler`/`WithReadResourceHandler` accept only one handler each. When `EnableNounResources == true`, replace both with async lambdas. For list: concatenate static + noun lists. For read: static wins when URI matches any entry in `McpResourcesConfiguration.Resources`; everything else routes to noun handler.

5. **StdioServerHost threading**: `config` (PowerShellConfiguration) is in scope in `RunMcpServerAsync` but not passed through to `ConfigureServerServices`/`RegisterMcpServerServices`. Added optional `psConfig` and `commandExecutor` parameters to both private methods. Default-null keeps the existing call site from needing changes elsewhere.

6. **NounRegistry.Build at startup**: Build immediately after `SetupMcpToolsAsync`/`SetupHttpMcpToolsAsync` returns, inside the registration helper. No need to store the registry as a service — it's captured by the dispatch lambda closure.

7. **URI prefix stripping in HandleReadAsync**: Strip `poshmcp://resources/` prefix to get the resource name, then pass to `GetEntryByResourceName`. If the URI doesn't start with the prefix, use the raw URI as the key (defensive fallback matching static handler behavior).

## 2026-05-15: PR #266 - fix(doctor) #261 Pool-mode display

### What I fixed
Doctor report was showing `effectiveProcessPoolSize: 0` and `effectiveMinHealthyForStartup: 0` when `SubprocessHostMode = Pool`. Those knobs are inert in Pool mode (they only apply to ProcessPool), so the value was technically correct but read like a bug to operators. Changed both to render `"n/a (Pool mode)"` outside ProcessPool, mirroring the existing `EffectiveRunspacePoolSize` pattern.

### Files touched
- `PoshMcp.Server/Diagnostics/DoctorReport.cs` - promoted `EffectiveProcessPoolSize` and `EffectiveMinHealthyForStartup` from `int` to `string` (default `string.Empty`).
- `PoshMcp.Server/Diagnostics/DoctorService.cs` - refactored the inline ternaries into an explicit `if (ProcessPool) { compute and ToString } else { "n/a (Pool mode)" }` block. ProcessPool semantics unchanged (clamping + defaults preserved).
- `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs` - no change. The renderer only emits `process-pool`/`min-healthy` lines when `HostMode == ProcessPool`, so the new strings flow through cleanly.
- `PoshMcp.Tests/Unit/Diagnostics/DoctorOutOfProcessSectionTests.cs` - new, 5 tests: Pool n/a, ProcessPool integer-string, min-healthy clamping, default pool size, not-applicable.

### Test approach
`DoctorService` is internal but the test project has `InternalsVisibleTo`. `OutOfProcessSection` is a public sealed record. Called `DoctorService.BuildOutOfProcessSection` directly with synthesized `PowerShellConfiguration` instances and `NullLoggerFactory.Instance`. No FS, no process spawning - pure Unit tier.

### Gotchas
- `DoctorService` and `DoctorReport` live in the **root `PoshMcp` namespace**, not `PoshMcp.Server.Diagnostics` (despite the folder). My first test file used the folder-shaped namespace and failed to compile. Use `using PoshMcp;` not `using PoshMcp.Server.Diagnostics;`.
- `gh pr create` failed with `Unauthorized: As an Enterprise Managed User` on the `stmuraws_microsoft` account. Had to `gh auth switch -u usepowershell` first. Worth remembering for future PRs to `usepowershell/PoshMcp`.

### Outcome
PR #266 - https://github.com/usepowershell/PoshMcp/pull/266 - marked ready for review, labeled `squad` + `squad:bender`. 54 doctor tests green, full server build clean.
## 2026-05-15: Team update (via Scribe)
**Ralph round 1 — 3 PRs in-flight, may need your review:**
- **PR #266** (Bender, issue #261): Doctor pool display sentinel — EffectiveProcessPoolSize / EffectiveMinHealthyForStartup promoted to `string`, returning `"n/a (<mode> mode)"` when inert. Files: `DoctorService.cs`, `DoctorReport.cs`, `DoctorTextRenderer.cs` + Unit tests.
- **PR #264** (Hermes, issue #262): AAD v2.0 `preferred_username` mapping — added `ClaimsMapping.NameClaim` to `AuthenticationConfiguration`; wires to `JwtBearerOptions.TokenValidationParameters.NameClaimType`. Null preserves default (no behavior change for existing deployments). Files: `AuthenticationConfiguration.cs`, `AuthenticationServiceExtensions.cs`, `docs/entra-id-auth-guide.md`.
- **PR #265 DRAFT** (Farnsworth, issue #263): Spec 011 design-only — `specs/011-doctor-module-imports/spec.md` (13 FRs / 4 SCs / 5 OQs). Implementation split to follow-up issues #267 (Bender) and #268 (Hermes).

## 2026-05-15 — Spec 011 C# wiring (#267) → PR #270 [DRAFT]

**Branch:** `squad/267-doctor-module-imports-csharp` (worktree at `poshmcp-267`), stacked on `squad/268-module-discovery` (#269 — Hermes' `ModuleDiscovery` helper).

### What shipped
- `DoctorReport`: `ModuleImports` property + 4 sealed records (`ModuleImportsSection`, `ModuleImportEntry`, `PatternImportEntry`, `ToolImportEntry`) + extended `ComputeStatus` (module errors → `errors`; pattern/module warnings → `warnings`).
- `DoctorService`: two `BuildModuleImportsSection` overloads — pure-logic (test seam, takes `IReadOnlyList<ModuleProbeResult>`) + runspace-driven (production, calls `ModuleDiscovery.ProbeModules` once). Wired into both `BuildDoctorReportForCliAsync` and `BuildDoctorReportFromConfig`.
- `DoctorTextRenderer`: `RenderModuleImports` + `HasModuleImports` omit guard.
- `DoctorModuleImportsTests`: 12 unit tests (FR-263-12 cases 1-8 + 2 `ComputeStatus` flips + renderer snapshot + empty-section omit). Full Unit suite stays green: 461/461.
- CHANGELOG `## [Unreleased]` with `### Breaking` callout for the `summary.status` flip.

### Key learnings (write these down so future-Bender doesn't re-discover them)

1. **Namespace gotcha (still biting):** `DoctorReport`, `DoctorService`, `DoctorTextRenderer` all live in the ROOT `PoshMcp` namespace, NOT `PoshMcp.Server.Diagnostics`. Tests use `using PoshMcp;` (NOT `using PoshMcp.Server.Diagnostics;`). Look at `DoctorOutOfProcessSectionTests.cs` — it's the canonical pattern.
2. **McpServerTool stubbing:** `McpServerTool` is abstract, but you don't need a custom subclass. Use `McpServerTool.Create(Func<string>, McpServerToolCreateOptions { Name = "snake_case_tool", Title = "PowerShell-CommandName" })`. The `Title` field is what `McpToolFactoryV2` uses to stash the PowerShell command name — that's the field `ExtractToolIdentity` reads to recover `commandName` for FR-263-9 attribution.
3. **Attribution heuristic trade-off (must revisit in Phase 2):** Per-tool `commandName` attribution is exact (we own `config.CommandNames`). For `module`, it's a heuristic: if the config has exactly ONE `Modules` entry, all non-`commandName` tools are attributed to it. With multiple modules, non-`commandName` tools fall back to `source: "unknown"`. The clean fix needs a wire-format extension threading `sourceModule` through `RemoteToolSchema` / `PowerShellCommandMetadata`. Documented in PR body and code comments. Don't lose track of this when planning Phase 2.
4. **Pattern matching:** `PatternMatches` translates `*`/`?` to anchored regex with case-insensitive matching. Wrap in try/catch returning false on regex failure (defensive — the user-supplied pattern could be anything).
5. **Diagnostics MUST be sanitized:** Every diagnostic field that includes a user-supplied module/pattern name flows through `LogSanitizer.Scrub` (FR-263-13, CWE-117 mandate). Don't skip this even when the input "looks safe."
6. **Worktree discipline:** All build/test/commit ops from `poshmcp-267`. All `.squad/*` writes at TEAM_ROOT (`poshmcp`, not the worktree). I keep almost making this mistake.
7. **Renderer recovery:** First multi-edit on `DoctorTextRenderer.cs` accidentally collapsed the `RenderMcpDefinitions` header line into its `foreach` body. Caught by `get_errors` → fixed with one targeted `replace_string_in_file`. Lesson: always run `get_errors` after multi-edit before moving on. Cheap insurance.
8. **PR base:** Stacked on `squad/268-module-discovery` (Hermes' branch), NOT `main`. When that lands first, GitHub will auto-rebase or I'll need to retarget.

## 2026-05-15 — Spec 011 fully shipped

PRs #269 (Phase 1 ModuleDiscovery), #270 (Phase 2a DoctorService wiring), #271 (Phase 2b OOP wire-format parity) all merged to `main` on 2026-05-15. Issue #263 closed. #272 tracks per-tool source attribution refinement separately.

## Learnings

### 2026-05-18 — Issue #279 NounRegistry (Spec 012)

- **File:** `PoshMcp.Server/McpResources/NounRegistry.cs` — placed in McpResources namespace alongside `McpResourceHandler.cs` and `McpResourcesConfiguration.cs`.
- **FrozenDictionary** (from `System.Collections.Frozen`) is available on net10.0 with no extra NuGet package. Use `collection.ToFrozenDictionary(keySelector, StringComparer.OrdinalIgnoreCase)` — clean and thread-safe immutable after `Build()`.
- **Verb extraction for module-qualified commands** (`ModuleA\Get-User`): `commandName.Split('-')[0]` gives `ModuleA\Get`, NOT `Get`. Fix: extract `verbPart = commandName[..dashIndex]` then `verbPart[(lastBackslash+1)..]` to strip module prefix. Check `LastIndexOf('\\')`.
- **Conflict tracking**: use a single `Dictionary<string, NounEntry>` keyed by resource_name for winners. Conflicted entries go into `allEntries` list but not into the dictionary. Build FrozenDictionaries from `claimedByResourceName.Values` only → `GetEntry` / `GetEntryByResourceName` return null for conflicted nouns automatically.
- **AllEntries vs GetEntry**: `AllEntries` includes conflicted entries (for doctor reports); `GetEntry` / `GetEntryByResourceName` return null for conflicted nouns since they don't own a resource.
- **Resource name regex**: `@"(?<=[a-z])([A-Z])|(?<=[A-Z])([A-Z][a-z])"` replace `"_$1$2"` → handles both camelBoundary and ACRONYMBoundary. Verified: `BamiTenantUser→bami_tenant_user`, `HTMLParser→html_parser`, `Location→location`.
- **CanonicalGetCommand** is always stored as the simple `Get-{noun}` form (not module-qualified), matching spec examples.

### 2026-05-16T17:02:54.700-05:00 — Issue #272 import source tracker
- `IToolImportSourceTracker` mirrors the spec-010 description tracker shape: per-command, thread-safe, first-writer-wins, and populated during discovery rather than reconstructed later.
- In-process attribution should be recorded directly inside `McpToolFactoryV2` discovery (`GetCommandsByName`, `GetCommandsByModule`, `GetCommandsByPattern`) so no new `Get-Command`/`Get-Module` calls land on the doctor hot path.
- OOP attribution should be captured from `RemoteToolSchema.SourceModule` / `SourcePattern` / `SourceDetail`; when older hosts omit those fields, doctor must report `tools[].source = "unknown"` instead of reviving the old heuristic.
- Doctor consumption point: `PoshMcp.Server/Diagnostics/DoctorService.cs` now takes the tracker and uses it for `moduleImports.tools[]` plus module contribution counts. Wiring entry points are `PoshMcp.Server/McpToolFactoryV2.cs`, `PoshMcp.Server/Server/McpToolSetupService.cs`, and `PoshMcp.Tests/Integration/ToolImportParityTests.cs`.
## 2026-05-16 — Issue #272 assigned to Bender

**Via Farnsworth triage (22:00:11Z):**

Issue #272 "Per-tool import source attribution: introduce IToolImportSourceTracker" assigned to Bender. 

**Scope:** C# interface design task. Directly mirrors Spec 010 pattern (`IToolDescriptionSourceTracker`). No PowerShell discovery work. Builds on Spec 011 Phase 2 payload infrastructure (PR #271).

**Next:** Scope implementation approach for exact per-tool source resolution in doctor report.

### 2026-05-17T08:03:00-05:00 — Issue #277 log forging fixes
- In `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs`, `OperationContext.CorrelationId` must be treated as untrusted at log sinks; scrub once into `safeInvocationId` and reuse it for every `InvocationId` log argument.
- In `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`, JWT diagnostics need call-site scrubbing for echoed config values (`Authority`, `ValidAudiences`, `ValidIssuers`) and token-derived data (`AllClaims`, `aud`, `scp`, `roles`, decoded `aud`/`iss`, challenge errors).
- In `PoshMcp.Server/Observability/LoggerExtensions.cs`, structured logging scopes are also log-forging sinks, so `CorrelationId` needs the same `LogSanitizer.Scrub()` treatment as `OperationName`.
## 2026-05-16 — v0.14.1 Release (via Scribe)

Release v0.14.1 shipped successfully. Version bump, release notes, and GitHub release creation completed by Amy. Commit a2a89b3, tag v0.14.1 pushed to origin, release published.

