# Decisions

## Recent Decisions
> Older entries archived to `decisions-archive.md` (entries >7d removed when file >= 50KB).

### 2026-05-13: Accept JsonConverter + TransformSchemaNode pattern as the standard workaround for MCP SDK reflection-binding gaps
**By:** Farnsworth (review of PR #222 by youyuanwu, requested by Steven)
**What:** When a CLR type cannot be bound by the MCP SDK's default System.Text.Json reflection (e.g. `SwitchParameter` — struct with getter-only `IsPresent`), the accepted fix is:
1. A dedicated `JsonConverter<T>` in `PoshMcp.Server/PowerShell/`.
2. A small static support class exposing shared, frozen `JsonSerializerOptions` and `AIJsonSchemaCreateOptions` (with `TransformSchemaNode` rewriting the bad node to a permissive `anyOf`).
3. Wire both through `McpServerToolCreateOptions.SerializerOptions` / `SchemaCreateOptions` in `McpToolFactoryV2.CreateMcpToolOptions`.
**Why:** Single chokepoint, no per-parameter detection needed, schema stays honest about what the converter actually accepts. PR #222 establishes the template.
**Follow-up:** Track whether globally replacing the SDK's default `SerializerOptions` (instead of cloning + extending) introduces response-serialization regressions for tools that emit explicit nulls — `DefaultIgnoreCondition = WhenWritingNull` now applies to every tool.

### 2026-05-07: Leela — OOP docs + samples audit (PR #210)
**By:** Steven Murawski (via Leela)
**What:** Audited whether spec 004 OOP changes (default flip to `Pool`, `SubprocessHostMode` taxonomy, sizing knobs, cancellation contract) reached `./docs` and the sample `appsettings.json` files. Findings: docs had material gaps (advanced.md stale, configuration.md silent on RuntimeMode/SubprocessHostMode, azure-integration.md described RuntimeMode incorrectly as "sync/async"); samples were partial (root + PoshMcp.Server were current; `examples/appsettings.advanced.json` and `examples/appsettings.tenant.json` had no PowerShell runtime tuning despite being the heavy-Az and multi-tenant scenarios where OOP applies). Updates landed in PR #210: rewrote advanced.md OOP section with full taxonomy, sizing, cancellation contract, ProcessPool example, link to benchmark-findings.md; added Runtime Mode section to configuration.md; fixed azure-integration.md description; added `RuntimeMode: OutOfProcess` + `SubprocessHostMode: Pool` to advanced.json and `RuntimeMode: OutOfProcess` + `SubprocessHostMode: ProcessPool` (size 4, min healthy 2) to tenant.json; documented rationale in `examples/README.md`. Intentionally left alone: examples/appsettings.basic.json (purpose mismatch), PoshMcp.Server/default+modules+azure+environment-example (loaded by dev/tests, out of audit scope), README.md/DOCKER.md (already updated in #208), docs/release-notes (belongs with the shipping release). Build green.
**Why:** Source-of-truth schema (`PowerShellConfiguration.cs`) shipped Pool as the default but the user-facing docs and the two samples whose use cases are exactly what the modes exist for hadn't been updated to match. Risk was that users following the docs or copying the samples would not know the new default exists, would not know how to opt into ProcessPool for trust-boundary scenarios, and (in azure-integration.md) would read a wrong description of the RuntimeMode field.

### 2026-05-07: Cubert — Fact-check verdict on PR #210 (OOP docs + samples audit)
**By:** Steven Murawski (via Cubert)
**Verdict:** REQUEST CHANGES — three substantive errors in `docs/articles/advanced.md`. Samples and other docs check out.

**Verified ✅**
- All property names in changed docs and samples exist in `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` with the casing shown: `RuntimeMode`, `SubprocessHostMode`, `SubprocessRunspacePoolSize`, `SubprocessPoolSize`, `SubprocessMinHealthyForStartup`.
- All `SubprocessHostMode` string values used (`Single`, `Pool`, `ProcessPool`) match the enum defined in `PoshMcp.Server/PowerShell/OutOfProcess/SubprocessHostMode.cs`.
- Defaults cited match code: `SubprocessHostMode = Pool`, `SubprocessRunspacePoolSize = 0` auto-sizes to `min(ProcessorCount, 8)`, `SubprocessPoolSize = 4`, `SubprocessMinHealthyForStartup = 1`.
- Clamp claim "Clamped to `[1, SubprocessPoolSize]`" matches `Math.Min(config.SubprocessMinHealthyForStartup, Math.Max(1, config.SubprocessPoolSize))` in `McpToolSetupService.cs:214` and the doctor-warning paths in `DoctorService.cs:361,365`.
- `4.86×` warm-invoke throughput at concurrency 10 matches `specs/004-out-of-process-execution/benchmark-findings.md` §1 (table: 4.86× mean / 4.79× P99).
- "Clears the spec's per-scenario 4× bar for I/O-shaped workloads" matches the same findings file.
- "Default since 2026-05-06" matches the date on `benchmark-findings.md` and the spec-004 default-flip context.
- `examples/appsettings.advanced.json` and `examples/appsettings.tenant.json` (PR-branch versions) parse as valid JSON. advanced.json uses Pool-mode-relevant key (`SubprocessRunspacePoolSize`); tenant.json uses ProcessPool-mode-relevant keys (`SubprocessPoolSize`, `SubprocessMinHealthyForStartup`) — correct per-mode key selection.
- `examples/README.md` rationale aligns with `benchmark-findings.md` §4 recommendation (Pool for typical concurrent MCP load, ProcessPool for trust-boundary / tail-latency-sensitive workloads).
- `docs/articles/azure-integration.md` `RuntimeMode` fix uses real values from the schema (`InProcess`/`OutOfProcess`).
- `POSHMCP_RUNTIME_MODE=OutOfProcess` (PascalCase) in advanced.md is accepted by `SettingsResolver.NormalizeRuntimeModeValue`.
- No new TOC entries needed; no broken intra-doc links observed in the diff.

**Discrepancies ❌**
1. `docs/articles/advanced.md`, "Enable Out-of-Process Mode": "Unrecognized values fall back to `InProcess` with a logged error." Code does not fall back — `ConfigurationLoader.cs:50` **throws `InvalidOperationException`** ("Unsupported runtime mode '{value}'. Supported runtime modes: in-process, out-of-process.") and the server fails to start. Recommend: replace with "Unrecognized values cause the server to fail startup with `InvalidOperationException`."
2. `docs/articles/advanced.md`, "Cancellation" section, **ProcessPool** bullet: "cancellation tears down the leased subprocess; the pool spins a replacement. Other hosts are unaffected." Per PR #207 (merged 2026-05-07) and `specs/004-out-of-process-execution/cancellation-design.md` §2.3, ProcessPool now inherits soft-cancel via the new `cancel` wire frame. BeginStop is invoked inside the host; the slot **stays healthy** and is returned to the pool. Subprocess teardown is only the **backstop** for wedged hosts (unmanaged code) via the existing per-request kill-on-timeout path. Leela's text describes the backstop as if it were the normal path.
3. `docs/articles/advanced.md`, "Cancellation" section, **Single** bullet: "cancellation kills the host; the historical timeout-and-restart behavior applies." PR #207 explicitly refactored `oop-host.ps1` so the Single-mode handler runs invokes on a background dispatcher thread; cancel calls `BeginStop` on the matching `[powershell]` instance and **the host stays healthy for follow-ups** (PR #207 description, verbatim). This is pre-#207 behavior.

**Minor ⚠️**
- `docs/articles/advanced.md` "Valid values: `InProcess`, `OutOfProcess`" is incomplete. `SettingsResolver.NormalizeRuntimeModeValue` also accepts `in-process` / `out-of-process` (kebab-case) and lowercase forms. The repo's own `README.md`, `integration/README.md`, and `CliDefinition.cs:212` describe the kebab form as canonical for the env var/CLI, while `spec.md` uses the PascalCase form. Not blocking, but could mislead.

**Lockout:** Per Reviewer Rejection Protocol — strict lockout. Leela may not self-revise. Recommend Steven assigns Bender (owner of PR #207, cancellation-design.md author) to revise the cancellation bullets and the runtime-mode error-handling claim.

**Why:** External-facing docs that misstate the cancellation contract are exactly what the spec-004 default flip was gated on (`benchmark-findings.md` §6 caveat 5). Shipping these docs as-is would teach users wrong expectations about host survivability after a cancelled invoke — the property the cancellation work was created to provide.

### 2026-05-07: Farnsworth — PR #210 review (Leela — OOP docs + samples audit)

**By:** Steven Murawski (via Farnsworth, Lead / Architect)

**What:** APPROVE with one non-blocking framing nit. Architectural review of PR #210 covering mental model, framing coherence with #208 (default flip), sample-pick rationale, and operator-facing completeness. Cubert handled fact-checking in parallel; this review is scoped to architecture and framing only.

**Mental model assessment — clear.** Two-entry-point split (brief in `configuration.md`, deep-dive in `advanced.md`) avoids duplication. New operator landing on either article reaches the three-mode taxonomy with explicit "when to use" guidance, sizing knobs (pool runspaces vs pool processes vs min healthy), per-mode cancellation contract, and doctor pointer for verification. Decision narrative — `Pool` wins warm throughput (~4.86×, citing `benchmark-findings.md`), `ProcessPool` opt-in for trust/tail, `Single` legacy/bisect — matches the spec 004 study and #208 default-flip rationale exactly.

**Coherence with #208.** `RuntimeMode` correctly described as `InProcess`/`OutOfProcess` (the `azure-integration.md` "sync/async" line was a real bug; correctly fixed). `SubprocessHostMode` is presented as a primary configuration concept rather than a tuning knob — correct framing for post-default-flip docs. Cancellation is documented as a contract per mode, not a footnote — correct framing because cancellation is what made the flip safe.

**Sample-pick rationale — both correct.** `advanced.json` → `Pool` matches Pool's documented strength (concurrent warm-invoke throughput) plus the heavy-Az use case; `SubprocessRunspacePoolSize: 0` (auto-tune to `min(ProcessorCount, 8)`) is the right default for a copy-paste sample. `tenant.json` → `ProcessPool` (size 4, min healthy 2) matches ProcessPool's documented strength (per-slot crash recovery + process-level isolation between callers). The `examples/README.md` rationale names the tradeoff explicitly ("trust boundaries between callers matter more than peak throughput") — multi-tenant is exactly the workload class where peak throughput is the wrong optimization target.

**Operator completeness.** `poshmcp doctor` is referenced from `advanced.md` ("reports the resolved host mode, effective pool sizes, host-script path, and any clamp warnings under Runtime Settings"). Adequate — answers the "how do I verify my config did what I intended?" question without burying it or over-emphasizing.

**Non-blocking framing nit (one):** The Cancellation section in `advanced.md` says of `Single`: *"the historical timeout-and-restart behavior applies."* This undersells what Single mode does post-#207 — the `SingleDispatcher` worker-thread pattern landed in #207 supports the same cooperative soft-cancel contract as Pool/ProcessPool, with the per-request timeout serving as the backstop. As written, an operator could read this as "Single mode does not support cooperative cancellation," which would be inaccurate, and which would also undersell why the default flip became safe across all three modes simultaneously. Suggested follow-up phrasing: *"Single: cooperative cancellation via the dispatcher worker; the per-request timeout acts as the backstop and recycles the host on timeout."* One line. Not blocking #210.

**No architectural gaps that block.** Mental model intact, decision narrative matches engineering, sample picks match documented tradeoffs, doctor surfaced for verification.

**Comment URL:** https://github.com/usepowershell/PoshMcp/pull/210#issuecomment-4396923714

### 2026-05-07: Cubert — Re-verification verdict on PR #210 (post-Bender revision)
**By:** Steven Murawski (via Cubert)
**Verdict:** APPROVE — all three blocking findings from prior fact-check are resolved in commit `a4c9ed0`. No collateral defects introduced.

**Scope:** Re-verified `docs/articles/advanced.md` at HEAD (`a4c9ed09a395384596905aa169c3edb30ae60eb0`) on `squad/oop-docs-samples-audit`. Bender (revision author per strict-lockout rule) modified only `advanced.md` per his decision drop.

**Per-finding verdict:**

1. ✅ **`RuntimeMode` invalid-value behavior — RESOLVED.** Doc text now reads: "Unrecognized values cause the server to fail startup with `InvalidOperationException` (`Unsupported runtime mode '<value>'. Supported runtime modes: in-process, out-of-process.`)." Matches ground truth in `PoshMcp.Server/Configuration/ConfigurationLoader.cs:46-50` verbatim — the loader throws when `config.RuntimeMode == RuntimeMode.Unsupported`. No fallback path exists. The non-blocking kebab-case clarification (`in-process` / `out-of-process` accepted by env var/CLI) is folded into the same paragraph correctly.

2. ✅ **ProcessPool cancellation — RESOLVED.** Doc now describes soft-cancel via inherited `OutOfProcessHost` cancel frame as the primary path: "each leased host runs the Single-mode script and inherits the same soft-cancel via the inherited `OutOfProcessHost` cancel frame. If the host honors `BeginStop`, the slot stays healthy and is returned to the pool; other hosts are unaffected. The existing per-request kill-on-timeout path in `OutOfProcessSubprocessPool` remains as a backstop for wedged hosts (e.g., a cmdlet stuck in unmanaged code) that do not honor `BeginStop` within the per-request timeout." Matches `specs/004-out-of-process-execution/cancellation-design.md` §2.3.

3. ✅ **Single cancellation — RESOLVED.** Doc now reads: "`SingleDispatcher` runs the invoke on a background dispatcher thread and calls `BeginStop` on the matching `[powershell]` instance when the cancel frame arrives. The host stays healthy for follow-up requests; the per-request timeout serves as the backstop and recycles the host only if `BeginStop` does not unwind the pipeline in time." Matches `cancellation-design.md` §2.1.

**Collateral check:** Skimmed surrounding cancellation section. The new shared-mechanism lead-in is accurate (`cancel` control frame from `OutOfProcessHost.SendRequestAsync`; cooperative `BeginStop`; .NET awaiter completes with `OperationCanceledException` immediately without waiting for host ack — matches `cancellation-design.md` §3 lines 104, 115). Pool bullet (`PoolDispatcher` looks up active `[powershell]` by request id and calls `BeginStop`, runspace returned without restart) matches §2.2 lines 46-47. No broken markdown links, no broken code fences, no new factual errors introduced. Markdown structure intact.

**CI:** All checks green on `a4c9ed0` (CodeQL actions/csharp/python, Squad CI test, submit-nuget). PR is `MERGEABLE`.

**Lockout note:** With APPROVE verdict, no further lockout triggers. PR cleared from fact-check standpoint.

### 2026-05-07: v0.11.0 minor release version bump
**By:** Amy (DevOps / Platform / Azure Engineer), requested by Steven Murawski
**What:** Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.10.0` to `0.11.0` and added a `## [0.11.0] - 2026-05-07` entry to `CHANGELOG.md`.
**Why:** Cutting a minor release. The marquee feature is the out-of-process subprocess pool (`Pool` is now the default `SubprocessHostMode`, #196), with supporting work across ProcessPool mode, `OutOfProcessHost` extraction, OOP cancellation propagation (#188), the new `PoshMcp.Benchmarks` harness, OOP fixes (`ConvertTo-Json` wrap #203, `$Error` clear #189), CWE-117 log-injection hardening in the OOP host, CI permission minimization plus `SECURITY.md`, and docs catch-up (#210, #187). Minor-version bump is appropriate — new feature surface (Pool default, ProcessPool, benchmarks) is additive but a meaningful behavior change for OOP users.
**Status:** Code change shipped (csproj + CHANGELOG). Build verified clean (`dotnet build PoshMcp.sln -c Debug` → 0 errors, only pre-existing nullable warnings). Git tag (`v0.11.0`) and push are intentionally deferred to Steven, after Cubert reviews release notes and Leela finishes `docs/release-notes/` + `SECURITY.md` work.

### 2026-05-07: v0.11.0 release notes published; SECURITY.md support matrix bumped to 0.11.x
**By:** Leela (Developer Advocate), requested by Steven Murawski
**What:**
- Created `docs/release-notes/0.11.0.md`. Lead story is OOP execution maturity: `Pool` is now the default `SubprocessHostMode` (replacing `Single`) backed by ~4.86× warm-invoke throughput at concurrency 10 in the new benchmarks harness; new `ProcessPool` topology for trust-boundary / tail-latency workloads; cancellation now propagates across the OOP boundary. Also covers `PoshMcp.Benchmarks` harness, log-sanitization (CWE-117) hardening, minimum workflow permissions, published `SECURITY.md`, and bug fixes (`ConvertTo-Json` `Content` shadowing, `$Error` clear-before-invoke). Upgrade notes call out the `Pool` default flip explicitly with an opt-out snippet to preserve `Single`.
- Updated `SECURITY.md` supported-versions table: `0.11.x` now `:white_check_mark:`, `< 0.11` now `:x:`. Replaces the prior `0.10.x` line.
**Why:** v0.11.0 is the first release where OOP `Pool` is the default — that needs an explicit, accurate upgrade story for users, and the supported-versions matrix must follow the new minor line.
**Scope:** Did not touch `CHANGELOG.md` or `PoshMcp.Server/PoshMcp.csproj` — those are Amy's. Cubert to review.

### 2026-05-07: v0.11.0 release notes review — config key error in upgrade snippets
**By:** Cubert (review of Leela's docs/release-notes/0.11.0.md)
**What:** REJECTED. Both jsonc snippets in the "Upgrade Notes" section use `"PowerShell"` as the top-level config key. The actual section name in every shipping `appsettings.json`, doc, and example is `"PowerShellConfiguration"`. Users copy-pasting the opt-out snippet would silently keep the new `Pool` default instead of restoring `Single` — defeating the entire purpose of the upgrade note.
**Why:** Verified zero matches for `"PowerShell": { ... }` carrying these properties; 30+ matches for `"PowerShellConfiguration"` as the canonical section. Confirmed against `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` (binds to the `PowerShellConfiguration` section) and all repo configs/docs.
**Rule for future release notes:** Spot-check every jsonc/json snippet's top-level keys against an actual shipping `appsettings.json` before publishing. Default-flip snippets are user-facing executable content — wrong keys are silent landmines, not cosmetic bugs.
**Other claims in v0.11.0 release notes verified accurate:** Pool default flip in code, three-mode taxonomy, sizing knobs, cancellation propagation, benchmarks harness, bug fixes (#203, #189), security hardening, SECURITY.md table update. Format matches prior release notes.
---

## Recommendation

Both PRs are ready to merge. Wave 1 infrastructure for spec 008 is complete.

### The Problem

`WebApplicationBuilder` starts with a `ConfigurationManager` that already contains the **baked-in `appsettings.json`** from the container image at `/app/server/appsettings.json`. This file has:
```json
"Authentication": { "Enabled": false, ... }
```

At line 1758 of `Program.cs`, the custom user config file (`PoshMcp/appsettings.json`, with `Enabled: true`) is added to `builder.Configuration`. In theory, later-added sources have higher priority. In practice, with `WebApplicationBuilder`'s `ConfigurationManager`, the baked-in `appsettings.json` was winning, causing:

- `authConfigValue.Enabled = false` at line 1800 → auth filters NOT registered, `WithRequestFilters` NOT set up
- `IOptions<AuthenticationConfiguration>.Value.Enabled = false` at middleware setup (line 1858-1864) → `UseAuthentication()` and `UseAuthorization()` NOT called
- `RequireAuthorization("McpAccess")` NOT applied to the MCP endpoint (inside the same `if (authConfigForMiddleware.Value.Enabled)` block)
- `AddPoshMcpAuthentication(builder.Configuration)` (line 1842) reads `Enabled: false` → returns early without registering JWT Bearer or the McpAccess policy

### Why the v0.9.2 Fix Didn't Fix This

The v0.9.2 fix addressed a **different bug**: when `Enabled: false` in config, `IOptions<AuthenticationConfiguration>` was not registered at all (the `services.Configure<T>()` call was inside the early-return guard). That fix moved `services.Configure<T>()` before the guard so IOptions always shows the real configured value.

The **current bug** is upstream: `builder.Configuration` itself returns `Enabled: false` because the base `appsettings.json` overrides the custom file. The fix was applied to the wrong layer.

### The Disconnect Between Diagnostic Tools and Runtime

`BuildRootConfiguration(configPath)` used by all diagnostic tools (`get-configuration-troubleshooting`, `get-configuration-guidance`, `BuildDoctorReportFromConfig`) is:
```csharp
var builder = new ConfigurationBuilder();
builder.AddJsonFile(configPath, ...);  // ONLY the custom file
builder.AddEnvironmentVariables();
return builder.Build();
```

This **does NOT include the base `appsettings.json`**. It only sees the custom file with `Enabled: true`. The runtime DI uses `builder.Configuration` (the `WebApplicationBuilder`'s `ConfigurationManager`) which starts with the base `appsettings.json` and has a precedence problem with the custom file.

---

## 5. The Fix

Changed `RunHttpTransportServerAsync` to build a dedicated `authRootConfig` via `ConfigurationLoader.BuildRootConfiguration(finalConfigPath, reloadOnChange: false)` — reading ONLY from the custom file and env vars, exactly like the diagnostic tools.

**Three call sites changed:**

```csharp
// NEW: build auth-specific config from custom file only
var authRootConfig = ConfigurationLoader.BuildRootConfiguration(finalConfigPath, reloadOnChange: false);

// IOptions now bound to authRootConfig (not builder.Configuration)
builder.Services
    .AddOptions<AuthenticationConfiguration>()
    .Configure(opts => authRootConfig.GetSection("Authentication").Bind(opts))
    .ValidateOnStart();

// ...

// authConfigValue from authRootConfig (not builder.Configuration)
var authConfigValue = authRootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>() ?? new();

// ...

// AddPoshMcpAuthentication reads from authRootConfig (not builder.Configuration)
builder.Services.AddPoshMcpAuthentication(authRootConfig);
```

**Result:**
- `authConfigValue.Enabled = true` → filters registered, `WithRequestFilters` set up ✓
- `IOptions<AuthenticationConfiguration>.Value.Enabled = true` → `UseAuthentication()` and `UseAuthorization()` called ✓
- `RequireAuthorization("McpAccess")` applied to MCP endpoint ✓
- JWT Bearer scheme and McpAccess policy registered ✓

**Tests:** 574 passing, 0 failing, 7 skipped.

---

## 6. Key Rule Going Forward

> **Never use `WebApplicationBuilder.Configuration` as the source for security-gate decisions when a custom config file is involved.**
>
> The `WebApplicationBuilder` default config chain always includes the baked-in `appsettings.json` which has `Authentication.Enabled: false` as a safe default. This can unexpectedly win over the custom file due to configuration precedence issues with `ConfigurationManager`. Use `ConfigurationLoader.BuildRootConfiguration(configPath)` for auth configuration — it reads only what the user explicitly configured.

---

## 7. Remaining Action Items

- [ ] **Deploy v0.9.3** with this fix. The current deployed v0.9.2 is still vulnerable.
- [ ] **Consider a regression test** verifying `authConfigValue.Enabled` is correctly read from the custom config file in an HTTP server context (Fry's domain per `fry-auth-regression-tests.md`).
- [ ] **Consider removing `Authentication.Enabled: false` from the baked-in `appsettings.json`** entirely — or at least document that the baked-in defaults are NOT for production use and will be overridden by custom configs only if there's no precedence race.


# Decision: Auth Config Source Fix — ConfigureCorsForMcp

**Date:** 2026-05-01  
**Author:** Bender  
**Commit:** 351c42c  
**Status:** Applied

## Context

After the main auth bypass fix (building `authRootConfig` via `ConfigurationLoader.BuildRootConfiguration` for IOptions and `AddPoshMcpAuthentication`), a second instance of `builder.Configuration` usage for auth settings was found in `ConfigureCorsForMcp`.

`ConfigureCorsForMcp` read `builder.Configuration.GetSection("Authentication")` to decide whether to open up CORS (`AllowAnyOrigin`) or restrict it. Because `builder.Configuration` includes the baked-in `appsettings.json` (where `Authentication.Enabled: false`), CORS would be opened wide even for deployments where the custom config had `Enabled: true` — a security gap.

## Decision

Extend `ConfigureCorsForMcp` to accept the `IConfigurationRoot authRootConfig` built from `ConfigurationLoader.BuildRootConfiguration(finalConfigPath)` and use it instead of `builder.Configuration`.

## Change

```csharp
// Before
private static void ConfigureCorsForMcp(WebApplicationBuilder builder)
{
    var authConfig = builder.Configuration.GetSection("Authentication").Get<AuthenticationConfiguration>()
        ?? new AuthenticationConfiguration();
    ...
}

// Call site
ConfigureCorsForMcp(builder);

// After
private static void ConfigureCorsForMcp(WebApplicationBuilder builder, IConfigurationRoot authRootConfig)
{
    var authConfig = authRootConfig.GetSection("Authentication").Get<AuthenticationConfiguration>()
        ?? new AuthenticationConfiguration();
    ...
}

// Call site
ConfigureCorsForMcp(builder, authRootConfig);
```

## Rationale

`authRootConfig` is the canonical auth config source for this server session — it reads only from the user-resolved config file + env vars, bypassing the WebApplicationBuilder config chain that includes the baked-in base defaults. All auth-gated decisions must use this same source.

## Verification

- `dotnet build PoshMcp.Server\PoshMcp.csproj --no-incremental`: 0 errors, 10 pre-existing warnings
- `dotnet test PoshMcp.Tests\PoshMcp.Tests.csproj`: 574 passed, 0 failed, 7 skipped

## Rule for Future Work

After any auth config source refactor, run:
```
grep -n "builder.Configuration.GetSection.*Authentication" Program.cs
```
Any remaining hits are potential auth bypass vectors.


# Decision: Always Register AuthenticationConfiguration with IOptions

**Date:** 2026-05-01
**By:** Bender (Backend Developer)
**Status:** Applied

## What

In `AuthenticationServiceExtensions.AddPoshMcpAuthentication()`, added `services.Configure<AuthenticationConfiguration>(configuration.GetSection("Authentication"))` **before** the early-return guard that exits when auth is disabled.

## Why

`IOptions<AuthenticationConfiguration>` was resolving to the default object (`Enabled = false`) throughout the application because the options system was never bound to configuration. The method used `.Get<AuthenticationConfiguration>()` for local decision-making but never called `services.Configure<>()` to wire up the DI options binding.

Three consumers were broken as a result:
- `Program.cs` (lines ~1859, ~1893): middleware and endpoint authorization guards both evaluated `false`, leaving the pipeline open to unauthenticated requests even when `Authentication.Enabled: true` in appsettings.
- `ApiKeyAuthenticationHandler.cs` (line 79): handler received a default (blank) config.
- `ConfigurationHealthCheck.cs` (line 24): health check evaluated against defaults, not real config.

## Rule Going Forward

When a service extension reads configuration via `.Get<T>()` for local logic AND consumers elsewhere depend on `IOptions<T>`, **always call `services.Configure<T>()` unconditionally** — regardless of whether the feature is enabled. The options registration must not be gated behind a feature flag because consumers may need to observe the real disabled state versus the default state.


# Decision: Show server version in doctor/troubleshooter output

**Author:** Bender (Backend Developer)  
**Date:** 2026-05-01  
**Status:** Implemented

## Decision

Add the PoshMcp server version string to both the `poshmcp doctor` CLI banner and the `get-configuration-troubleshooting` MCP tool JSON output.

## Rationale

Users and operators need to know which version of PoshMcp is running when diagnosing issues. The doctor/troubleshooter output is the natural place to surface this.

## Implementation

- Added `Version` property to `DoctorSummary` record (`DoctorReport.cs`).
- Added private `GetServerVersion()` helper to `DoctorReport` that reads `AssemblyInformationalVersionAttribute` and strips any `+{commit-hash}` suffix.
- Updated `DoctorReport.Build()` to populate `Version = GetServerVersion()`.
- Updated `DoctorTextRenderer.RenderBanner()` to show `PoshMcp v{version}` instead of `PoshMcp Doctor`.

## Version source

`typeof(DoctorReport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion`
stripped of everything after `+`.

The `.NET SDK` sets this automatically from `<Version>0.9.2</Version>` in `PoshMcp.csproj`.


# Fix: VS Code OAuth Redirect to PoshMcp `/authorize`

**Date:** 2026-05-01  
**Author:** Bender (Backend Developer)  
**Status:** Implemented — build clean, 574/574 tests pass

---

## What Was Fixed

Two authentication handler bugs that together caused VS Code to redirect to PoshMcp's own `/authorize` endpoint instead of Entra ID.

---

## Fix 1: JwtBearer — inject `resource_metadata` into `WWW-Authenticate`

**File:** `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`

**Before:** JwtBearer was configured with no `Events`, so 401 responses emitted:
```http
WWW-Authenticate: Bearer
```

**After:** Added `JwtBearerEvents.OnChallenge` that emits:
```http
WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource"
```

Key implementation details:
- `context.HandleResponse()` is called to suppress ASP.NET Core's default challenge pipeline (prevents a duplicate plain `Bearer` header being appended).
- `context.Response.StatusCode = 401` is set explicitly after `HandleResponse()`.
- The metadata URL is derived from `context.HttpContext.Request.Scheme + Request.Host` — never hardcoded.
- The `OnChallenge` block is guarded by `cfg.Value.ProtectedResource?.Resource is not null` so it only fires when PRM is configured (auth-disabled deployments are unaffected).

---

## Fix 2: ApiKeyAuthenticationHandler — fix `resource_metadata` URL construction

**File:** `PoshMcp.Server/Authentication/ApiKeyAuthenticationHandler.cs`

**Before:**
```csharp
var metadataUrl = $"{authConfig.Value.ProtectedResource.Resource}/.well-known/oauth-protected-resource";
// Produced: api://80939099-d811-4488-8333-83eb0409ed53/.well-known/oauth-protected-resource
```

**After:**
```csharp
var metadataUrl = $"{Request.Scheme}://{Request.Host}/.well-known/oauth-protected-resource";
// Produces: https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource
```

---

## Expected Post-Fix Behavior

1. Unauthenticated request hits PoshMcp
2. Server responds `401` with `WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource"`
3. VS Code reads `resource_metadata`, fetches the PRM
4. PRM returns `authorization_servers: ["https://login.microsoftonline.com/<tenant>"]`
5. VS Code fetches Entra ID metadata, discovers `authorization_endpoint`
6. Browser redirects to `login.microsoftonline.com/...` with VS Code's own `client_id=aebc6443-996d-45c2-90f0-388ff96faa56`

---

## Files Modified

| File | Change |
|------|--------|
| `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` | Added `JwtBearerEvents.OnChallenge` with `resource_metadata` header; added `using System.Threading.Tasks` |
| `PoshMcp.Server/Authentication/ApiKeyAuthenticationHandler.cs` | Fixed metadata URL to use `Request.Scheme + Request.Host` |

---

## Validation

- `dotnet build PoshMcp.Server/PoshMcp.csproj -c Release` — 0 errors, 10 pre-existing warnings (unchanged)
- `dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --no-build -c Release` — 574 passed, 0 failed, 7 skipped (pre-existing)


# Diagnosis: VS Code Redirecting to PoshMcp's Own `/authorize` Endpoint

**Date:** 2026-05-01  
**Author:** Bender (Backend Developer)  
**Status:** Diagnosis complete — awaiting fix approval

---

## The Symptom

VS Code opens a browser tab to:
```
https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/authorize
  ?client_id=80939099-d811-4488-8333-83eb0409ed53
  &response_type=code
  &code_challenge=DsFdRdRJrgNLeuzw_RsPo1Qv30blZiB0LfcPVbv2bQk
  &code_challenge_method=S256
  &redirect_uri=http%3A%2F%2F127.0.0.1%3A33418%2F
  &state=HqfYeTV%2F%2Bxr48AmWc9Wjfg%3D%3D
```

VS Code should redirect to **Entra ID** (`login.microsoftonline.com/...`), not to PoshMcp itself.

---

## Investigation Findings

### 1. What does the PRM return for `authorization_servers`?

The PRM is correctly configured in the deployed `appsettings.json`
(`C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json`):

```json
"ProtectedResource": {
  "Resource": "api://80939099-d811-4488-8333-83eb0409ed53",
  "ResourceName": "PoshMcp Server",
  "AuthorizationServers": ["https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b"],
  "ScopesSupported": ["api://80939099-d811-4488-8333-83eb0409ed53/user_impersonation"],
  "BearerMethodsSupported": ["header"]
}
```

**The PRM content itself is correct.** `authorization_servers` points to the right Entra ID tenant URL. This is NOT the bug.

### 2. Does PoshMcp have a `/authorize` endpoint?

**No.** There is no `app.MapGet("/authorize", ...)` or any route handling for `/authorize` anywhere in the codebase. The only auth-related endpoint PoshMcp maps is `/.well-known/oauth-protected-resource` via `ProtectedResourceMetadataEndpoint.MapProtectedResourceMetadata()`.

So when VS Code hits `/authorize`, it will get a 404 or fall through to the MCP handler.

### 3. Root Cause: JwtBearer 401 challenge omits `resource_metadata`

**This is the bug.** In `AuthenticationServiceExtensions.cs`, the JwtBearer scheme is configured with default options only:

```csharp
authBuilder.AddJwtBearer(name, options =>
{
    options.Authority = scheme.Authority;
    options.Audience = scheme.Audience;
    options.RequireHttpsMetadata = scheme.RequireHttpsMetadata;
    // ...
    // ← NO Events.OnChallenge configured
});
```

When an unauthenticated request hits a protected endpoint, ASP.NET Core's built-in JwtBearer handler issues a 401 with:
```http
WWW-Authenticate: Bearer
```

RFC 9728 (OAuth 2.0 Protected Resource Metadata) requires the 401 to include a `resource_metadata` parameter pointing to the PRM endpoint:
```http
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.calmstone-9cfc4790.eastus.azurecontainerapps.io/.well-known/oauth-protected-resource"
```

Without this hint, VS Code's MCP OAuth client never discovers the PRM. It falls back to treating the resource server itself as the authorization server and constructs the authorization URL as `{resourceServerBaseUrl}/authorize`.

### 4. Secondary Bug: ApiKeyAuthenticationHandler constructs a wrong `resource_metadata` URL

`ApiKeyAuthenticationHandler.HandleChallengeAsync()` does attempt to set `resource_metadata`, but it has a bug:

```csharp
// BUGGY — uses the api:// URI, not the server's HTTP base URL
var metadataUrl = $"{authConfig.Value.ProtectedResource.Resource}/.well-known/oauth-protected-resource";
// Produces: api://80939099-d811-4488-8333-83eb0409ed53/.well-known/oauth-protected-resource
```

This is not a valid HTTP URL. It uses `ProtectedResource.Resource` (the `api://` URI identifier) instead of the server's actual HTTPS base URL. This doesn't affect the current deployment (which uses JwtBearer), but would break any future ApiKey deployment.

### 5. The `client_id` discrepancy

`client_id=80939099-d811-4488-8333-83eb0409ed53` in the browser redirect is **the PoshMcp App Registration's Application ID** — the same GUID used in `"Audience": "api://80939099-d811-4488-8333-83eb0409ed53"` in the deployed config.

The documented VS Code pre-registered client ID for MCP is `aebc6443-996d-45c2-90f0-388ff96faa56`.

**Why VS Code is using `80939099-d811-4488-8333-83eb0409ed53` as its client_id:**

VS Code's MCP OAuth implementation has a fallback behavior. When it cannot resolve the authorization server via `WWW-Authenticate: Bearer resource_metadata=...`, it falls back to treating the resource server as the AS. In this fallback mode, VS Code extracts the GUID from the resource's `api://` URI and uses it as the `client_id` in the authorization request. This GUID (`80939099-d811-4488-8333-83eb0409ed53`) is exactly what's in the PRM's `resource` field.

**This is confirmation** that VS Code is in fallback mode — it found the PRM but couldn't follow the `authorization_servers` metadata path (or never got the `resource_metadata` hint to find the PRM in the first place).

---

## Root Cause Summary

**Primary cause:** `AuthenticationServiceExtensions.cs` does not configure `JwtBearerEvents.OnChallenge` to inject `WWW-Authenticate: Bearer resource_metadata="<serverBaseUrl>/.well-known/oauth-protected-resource"` into 401 responses. Without this header, VS Code cannot discover the PRM and falls back to using PoshMcp as the authorization server.

**Contributing cause:** Even the ApiKey handler's `resource_metadata` URL would be wrong (using `api://` URI instead of the server's HTTP base URL), so neither scheme currently produces a correct `WWW-Authenticate` challenge.

---

## What the Fix Should Be

### Fix 1: Add `OnChallenge` to JwtBearer configuration

In `AuthenticationServiceExtensions.cs`, configure the JwtBearer events to inject the correct `WWW-Authenticate` header:

```csharp
authBuilder.AddJwtBearer(name, options =>
{
    options.Authority = scheme.Authority;
    options.Audience = scheme.Audience;
    options.RequireHttpsMetadata = scheme.RequireHttpsMetadata;
    // ... existing config ...

    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            var authCfg = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<AuthenticationConfiguration>>();
            if (authCfg.Value.ProtectedResource?.Resource is not null)
            {
                var request = context.HttpContext.Request;
                var baseUrl = $"{request.Scheme}://{request.Host}";
                context.Response.Headers["WWW-Authenticate"] =
                    $"Bearer resource_metadata=\"{baseUrl}/.well-known/oauth-protected-resource\"";
            }
            return Task.CompletedTask;
        }
    };
});
```

**Important:** The `baseUrl` must be derived from `HttpContext.Request` (the actual server URL), NOT from `ProtectedResource.Resource` (which is an `api://` URI).

### Fix 2: Fix ApiKeyAuthenticationHandler

Replace:
```csharp
var metadataUrl = $"{authConfig.Value.ProtectedResource.Resource}/.well-known/oauth-protected-resource";
```
With:
```csharp
var request = Context.Request;
var metadataUrl = $"{request.Scheme}://{request.Host}/.well-known/oauth-protected-resource";
```

### Additional consideration: VS Code's pre-registered client_id

Once VS Code can properly discover Entra ID via the PRM, it should use its own pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`) rather than the fallback GUID. Confirm this works post-fix by verifying that:
1. The `WWW-Authenticate` header contains `resource_metadata`
2. VS Code fetches the PRM and follows `authorization_servers` to Entra ID
3. The browser redirect goes to `login.microsoftonline.com` with `client_id=aebc6443-996d-45c2-90f0-388ff96faa56`

---

## Files to Modify (when fix is approved)

| File | Change |
|------|--------|
| `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` | Add `JwtBearerEvents.OnChallenge` to inject `resource_metadata` in `WWW-Authenticate` |
| `PoshMcp.Server/Authentication/ApiKeyAuthenticationHandler.cs` | Fix `resource_metadata` URL construction to use `Request.Scheme + Request.Host` |

---

## Deployed Config Summary (for reference)

- **App Registration Application ID / Audience:** `80939099-d811-4488-8333-83eb0409ed53`
- **Tenant ID:** `d91aa5af-8c1e-442c-b77c-0b92988b387b`
- **JwtBearer Authority:** `https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b`
- **PRM `authorization_servers`:** `["https://login.microsoftonline.com/d91aa5af-8c1e-442c-b77c-0b92988b387b"]` ✅ correct
- **VS Code expected client_id:** `aebc6443-996d-45c2-90f0-388ff96faa56` (per docs)
- **VS Code actual client_id in redirect:** `80939099-d811-4488-8333-83eb0409ed53` ← fallback mode


### 1. Scope Naming Convention
- **New file** (`entra-id-mcp-auth.md`): Used `user_impersonation` as scope name example
- **Existing file** (`entra-id-auth-guide.md`): Used `access_as_server` as scope name example
- **Decision**: Keep `access_as_server` (more descriptive; already used throughout the guide for consistency)
- **Impact**: Low — both are valid; users should pick meaningful names for their use case. Consolidated guide now explicitly states this is a user-choice with guidance on granular scope design.

### 2. Protected Resource Metadata (PRM) Configuration
- **New file**: Mentioned App Service EasyAuth automatic PRM generation via `WEBSITE_AUTH_PRM_DEFAULT_WITH_SCOPES` environment variable
- **Existing file**: Covered manual `/.well-known/oauth-protected-resource` endpoint implementation for self-hosted deployments
- **Decision**: Include both approaches in the guide
- **Impact**: Informational addition. Users deploying on App Service now know they can use EasyAuth's auto-generation; self-hosted users already had guidance. No breaking changes.

### 3. VS Code Pre-Registered Client ID Authorization (Critical Missing Step)
- **New file**: Explicitly covered VS Code's pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`) and need to authorize it in "Authorized client applications"
- **Existing file**: Did not mention VS Code client authorization or this critical setup step
- **Decision**: Add as **Step 2b** in app registration setup (new step between scope creation and M2M credentials)
- **Rationale**: This is essential guidance for VS Code MCP users. Without authorizing the pre-registered client ID, users get "Dynamic client registration not supported" error with no clear fix
- **Impact**: High importance — prevents user confusion and support burden. New users will now see this step clearly

### 4. Scope Consent Model Guidance
- **New file**: Briefly mentioned consent model selection ("Admins only" vs "Admins and users")
- **Existing file**: Covered this in detail with guidance on M2M scenarios
- **Decision**: Existing guide's coverage is comprehensive; no changes needed
- **Impact**: None — existing documentation already correct

## Content Migration Summary

| Content Area | Source | Location in Consolidated Guide |
|--------------|--------|--------------------------------|
| OAuth 2.1 + RFC 9728 basics | New file | VS Code MCP Integration subsection |
| VS Code client ID authorization | New file | Step 2b (Authorize Client Applications) |
| VS Code OAuth flow explanation | New file | VS Code MCP Integration subsection |
| VS Code settings.json config | New file | VS Code MCP Integration subsection |
| Protected Resource Metadata endpoint | New file | VS Code MCP Integration subsection |
| PRM via App Service EasyAuth | New file | VS Code MCP Integration subsection |
| VS Code troubleshooting | New file | VS Code MCP Integration subsection |
| App Registration general guidance | Existing | Path A (unchanged) |
| Managed Identity guidance | Existing | Path B (unchanged) |
| Token validation & security | Existing | Token Validation & Security section (unchanged) |
| Comprehensive troubleshooting | Existing | Troubleshooting section (enhanced with VS Code errors) |

## Decision Authority

**Authority**: Leela (Developer Advocate) — documentation structure and organization

**Rationale for Keeping Existing File as Canonical**:
- More comprehensive scope (covers app registration + managed identity + security + troubleshooting)
- Better structured with clear paths and decision matrices
- Established TOC and cross-references
- More extensive testing and troubleshooting sections

**Rationale for Adding VS Code as Subsection (Not Separate Doc)**:
- Avoids link fragmentation — users looking for "Entra ID auth" now find everything in one place
- VS Code is one implementation scenario, not a separate authentication method
- Single source of truth for app registration steps (no duplication)
- Easier to maintain consistency across both general and VS Code-specific guidance

## Files Changed

- **Modified**: `docs/entra-id-auth-guide.md` (added Step 2b and VS Code MCP Integration subsection)
- **Deleted**: `docs/entra-id-mcp-auth.md` (content consolidated)
- **Updated**: `.squad/agents/leela/history.md` (added learning notes)

## Testing & Validation

- ✓ No broken cross-references (only reference was in auto-generated DOCFX summary)
- ✓ All VS Code-specific content from new file now in consolidated guide
- ✓ All app registration and managed identity content from existing file preserved
- ✓ Scope naming, terminology, and step sequence consistent throughout
- ✓ No duplicate content in final guide

## Recommendation for Future Entra ID Auth Docs

If new authentication scenarios emerge (e.g., third-party OIDC providers, custom claims mapping), add them as subsections to `docs/entra-id-auth-guide.md` rather than creating separate files. Keep the main authentication guide as the single source of truth.

If a scenario becomes large enough to warrant its own detailed guide, create a separate file and link to it from the main guide's TOC, but avoid duplication of core setup steps.


# Decision: VS Code Scope Naming Requirements

**Date:** 2026-05-01  
**Status:** RESOLVED — No changes needed  
**Owner:** Leela (Developer Advocate)  
**Stakeholder:** Steven Murawski  

## Question

After consolidating Entra ID documentation and choosing `access_as_server` as the scope name, Steven flagged a concern: Does VS Code specifically require the scope name `user_impersonation` rather than custom scope names?

## Investigation Results

### 1. VS Code OAuth Flow with MCP

VS Code's MCP client uses OAuth 2.1 with PKCE and a pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`). The flow:

1. VS Code connects to the MCP server
2. Server responds with `401 Unauthorized` + metadata URL
3. **VS Code fetches Protected Resource Metadata (RFC 9728) from the server**
4. **Metadata includes `scopes_supported` array listing available scopes**
5. VS Code requests those scopes during the OAuth flow
6. User authenticates and grants consent for the requested scopes
7. VS Code receives a token with the approved scopes

**Key insight:** VS Code does NOT hardcode scope names. It dynamically reads scope names from the server's Protected Resource Metadata endpoint.

### 2. Scope Naming Conventions

**`user_impersonation`** — Microsoft's built-in convention:
- Used for Azure service permissions: `AzureServiceManagement/user_impersonation`, `https://management.azure.com/user_impersonation`
- Indicates delegated access (acting on behalf of a user)
- Owned by Microsoft services

**`access_as_server`** — Custom scope owned by PoshMcp:
- Follows the custom scope pattern: `api://app-id/scope-name`
- Descriptive: clearly indicates delegated server access
- Fully configurable (any name works)

### 3. VS Code Compatibility

✅ **VS Code is compatible with any scope name**, as long as:
- The scope is declared in `ScopesSupported` in the Protected Resource Metadata
- The scope is authorized in "Authorized client applications" for the VS Code client ID
- The token includes the scope in its `scp` claim

No special naming convention is required.

## Decision

**Keep `access_as_server` as the scope name for PoshMcp.**

### Rationale

1. **Ownership:** PoshMcp defines and owns its custom scopes; `user_impersonation` belongs to Microsoft services
2. **Clarity:** `access_as_server` better describes the permission (delegated server access)
3. **Flexibility:** Custom scope names are fully supported by VS Code's dynamic scope discovery
4. **Standards compliance:** Follows OAuth 2.0 + RFC 9728 standards without constraint
5. **Existing compatibility:** Already implemented and working in the current documentation

## Documentation Status

✅ **No changes needed.** The current documentation is accurate:
- `access_as_server` is properly configured
- VS Code section correctly explains the Protected Resource Metadata mechanism
- Scope authorization step (Step 2b) is correct
- All troubleshooting guidance is accurate

## References

- **RFC 8414**: OAuth 2.0 Authorization Server Metadata (well-known endpoint discovery)
- **RFC 9728**: OAuth 2.0 Protected Resource Metadata (scope discovery)
- **Microsoft Entra ID scopes documentation**: Custom scopes follow pattern `api://{app-id}/{scope-name}`
- **VS Code MCP integration**: Uses RFC 9728 for dynamic scope discovery

---

**Next Steps:** None — document this finding in Leela's learnings and archive the decision.

### 2026-05-12: Bender — AuthServer metadata diagnosis (AggregateError fix)

**By:** Bender (Backend Developer)

**Problem:** VS Code reports `AggregateError: Failed to fetch authorization server metadata` when discovering the deployed PoshMcp instance.

**Root cause (primary):** `ProtectedResource.AuthorizationServers` in the deployed appsettings.json was missing the `/v2.0` suffix. Without it, Entra ID returns a v1.0 OIDC discovery document whose `issuer` is `https://sts.windows.net/{tenant}/` — which does not match the authorization_server URL (`login.microsoftonline.com`). VS Code rejects the document per RFC 8414 §3 (issuer validation).

**Root cause (secondary):** The deployed PRM response contains duplicated entries in `authorization_servers` (2x), `scopes_supported` (2x), and `bearer_methods_supported` (3x). The 2/2/3 pattern matches the constructor default of `BearerMethodsSupported = new() { ""header"" }` plus the config being bound twice. Likely caused by the custom appsettings.json being registered with the configuration pipeline more than once.

**Fix (required):** Append `/v2.0` to entries in `ProtectedResource.AuthorizationServers`.

**Fix (recommended):** Investigate the duplicate-binding cause and default `BearerMethodsSupported` to `new()` (empty) so config replacement works cleanly.

**Status:** Diagnosis only — fix not yet applied. Full diagnosis archived to the orchestration log.

**File**: `.squad/decisions/inbox/bender-authserver-metadata-diagnosis.md` (now merged)

---

### 2026-05-12: Bender — PR #211 test fixture architecture

**By:** Bender (Backend Developer)

**Decision:** For end-to-end validation of PR #211 (proxy detection + high-parameter delegate emit), use **reusable test fixtures that build real CommandInfo objects via PowerShell** (no mocking). Place them under `PoshMcp.Tests/Fixtures/` so Fry can consume them from integration tests.

**Components:**
- `ProxyTestFixtures.cs` — static factories: `CreateProxyStyledCommand()` (proxy path) and `CreateHighParameterCommand()` (17 parameters → triggers cached delegate emit, since BCL `Func<>` only goes to `Func17`).
- `Pr211IntegrationFixtureSetup.cs` — xUnit collection-fixture infrastructure with caching so fixtures are built once per collection.
- `README.md` — usage docs for teammates.

**Why real commands:** Mocked `CommandInfo` would not faithfully exercise the proxy detection (`IsImplicitRemotingProxy`) or the > 16-param delegate emit path. Tests already require a PowerShell runtime, so the cost of real fixtures is acceptable.

**Status:** Fixtures committed; Fry consumes them in the new integration tests for PR #211.

**File**: `.squad/decisions/inbox/bender-pr211-fixture-architecture.md` (now merged)

---

### 2026-05-12: Recommend trait-based phasing + resource hygiene audit for test suite consistency (spec 009)

**By:** Farnsworth (Lead/Architect), with Fry (Tester)
**For:** Steven Murawski (Brady)

**Decision:** For spec 009 (test suite consistency), recommend **Option 1 (trait-based phasing via `[Trait(""Category"", ...)]`)** as the first step, combined **in parallel** with **Option 3 (per-test resource hygiene audit — dynamic ports, GUID temp dirs, deterministic subprocess teardown)**. Defer Option 2 (separate test projects) until trait-based phasing has run in CI for two weeks. Hold Option 4 (drain fixtures) as a targeted follow-up only if specific categories remain flaky after Option 3 lands.

**Why:**
- The hard user requirement — *""all unit tests should always be able to be run and run quickly""* — is unblocked fastest by traits + a documented `dotnet test --filter` command. Days, not weeks.
- Traits alone only isolate flakes; they don't fix them. The hygiene audit addresses the actual root cause (port reuse races, pwsh handle leaks, temp-dir collisions across a 6-minute serial run).
- A project split is the likely correct end state but the wrong first step. Run with traits first, learn which boundaries actually matter, then split along validated lines.
- Drain fixtures are a sharpened tool for a specific shape of failure — predict less, measure more.

**Non-goals reaffirmed:** No test rewrites, no skipping tests, no framework change, no re-enabling parallelism, no benchmark changes.

**File**: `specs/009-test-suite-consistency/spec.md` (spec authored)

---

---

# Spec 009 acceptance — open questions resolved

**Date:** 2026-05-12
**By:** Farnsworth (Lead/Architect)
**Requested by:** Brady

## Decision

Spec 009 (Test Suite Consistency and Fast Unit Tier) moves from **Proposed** to **Accepted**. All seven open questions are resolved.

## Resolutions

1. **OQ-1 — Reference machine for < 60s target.** Maintainer's primary dev machine is the reference. Documented in **FR-419**.
2. **OQ-2 — Default category for untagged tests.** Permissive — untagged tests fall back to a default bucket (not `Unit`). No strict analyzer required at this stage. Documented in **FR-417**.
3. **OQ-3 — Functional/* classification.** Rule, not case-by-case: Functional = exercises multiple areas of code, no external resources. Any test that touches disk, network, files, subprocesses, or ports is `Integration` (or `OutOfProcess` / `Http`). Documented in **FR-416**. Existing `Functional/*` tests get audited under Issue 9.
4. **OQ-4 — Azure credentials in CI.** Deferred. Skip-when-no-creds locally remains in scope; CI-side Azure execution is a future task. Documented as a **Non-Goal**.
5. **OQ-5 — EditorConfig / analyzer to require `[Trait("Category", ...)]`.** Dropped from scope. Documented as a **Non-Goal**.
6. **OQ-6 — Option 4 cooldown duration.** Blocked on OQ-4 (Option 4 itself is deferred). Documented as a **Non-Goal**.
7. **OQ-7 — Flake-rate reporting.** Dedicated CI step that re-runs the phased suite N=5 times and emits a single flake-rate summary artifact. Documented in **FR-418**, scoped under Issue 5.

## Work plan

Milestone: **Spec 009: Test Suite Consistency** (number assigned at creation time — staging at `C:\Users\stmuraws\AppData\Local\Temp\poshmcp-spec009`).

Issues filed under the milestone:

1. Add Category traits to all tests (Option 1 baseline) — FR-400, FR-406, FR-417.
2. Reclassify misfiled `Unit/*` tests — FR-401, FR-402, FR-403, FR-414.
3. Document per-category local commands (TESTING.md) — FR-408.
4. CI: split full suite into category-scoped phases — FR-409.
5. CI: dedicated flake-rate step — FR-418 (OQ-7).
6. Resource hygiene: dynamic ports — FR-411.
7. Resource hygiene: pwsh subprocess teardown — FR-412.
8. Resource hygiene: unique temp directories — FR-403, FR-410.
9. Functional → Integration reclassification rule — FR-416 (OQ-3).
10. Measure unit-tier acceptance: <60s, 5x clean — SC-100, SC-101, FR-404, FR-405; blocked by #1, #2, #6, #7, #8.

## Trade-offs accepted

- **Permissive default bucket over strict analyzer (OQ-2/OQ-5).** Accepting the risk that a new test could land untagged and silently fall into the default bucket. Mitigation: the default bucket is documented and is explicitly **not** `Unit`, so an untagged test cannot accidentally promote itself into the fast pre-commit tier. A strict analyzer is a follow-up if untagged tests become a recurring problem.
- **Functional rule applied as a hard line, not case-by-case (OQ-3).** Reduces judgment overhead but may reclassify a test that "feels" Functional but touches disk. Trade-off accepted because case-by-case was already producing inconsistent classifications.
- **Azure-in-CI deferred (OQ-4).** CI will not catch Azure-category regressions until this is revisited. Acceptable because (a) Azure tests already skip locally without creds — same behavior in CI; (b) maintainer can run them on demand; (c) credentialed CI is a meaningful infra change that doesn't belong on this spec's critical path.
- **Drain fixture (Option 4) deferred until after Option 3 lands (OQ-6).** If hygiene audits (Issues 6, 7, 8) eliminate the flakiness, the drain fixture is unnecessary and we save complexity. If they don't, we'll have data on where leaks survive and can target the fixture precisely.

## EMU note

Milestone creation and issue creation are blocked under Farnsworth's account by the EMU policy on `usepowershell/PoshMcp` (HTTP 404 on `gh api POST` and `gh issue create`, same pattern previously logged for `gh issue create` and `gh pr review`). Staging is at `C:\Users\stmuraws\AppData\Local\Temp\poshmcp-spec009\` with a `create-all.ps1` script that creates the milestone and all 10 issues when run from a non-EMU context.

---

### 2026-05-12: OOP invoke must surface `hadErrors=true` as a thrown exception

**By:** Bender (Backend Engineer), requested by Brady
**Scope:** `PoshMcp.Server/PowerShell/OutOfProcess/` (single and pool executors)

**Bug pattern** (from a real user report): a tool call to `assert_tenant_role_member`
with an invalid role returned what looked like the previous `assert_tenant_user`
success payload, with MCP `IsError=false`. The server log showed
`warn: ... reported errors. Output: {prior-looking JSON}` followed by
`"assert_tenant_role_member" completed. IsError = False.`

**Mechanism** (NOT cross-invoke leak):
- Each invoke uses a fresh `[powershell]` instance with its own streams.
  `$Error.Clear()` already runs at the top of the OOP user script (#189 fix). So
  there is no stale `$Error` or stale stream contamination between invokes.
- The "prior-looking payload" is actually the *current* command's intermediate
  pipeline output captured by `$r = & $Name @Splat` BEFORE the command writes a
  non-terminating error to the error stream. AdvocacyBami's
  `Assert-BamiTenantRoleMember` internally calls `Assert-BamiTenantUser` (which
  emits the user object) and then writes a non-terminating error for the bad
  role. `$r` ends up bound to the user object, which gets JSON-serialized.
- The OOP host correctly reported `hadErrors=true` on the wire. The .NET-side
  `InvokeAsync` (both single and pool variants) logged a warning and returned
  the partial output unchanged. The MCP framework can only mark a tool result
  `IsError=true` when the generated method throws — a successful string return
  is always treated as success.

**Decision**: when an OOP invoke response carries `hadErrors=true` and
`cancelled=false`, `InvokeAsync` MUST throw `InvalidOperationException` with a
message of the form `OOP error: command '{name}' reported {N} error(s): {joined
errors}`. The `"OOP error:"` prefix matches the existing terminating-error path
(`OutOfProcessHost` → `tcs.TrySetException(new InvalidOperationException("OOP error: " + msg))`)
so existing test catches like `ex.Message.Contains("OOP error")` keep working.

**Explicit non-decisions**:
- `cancelled=true` is excluded from the throw. Cancellation has its own surface
  and reclassifying it as a tool error would break the cancel-in-flight path.
- A command can legitimately write to `$Error` and still produce output worth
  surfacing. Post-fix that case becomes `IsError=true`. If a future caller wants
  a tolerant variant, add a separate API — do NOT weaken this gate.
- The PowerShell-side user script is NOT changed. `$Error.Clear()` and fresh
  per-invoke `[powershell]` instances were already correct.

**Files touched**:
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs`
  (single-host `InvokeAsync` + new `ExtractErrorMessage` helper)
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessSubprocessPool.cs`
  (pool `InvokeAsync` + new `ExtractInvokeErrorMessage` helper)
- `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` — new test
  `Invoke_WithErrorAfterSuccess_DoesNotReturnPreviousOutput`

**Test gate**: 40/40 `Category=OutOfProcess` tests pass.

**Why this matters for future bug reports**: when a user describes "the previous
command's output leaked into the next command", check `hadErrors` plumbing
first. The OOP runspace is shared by design (so `Connect-AzAccount` state
persists across invokes), so any "stale state" claim should be cross-checked
against the actual single-invoke output shape before chasing leak hypotheses.

---


### 2026-05-12: OOP cross-invoke output leak — investigated, could not reproduce

**By:** Bender (revisiting prior incomplete diagnosis)
**Requested by:** Steven Murawski

**What:** Steven reported a cross-invocation state leak in OOP PowerShell execution: a
command that initially returned `null` started returning *prior commands' output* after
other invokes ran. He explicitly rejected the prior diagnosis (which addressed a related
but distinct hadErrors-not-propagated bug) and asked for the real leak to be found and
fixed — with reproduction required FIRST.

**Investigation:**
- Reviewed all OOP source files for shared mutable state across invokes:
  - `oop-host.ps1`, `oop-host-pool.ps1`: fresh `[powershell]` per invoke, local-scoped
    `$r`, `$Error.Clear()` at top of user script. No `$script:`/`$global:` output buffer.
  - `OutOfProcessHost.cs`: `_pending` keyed on per-request Guid, removed on completion.
  - `OutOfProcessCommandExecutor.cs` + `OutOfProcessSubprocessPool.cs`: no result cache;
    `_cachedSchemas` is discover-only; `_lastSetupConfig` is restart-only.
  - No mutable static fields in OOP module.
- Built two new regression tests reproducing Steven's exact sequence (empty-returning
  cmd → producing cmd → rerun empty cmd, assert null) on both Single and Pool hosts.
  Both PASS on current main (HEAD `273bc3b`).
- Full `Category=OutOfProcess` suite: 46/46 PASS.

**Outcome:** Could NOT reproduce a framework-level cross-invoke output leak. Per
Steven's directive ("do NOT push a speculative fix"), no production code changed.

**Committed:** 2 new regression tests as permanent guards. They will fail loudly if a
real cross-invoke leak is ever introduced.

**Hypotheses NOT chased (would need Steven's exact command list to verify):**
1. A user-module's own `$script:`-scoped state leaking across invokes (out of framework
   scope to detect or fix).
2. Subprocess restart/reconnect path with overlapping calls.
3. A specific parameter-binding shape in the tool generator.

**Why:** The user's stated observation requires real time-separated state survival —
the prior diagnosis's "current-invoke partial pipeline output" cannot explain
"command returned null first, then started returning later commands' output". Without
a faithful reproduction, any production change would be speculative.

**Files changed:**
- `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` (test added)
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` (test added)


# Decision — OOP user-script defensive scope (defense-in-depth)

**Date:** 2026-05-12
**By:** Bender (Backend Developer)
**Requested by:** Steven Murawski (Brady)
**Status:** Applied — commit e1c923e on main

## Context

Brady reported a deployed poshmcp-web v0.12.2 returning byte-for-byte
identical payloads from two sequentially invoked, structurally unrelated
MCP tools (`get_tenant_context` then `assert_tenant_role_member`). The
v0.12.2 server pre-dates commit 6908917 ("fix(oop): clear per-invoke
state so errors don't return prior output"), which converts an invoke
that reports `hadErrors=true` into a thrown `InvalidOperationException`
that MCP surfaces as `IsError=true`. On v0.12.2 the same condition logs
a warning and returns the partial pipeline output as a successful tool
result.

The earlier (2026-05-12) repro round on current main HEAD used a single
script body with different parameters at pool size 2 across 6 iterations
and could not reproduce a framework-level cross-invoke output leak. The
honest disposition recorded at the time was: I cannot reproduce, and I
will not push a speculative fix.

## What was done in this round

1. **Production-shape repro test.** Added
   `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak` to
   `OutOfProcessPoolHostIntegrationTests.cs`. The new test mirrors the
   deployed configuration (runspacePoolSize=10) and uses TWO structurally
   different commands per iteration over 50 iterations:
   - **A:** `Write-Output -InputObject <per-iteration sentinel>` — a
     fresh sentinel per iteration; asserts the response contains the
     current sentinel and NO prior sentinel.
   - **B:** `Write-Verbose -Message <iteration tag>` — returns nothing;
     asserts the response equals the canonical `"null"` payload and
     contains NO prior sentinel.
   The test passes on current main HEAD even without the defensive
   change below, confirming the framework-level `$r`-leak hypothesis is
   not what produces the user's reported symptom.

2. **Defensive change.** Updated both `oop-host.ps1` and
   `oop-host-pool.ps1` to call `AddScript($userScript, $true)` instead
   of `AddScript($userScript)`. With `useLocalScope=$true` the script
   body runs in a child scope of the runspace's default scope, so the
   per-invoke working variable `$r` is discarded when the pipeline
   returns instead of living at runspace scope where the next invoke on
   the same leased runspace could observe it. The per-pipeline
   `Streams.Error` and `HadErrors` flags are unaffected because they
   live on the `[powershell]` instance, not the runspace scope chain.

3. **First attempt rejected.** An initial version of this change also
   wrapped the call site in an inner `& { ... }` scriptblock for
   redundant child-scope isolation. That broke
   `HadErrorsDoesNotLeakAcrossInvokes`: with the inner scriptblock in
   place, `Get-ChildItem -Path missing -ErrorAction SilentlyContinue`
   no longer surfaced as `HadErrors=true` on the parent pipeline
   (`Streams.Error` remained populated but the boolean flag flipped).
   The single-layer `useLocalScope=$true` change passes all prior tests
   and gives the same structural defense without the side effect on
   non-terminating-error reporting.

## Reproduction outcome

- **Did I reproduce a framework-level cross-invoke output leak locally?**
  No. Two iterations of repro design (the prior 2026-05-12 attempt at
  pool=2 / 6 iterations / same script with different params, and this
  round's pool=10 / 50 iterations / different scripts) both ran clean
  on current main HEAD.
- **Did I land a fix anyway?** Yes, explicitly as defense-in-depth
  rather than a repro-driven point fix.

## What this commit does NOT fix

- It does NOT make the production-deployed v0.12.2 server stop
  returning the deceptive tenant-context payload from
  `assert_tenant_role_member`. That requires deploying current main
  (≥ 6908917), which converts `hadErrors=true` into a thrown exception
  that MCP marks `IsError=true`. The defensive scope change applies a
  second layer to that already-merged primary fix.
- It does NOT prevent user-authored modules from setting their own
  cross-invoke state (`$global:` or `$script:` variables in the user's
  module scope). That is module behavior and the framework cannot
  contain it from outside.

## Files changed

- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` — AddScript
  call now passes `$true` (useLocalScope); explanatory comment block.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1` — same.
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` —
  added `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak`.

## Test status

- `Category=OutOfProcess`: 47 passed, 0 failed, 0 skipped.
- New production-shape test passes on current main HEAD with the
  defensive change applied.

## Commit

`e1c923e fix(oop): defensive per-invoke scope for user script`,
pushed to `main`.

### 2026-05-12: Farnsworth — Spec 010 drafted

**By:** Farnsworth (Lead / Architect)
**Requested by:** Steven Murawski

**What:** Authored `specs/010-tool-self-documentation/spec.md` — "Improve MCP Tool Self-Documentation from PowerShell Help/Metadata." Status: Draft, awaiting Brady's review (Cubert pre-review per the 2026-05-05 directive applies).

**Co-authored with Hermes** — his 2026-05-12 research entry in `.squad/agents/hermes/history.md` established the technical baseline (two-path divergence: in-process never calls Get-Help, OOP reads only Synopsis; both paths use literal `"Parameter of type X"` for every parameter description; misleading XML doc on `RemoteToolSchema.Description`).

**Scope (per Brady's clarification):** What `Get-Help`/`Get-Command`/`CommandInfo`/`ParameterMetadata` already expose, since the platform has normalized whatever help mechanism the author chose. NOT about comment-based vs MAML vs XML authoring conventions.

**Headline recommendation:** Option A — implement a shared sourcing function that reads `Get-Help` in both paths, with documented precedence chains for tool descriptions (Synopsis → Description body → syntax line → command name) and parameter descriptions (Get-Help param description → `ParameterAttribute.HelpMessage` → `ValidateSet` hint → `"Parameter of type X"` fallback). Mandates byte-identical output across in-process and OOP modes (FR-520) verified by automated test (FR-521). Includes alias exposure (FR-530/531), sanitization + length caps (FR-540..542), FR-571 caching keyed by the same setup-hash already used for OOP discovery, and FR-572 cold-start regression gate via `PoshMcp.Benchmarks`. Option D (`[PoshMcp.ToolDescription]` attribute) explicitly deferred as an opt-in follow-up.

**Open Questions** left for Brady to resolve before Accepted: alias placement, length cap defaults, MamlParaText join style, cache invalidation across runspace recycling (coordinates with spec 004), doctor field shape (coordinates with spec 006), ValidateSet description phrasing, fallback-frequency telemetry.

**Why:** Authors write PowerShell help and reasonably expect MCP clients to see it. Today's two-path divergence means the same command exposes structurally different descriptions depending on a flag (`RuntimeMode`) the author cannot see. The headline gap is real and the platform-normalized data sources to close it already exist.



### 2026-05-12: Cubert pre-review of spec 010 — APPROVE WITH CHANGES
**By:** Cubert (Fact Checker)
**Requested by:** Steven Murawski (Brady)
**Artifact:** specs/010-tool-self-documentation/spec.md (Status: Draft)
**Author:** Farnsworth (locked out from self-revision per strict-lockout rule)

**Verdict:** APPROVE WITH CHANGES. Five required changes; spec cannot promote to Accepted until they land. Recommended revision agent: Hermes (technical co-author with the original grounding research, not the original drafter).

**Verification report:**

Citations all check out (✅):
- `PoshMcp.Server/McpToolFactoryV2.cs#L123-L145` — `SetParameterSetDescription` body matches spec quote verbatim. Get-Help is never called.
- `PoshMcp.Server/McpToolFactoryV2.cs#L442` — `Description = string.IsNullOrWhiteSpace(schema.Description) ? schema.Name : schema.Description` confirmed.
- `PoshMcp.Server/PowerShell/PowerShellSchemaGenerator.cs#L98` — `schema["description"] = $"Parameter of type {parameterType.Name}";` confirmed.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1#L763-L771` — Synopsis-only read with `-ne $cmd.Name` guard confirmed.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1#L824-L832` — same logic, inline form, confirmed.
- `PoshMcp.Server/PowerShell/OutOfProcess/RemoteToolSchema.cs#L17` — XML doc text matches spec quote ("from Get-Help or parameter set syntax") and is genuinely misleading (long Description never used; OOP fallback is empty string, not syntax).

Scope discipline (✅): No FR drifts into authoring formats. Non-Goals explicitly disclaims comment-based vs MAML vs XML, matches Brady's scoping directive.

Format conformance (✅): Layout matches spec 009 exactly — Title block, Background, User Scenarios with P1/P2/P3, Edge Cases, Functional Requirements (grouped sub-sections), Approach Options, Recommendation, Open Questions, Non-Goals, Success Criteria.

**Required changes (must fix before Accepted):**

1. **FR-521 (parity test) is hand-wavy.** "Verified by an automated test that runs the same configured command through both paths and asserts equality of the resulting MCP `tools/list` responses for description fields." Doesn't say which test project (PoshMcp.Tests, presumably), doesn't name the test class or pattern, doesn't say what the equality primitive is (string-equal per field? full JSON tree? scoped to `description` only?), doesn't say what command(s) constitute the parity corpus, doesn't say how flaky-test risk is bounded if Get-Help cold-loads MAML mid-test. A reviewer can't tell if the test is implementable in 50 lines or 500. Specify: test project, naming pattern, equality scope, fixture command set (suggested: a small in-tree test module with deterministic help), and whether the test runs in both InProcess and OOP modes within a single test session.

2. **FR-550 (no description regression) has no measurement strategy.** "No tool currently producing a useful description MUST regress to a less useful one" — but "useful" is undefined and "regression" has no detection mechanism. Snapshot test against a reference module? Manual operator opt-in? Diff against baseline captured pre-change? Without a mechanism this FR cannot be verified, and "no regression" claims at release time will be unsupported. Either: (a) add a snapshot test that captures pre-change descriptions for a fixed module set and asserts post-change descriptions are equal-or-longer for non-empty originals, OR (b) tighten the FR to a verifiable property (e.g., "every command whose pre-change description is a non-empty Synopsis MUST surface that exact synopsis or a strict superset post-change"). Status quo is unfalsifiable.

3. **FR-530 punts on field placement and labels it "implementation decision".** Functional requirements must be testable. As written, FR-530 says "command aliases MUST be exposed in the MCP tool metadata" then immediately disclaims where. A test cannot assert on "exposed somewhere"; it must assert on a concrete shape. This is identical content to OQ-1 — the FR should either resolve OQ-1 inline (pick one or both options and commit) or be downgraded to a sub-bullet of OQ-1 until OQ-1 is resolved. Same critique applies to FR-531 by reference. Recommendation: resolve OQ-1 to "dedicated `aliases` array on the tool/parameter object, AND tail-of-description for clients that ignore custom fields" (covers both machine and human consumers) and rewrite FR-530/531 to cite the chosen shape. See Open Question recommendations below.

4. **FR-572 (performance gating) is concrete on threshold but vague on baseline capture.** "Regression of more than 50% on cold start triggers a redesign" — threshold is concrete ✅ and the benchmark is named (`PoshMcp.Benchmarks` cold-start scenario) ✅. But "re-run pre/post change" doesn't say where the baseline lives. Run-4 of the benchmark runs (per Hermes's findings) is the natural pre-change baseline; the FR should name the baseline artifact (e.g., `bench-runs/run-4-artifacts/` or a new captured `bench-runs/run-5-pre-spec010/`) and require the post-change run to be committed alongside as `run-N-post-spec010/` so the regression delta is reproducible from the repo, not from a developer laptop.

5. **SC-205 / SC-206 byte-identical claim needs a carve-out.** "Byte-identical between in-process and OOP modes, given identical PowerShell source loaded identically." Two paths read help in different process contexts; `Get-Help` output can include culture-dependent formatting (paragraph wrapping varies by `$Host.UI.RawUI.BufferSize`) and the OOP host runs in a fresh subprocess with potentially different `$PSDefaultParameterValues` or culture. Either: (a) add an explicit precondition "given identical culture, identical loaded modules, and identical `$PSDefaultParameterValues`" to SC-205/SC-206, OR (b) acknowledge in FR-540 (sanitization) that normalization MUST be aggressive enough to absorb host-specific formatting differences (e.g., collapse all runs of whitespace to single space, not just `\r\n`). Without one of these, the parity test in FR-521 will be flaky on Windows-vs-Linux CI agents and the spec is making an undeliverable promise.

**Suggested improvements (non-blocking, nice-to-have):**

- **Background "What authors expect" table** is excellent — concrete side-by-side of in-process vs OOP for `Get-AzContext`. Consider adding a third column showing what the spec delivers post-change so the win is unambiguous.
- **Edge case "parameter present in multiple parameter sets"** correctly mandates per-parameter (not per-set) descriptions in FR-511. Worth adding an SC for this case so the property is testable and not just declarative.
- **Sequencing step 9** says "Update `docs/articles/exposing-tools.md` (or a new `authoring-tools.md`)". Pick one — leaving the choice in the sequencing list creates a follow-up question at implementation time. Suggest committing to `docs/articles/exposing-tools.md` as the existing surface most authors will look at first.
- **Recommendation section** is strong on rationale. The "Sequencing" sub-list reads like a tasks.md preview; consider moving it to `tasks.md` when this spec promotes, leaving Recommendation focused on the architectural choice.

**Open Question recommendations (where Cubert has an opinion):**

- **OQ-1 (alias placement):** Resolve to **both** — dedicated `aliases` array on the tool/parameter object AND a `(aliases: x, y)` tail on the description. Machine readers get structure; human-only readers (clients that render only `description`) still see them. Closes FR-530/531 testability gap (Required Change 3).
- **OQ-3 (Description body assembly):** Join `MamlParaText[]` with single space, not `"\n\n"`. FR-540 already mandates collapsing embedded newlines; preserving paragraph breaks just to strip them again two FRs later is a contradiction. Single space, then sanitize, then truncate.
- **OQ-6 (ValidateSet phrasing):** Use `"One of: A, B, C"` for ≤5 values, `"One of N values: A, B, C, ..."` for >5. Including the parameter type alongside is redundant — the schema already advertises the type. Don't repeat it in description.
- **OQ-2 (length caps configurability):** Don't make configurable in v1. 1024/512 are sane defaults; configurability adds a config surface to test for marginal value. Defer until an operator asks.
- **OQ-4 (cache invalidation across runspace recycling):** Cache lives in the executor layer (above the runspace), keyed by setup-hash. Recycling a slot does NOT invalidate — the setup-hash is stable across recycles. This matches the existing OOP discovery cache pattern (per #200 review).
- **OQ-5 (doctor field naming):** Coordinate with spec 006 as the spec already says. No opinion until spec 006's doctor schema lands.
- **OQ-7 (telemetry):** Defer to a follow-up spec. Adding a metric layer is a separate concern from making the data correct in the first place.

**Per Reviewer Rejection Protocol (strict lockout):** Farnsworth drafted this spec and is locked out from revising it. Recommended revision agent: **Hermes** (provided the technical baseline research per his 2026-05-12 history entry; has independent grounding in the same code paths Farnsworth cited). Alternate: any squad member other than Farnsworth.

**Cubert.**



### 2026-05-12: Spec 010 revised — ready for re-review or promotion to Accepted
**By:** Hermes (PowerShell Expert)
**Requested by:** Brady
**Artifact:** specs/010-tool-self-documentation/spec.md (Status remains Draft — Brady promotes)
**Original author:** Farnsworth (locked out from self-revision per Reviewer Rejection Protocol strict-lockout rule)
**Reviewer:** Cubert (pre-review verdict: APPROVE WITH CHANGES, 5 required)

**What:** Revised spec 010 to address Cubert's 5 required changes and bake in all 7 of Brady's Open Question resolutions. Status stays Draft per task instructions; Brady makes the final promotion call.

**Cubert's 5 required changes — addressed:**
1. **FR-521 parity test** is now concrete: test class `PoshMcp.Tests/Integration/ToolDescriptionParityTests.cs`, fixture corpus at `PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/HelpParityFixture.psm1` (5 named functions covering each precedence step), equality scope narrowed to MCP `description` + `inputSchema.properties.<name>.description`, both modes run within a single test session, pre-warm Get-Help to bound MAML lazy-load flake.
2. **FR-550 regression** rewritten as a verifiable property + snapshot mechanism. Baseline lives at `specs/010-tool-self-documentation/baseline/{mode}-tools-list.json`. Post-change assertion: any non-empty Synopsis-sourced description must equal-or-prefix-then-`\n\n` post-change.
3. **FR-530/FR-531 removed** entirely per Brady's OQ-1 directive (skip aliases). Added Non-Goal entry. Pruned alias references from Edge Cases, SC list (SC-208/209/210 removed), Approach Options, Recommendation rationale #5, and the Sequencing list.
4. **FR-572 baseline artifact** named explicitly: `bench-runs/run-N-pre-spec010/` captured before implementation, `bench-runs/run-N-post-spec010/` committed with the implementation PR. Regression delta computed against the pre-spec010 baseline specifically.
5. **SC-205/206 byte-identical claim** carve-out resolved via Cubert's option (b) — strengthened FR-540 sanitization to collapse all whitespace runs within paragraphs to a single space while preserving `\n\n` separators, plus stripping non-printable control chars. Spec states explicitly that this normalization is what makes the byte-identical guarantee deliverable across the in-process console host and the OOP subprocess with redirected stdin/stdout.

**Brady's 7 OQ resolutions baked in (now in "Resolved Questions" section):**
- **OQ-1 aliases:** out of scope (FR-530/531 removed, Non-Goal added)
- **OQ-2 length caps:** 1024 tools / 512 params, not configurable in v1 (left a clarifying note in Resolved Questions in case Brady meant 512 for both)
- **OQ-3 description body assembly:** join `MamlParaText[]` with `\n\n`, FR-540 preserves separators
- **OQ-4 cache invalidation:** per-path resolution in FR-571 — in-process cache lives for the runspace lifetime; OOP in-subprocess cache lives until process recycle; optional .NET-side cache invalidates on setup-hash change
- **OQ-5 doctor field:** Hermes-proposed name `descriptionSource` with 4+4 string literals (FR-583)
- **OQ-6 ValidateSet phrasing:** singleton `"One of: A, B, C"` / array `"Each item is one of: A, B, C"` (FR-510 step 3)
- **OQ-7 telemetry:** FR-590 added — two OpenTelemetry counters (`poshmcp.tool_description.source`, `poshmcp.parameter_description.source`) with `step` tag matching the FR-583 vocabulary exactly

**Non-blocking suggestions also applied:**
- "What authors expect" table now has a third row showing what both paths deliver post-spec 010
- Added Scenario 3 (P3) + SC-208 covering FR-511 multi-parameter-set consistency
- Sequencing step 11 commits to `docs/articles/exposing-tools.md` (no "or new file" choice)
- Sequencing list re-headed to note detailed step-by-step belongs in `tasks.md` when promoted; numbered 1-11 with pre-change baseline captures (FR-572 bench + FR-550 snapshots) explicitly first

**Status / next:**
- Spec is Draft. Brady makes the call to promote to Accepted.
- Re-review by Cubert is optional but recommended (the 5 required changes were substantive and the structural changes — new Scenario 3, FR-583, FR-590, Resolved Questions section — warrant a second look).
- Per strict-lockout, if a re-review surfaces further required changes, Hermes is now also locked out from any subsequent revision; a third squad member would own the next pass.

**One open question for Brady (non-blocking, recorded inline in Resolved Questions OQ-2):** Brady's note "512 is reasonable" was interpreted as the parameter cap (512) with tool description cap kept at the draft's proposed 1024. If Brady intended 512 for both, flag and I'll re-revise FR-541 + Resolved Questions OQ-2.

**Hermes.**



### 2026-05-12: Bender — IToolMetadataSource seam shape (PR #238, spec 010 step 3)
# Decision: IToolMetadataSource seam shape

**Date:** 2026-05-12
**By:** Bender (#225)
**Status:** Implemented in seam; precedence implementations land in #226, #227

## Decision

Spec 010 Option A's shared sourcing seam is `IToolMetadataSource` with two
methods: `ResolveToolDescription(in ToolDescriptionRequest)` and
`ResolveParameterDescription(in ParameterDescriptionRequest)`. Both return a
result record carrying the resolved string + an enum identifying which
precedence step produced it.

## Contract

```
IToolMetadataSource
├── ToolDescriptionResult ResolveToolDescription(in ToolDescriptionRequest)
└── ParameterDescriptionResult ResolveParameterDescription(in ParameterDescriptionRequest)

ToolDescriptionRequest        ToolDescriptionResult       ToolDescriptionSource
  CommandName : string          Description : string        Synopsis
  ParameterSetName : string?    Source : enum               Description
  Synopsis : string?                                        Syntax
  LongDescription : string?                                 Name
  ParameterSetSyntax : string?

ParameterDescriptionRequest             ParameterDescriptionSource
  CommandName : string                    HelpParameter
  ParameterName : string                  HelpMessage
  ParameterTypeName : string              ValidateSet
  HelpParameterDescription : string?      TypeFallback
  HelpMessage : string?
  ValidateSetValues : IReadOnlyList<string>?
  ValidateSetAppliesToArrayElement : bool
```

Enum values map 1:1 to the FR-583 `descriptionSource` string literals so
doctor output (#228) and metrics tags (FR-590) can serialize the enum
directly (camelCase JSON convention).

## Rationale

- **Pre-resolved fields, not callbacks.** The seam never calls `Get-Help`
  itself. Each caller (in-process #226, OOP #227) populates the help fields
  from its own source and passes them in. This keeps the seam thread-safe and
  side-effect-free; both call sites can be unit-tested without a PowerShell
  runspace.
- **Request records are `readonly record struct`.** No allocation per call,
  pattern-match-friendly, immutable.
- **`in` parameters.** Avoid struct copies at call sites.
- **Two interface methods, not one.** Tool-level and parameter-level
  precedence are independent chains with different inputs (parameter has no
  syntax line, tool has no `ValidateSet`). Splitting them is clearer than a
  union request type with mode discriminators.
- **Default implementation preserves pre-spec-010 behavior byte-for-byte.**
  In-process falls through Synopsis (null) → Syntax → identical to old
  `"{name} {parameterSet.ToString()}"`. OOP path's Synopsis-when-non-empty
  rule is reproduced exactly.

## DI Wiring

`StdioServerHost` and `HttpServerHost` register
`TryAddSingleton<IToolMetadataSource, DefaultToolMetadataSource>()`. The
`TryAddSingleton` choice lets #226/#227 register their replacement
implementation earlier (or via a layered registration) without conflict.

`McpToolFactoryV2` ctors accept an optional `IToolMetadataSource?` parameter
that defaults to a fresh `DefaultToolMetadataSource` instance. This keeps the
factory usable in test contexts that don't construct a `HostApplicationBuilder`.

## Reviewer-open question

`ToolDescriptionRequest.LongDescription` is part of the contract but the
default impl ignores it. The spec assigns Get-Help long-description
*sourcing* to the caller side in #226, not the seam's behavior selection
ladder. If Farnsworth/Cubert prefer the seam itself to consume
`LongDescription` (i.e., precedence step 2 logic centralized in the seam
rather than each caller deciding what to populate), the change is a
~3-line edit to `DefaultToolMetadataSource.ResolveToolDescription`. Posed
in PR #238 body for explicit reviewer call.



### 2026-05-12: Farnsworth — Spec 010 IToolMetadataSource seam architecture verdict (PR #238)
# Farnsworth — Spec 010 IToolMetadataSource seam architecture verdict (PR #238)

**By:** Farnsworth (Lead/Architect)
**Requested by:** Steven Murawski
**Date:** 2026-05-12
**Status:** Approve (formal approval owned by Steven)

## What

Approved the architectural shape of `IToolMetadataSource` introduced in PR #238 (Bender, branch `squad/225-tool-metadata-source`, closes #225, spec 010 step 3, Option A). This is the foundational seam that wave 3 (#226 in-process Get-Help precedence, #227 OOP `RemoteToolSchema` extension) and wave 4 (#228 OOP wire-through + doctor + FR-590 metrics) plug into.

## Architectural decisions ratified

1. **Caller-side data acquisition.** Get-Help is invoked by `McpToolFactoryV2`/`PowerShellSchemaGenerator` — NOT inside the seam. The seam owns precedence rules; callers own data acquisition. This separation keeps the OOP wire-format independent: the subprocess can resolve and ship pre-resolved fields over ndjson without the seam needing to know about runspaces or processes.

2. **Two-method interface, request/result records.** `ResolveToolDescription` and `ResolveParameterDescription`, each keyed off `readonly record struct` request types. Result types carry both the resolved string and a `Source` enum.

3. **Source enums map 1:1 with FR-583 literals.** `ToolDescriptionSource` {Synopsis, Description, Syntax, Name} and `ParameterDescriptionSource` {HelpParameter, HelpMessage, ValidateSet, TypeFallback}. Enum-to-literal conversion is deferred to #228 (doctor) — correct placement.

4. **DI registration is `TryAddSingleton` in both hosts.** Stdio and HTTP host configurations both register `DefaultToolMetadataSource` via `TryAddSingleton`. `TryAdd` is the right choice — lets #226/#227 register a replacement before host configuration without conflict. Singleton lifetime is correct (default impl is stateless and thread-safe by documentation).

## Verdict

**Approve.** Both call sites (`SetParameterSetDescription` for in-process, `CreateRemoteCommandMetadataMapping` for OOP) are wired through the seam. Behavior is preserved byte-for-byte for realistic inputs in both paths. The interface is shaped so #226/#227/#228 plug in without touching it.

## Forward-compat notes for the wave 3/4 implementers

- **#226 (in-process Get-Help):** populate `Synopsis`, `LongDescription`, `HelpParameterDescription` on the request records. Resolve Get-Help once per command per discovery (FR-570). Switch `McpToolSetupService.CreateToolFactory` to constructor-injected metadata source so the `null` branch can't accidentally bypass a registered replacement.
- **#227 (OOP `RemoteToolSchema` extension):** extend the schema additively with per-parameter help fields. The OOP caller (`CreateRemoteCommandMetadataMapping`) passes them through the same request shape as in-process — no seam change needed.
- **#228 (doctor + metrics):** read `result.Source`, format as the FR-583 literal, emit on the `descriptionSource` field and as a tag on the `poshmcp.tool_description.source` / `poshmcp.parameter_description.source` counters.

## Non-blocking observations (filed for follow-up, not blocking PR #238)

- `McpToolFactoryV2` now has six constructors (3 × {with, without metadata source}). Acceptable for backward-compat during transition. Collapse in a follow-up once all callers route through DI.
- `DefaultToolMetadataSource` calls `Synopsis.Trim()`. PowerShell's Get-Help output is already trimmed for realistic inputs, and FR-540 step 1 will mandate trim anyway. Strictly closer to the spec target than the pre-change OOP behavior.

## Cubert's role

Per the 2026-05-05 user directive, Cubert pre-reviews Farnsworth plans/proposals before they reach the user. This was a PR review (not a plan/proposal), so the directive did not gate this work — Cubert fact-checked in parallel.



### 2026-05-12: Spec 010 baseline capture mechanics

**By:** Fry (issue #224, requested by Steven)

**What:**
- Pre-spec-010 `tools/list` snapshots live under `specs/010-tool-self-documentation/baseline/`. The full JSON-RPC envelope is persisted (pretty 2-space, LF), not just `result.tools`.
- The fixture module `HelpParityFixture` (FR-521) was authored as part of this baseline (PR #236) because the snapshot is meaningless without it. It exports six deterministic functions, one per FR-500/FR-510 precedence-chain rung.
- `capture-snapshots.ps1` is the canonical regen mechanism. Do NOT regenerate after spec 010 lands — the snapshots must remain pre-change to anchor the FR-550 regression test.

**Why (pre-change parity artifacts surfaced during capture; documented in baseline/README.md):**
- In-process tool count (133) ≠ OOP tool count (144) for the same configured module set. Out of scope for spec 010 (FR-551 keeps tool names stable).
- The in-process discovery path does not auto-load modules from `PSModulePath` via `Get-Command -Module`; explicit `CommandNames` are required to trigger auto-load.
- `PowerShellConfiguration.Environment` (ImportModules / ModulePaths) is wired only for the OOP path; the `PowerShellEnvironmentSetup` class exists but is not instantiated for in-process. Captured but worth a separate bug if not intentional.
- `IncludePatterns = ["*"]` is required for OOP discovery to enumerate commands from imported modules; in-process treats it as the no-filter default. Same setting in both modes produces semantically equivalent discovery.
