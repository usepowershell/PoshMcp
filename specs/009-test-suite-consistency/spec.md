# Feature Specification: Test Suite Consistency and Fast Unit Tier

**Spec Number**: 009
**Feature Branch**: `009-test-suite-consistency`
**Created**: 2026-05-12
**Status**: Proposed
**Input**: Make the xUnit test suite deterministic and split out a fast, independently runnable unit tier (well under a minute) so contributors can validate changes pre-commit without paying the ~6-minute integration cost.

---

## Background

The full xUnit test suite (~668 tests, ~6 minutes via `dotnet test PoshMcp.sln`) is flaky despite xUnit parallelization already being disabled in `PoshMcp.Tests/AssemblyInfo.cs`:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
```

Observed on 2026-05-12:

- Clean runs on `main` HEAD report 2–9 failing tests.
- Failing test names are **not stable** across runs — different tests fail each time.
- Examples that have failed at least once: `McpResourcesIntegrationTests.ResourcesList_ReturnsFileResource_WithCorrectMetadata`, `McpPromptsIntegrationTests.PromptsGet_FileSource_ReturnsFileContentAsUserRoleMessage`, `UnifiedHttpTransportIntegrationTests.ServeHttpTransport_ShouldReturnErrorForUnknownToolCall`, `OutOfProcessMcpRoundTripTests.ToolsCall_ErrorHandling_ReturnsErrorForInvalidTool`, `ServerWithExternalClient.ShouldExecutePowerShellCommand`, `OutOfProcessSubprocessPoolIntegrationTests.Pool_PerRequestTimeout_KillsHostAndPoolRecovers`, `OutOfProcessIntegrationTests.TimeoutOnSlowCommand`, several `ApplicationInsightsIntegrationTests.Server_*`, `StdioLoggingTests.StdioTransport_WithNoLogFile_ProducesNoConsoleLogOutput`.
- Every flaky test passes when run in isolation (8/8 in 31s).
- The OOP-specific suite runs cleanly on its own (149 passed / 0 failed / 6 skipped in 5m47s).

Likely root cause: **resource exhaustion and OS-level cleanup latency** across a long serial run that spawns many `pwsh` subprocesses, binds HTTP ports, and writes to temp directories. Symptoms are consistent with port reuse races, leaked process handles, and temp-dir collisions — not with logic bugs in the tests themselves.

The current `Unit/` folder also contains tests (notably `OutOfProcess/*`, `ProgramCli*`) that exercise heavy infrastructure, so "Unit" is currently a folder convention, not a behavioral guarantee.

---

## User Scenarios & Testing

### Scenario 1 (P1): Contributor Runs Fast Unit Tests Pre-Commit

**Why this priority**: This is the hard requirement from the user — *"All unit tests should always be able to be run and run quickly."* Without a fast, deterministic tier, contributors either skip tests locally (silent regressions land) or wait 6 minutes for a result they can't trust (flake noise).

**Independent Test**: From a clean checkout, run the unit tier (e.g., `dotnet test --filter Category=Unit` or the dedicated unit project) and observe completion in well under a minute with zero failures across at least 5 consecutive runs.

**Acceptance Scenarios**:

- SC-100: Given a clean repo on `main` — the unit tier completes in **< 60 seconds** on a developer laptop.
- SC-101: Given 5 consecutive unit-tier runs back-to-back — all 5 pass with **0 flaky failures**.
- SC-102: Given a contributor who has never set up Azure credentials or Docker — the unit tier still runs to completion.
- SC-103: Given the unit tier runs — it spawns **no `pwsh` child processes**, binds **no TCP ports**, and writes to **no shared temp directories** (verified by inspection or process audit).

### Scenario 2 (P2): CI Runs Full Suite Reliably in Phases

**Why this priority**: CI must be a trustworthy gate. Today a "red" build is ambiguous — it could be a real regression or a known flake. Phased execution with cooldowns between resource-heavy categories gives CI a stable signal.

**Independent Test**: Run the full suite in CI as a sequence of category-scoped phases (Unit → Integration → OOP → Http → Azure) with explicit drain/cooldown between phases; observe 0 flaky failures across 5 consecutive CI runs on `main`.

**Acceptance Scenarios**:

- SC-104: Given a CI run that executes phases sequentially — each phase reports its own pass/fail summary and total duration.
- SC-105: Given a flake-rate baseline measurement across 5 runs — the rate is **0 flakes per run** on `main`.
- SC-106: Given a phase fails — the failure message identifies the category, making triage immediate.
- SC-107: Given the full phased run — total wall-clock time is **comparable to or better than** the current 6-minute serial run.

### Scenario 3 (P3): Maintainer Triages a Flake by Running a Single Category

**Why this priority**: When a flake appears in CI, the maintainer needs to reproduce locally without re-running the whole suite. A single-category filter that runs the same tests CI ran is the minimum tooling for fast triage.

**Independent Test**: Given a CI failure in (say) the OOP phase, the maintainer runs the equivalent local filter and reproduces or rules out the flake in under 6 minutes.

**Acceptance Scenarios**:

- SC-108: Given a documented filter syntax — the maintainer runs exactly the same test set CI ran for any single phase.
- SC-109: Given a category-scoped run — the output clearly identifies the category and reports any failures without ambiguity.
- SC-110: Given a test that is flaky in the full suite but passes in isolation — running just its category surfaces the flake within 2x of the full-suite reproduction rate (i.e., resource contention within the category alone is enough to trigger it).

---

## Edge Cases

- A test currently in `Unit/` (e.g., `OutOfProcess/*`, `ProgramCli*`) that actually spawns subprocesses or binds ports must be **reclassified** out of the unit tier, not left mislabeled.
- Tests that depend on Azure credentials (e.g., `AzureDeploymentIntegrationTests`, some `ApplicationInsightsIntegrationTests`) must remain in a category that is **skipped by default locally** unless explicitly opted in.
- Existing collection fixtures (`CachingStateTestCollection`, `TransportSelectionTestCollection`) already serialize specific groups — any new categorization must compose with them, not conflict.
- Reclassifying tests must not change their assertions or coverage — categorization is metadata only.
- Windows file/process handle behavior differs from Linux; teardown helpers must wait for handle release on Windows where `Process.WaitForExit` returns before child file handles are flushed.

---

## Requirements

### Functional Requirements

- **FR-400**: Every test in `PoshMcp.Tests` MUST belong to exactly one category from this set: `Unit`, `Integration`, `OutOfProcess`, `Http`, `Azure`, `Functional`.
- **FR-401**: A test classified as `Unit` MUST NOT spawn `pwsh` (or any) child process.
- **FR-402**: A test classified as `Unit` MUST NOT bind a TCP port (no Kestrel host, no `HttpListener`, no `HttpClient` against localhost).
- **FR-403**: A test classified as `Unit` MUST NOT write to shared temp directories — if it needs a temp path, it MUST use a unique per-test directory and clean it up in teardown.
- **FR-404**: The unit tier MUST be runnable in isolation via a single, documented command and MUST complete in **< 60 seconds** on the maintainer's reference machine.
- **FR-405**: The unit tier MUST report **0 flaky failures across 5 consecutive runs** on `main` before this spec is considered Done.
- **FR-406**: Test categorization MUST be expressed in a form that `dotnet test --filter` understands natively (xUnit `[Trait("Category", ...)]` or separate project, depending on chosen approach).
- **FR-407**: The current assembly-wide `DisableTestParallelization = true` MUST be preserved or strengthened — this spec does NOT propose re-enabling parallel execution.
- **FR-408**: A documented local command MUST exist for each category so a maintainer can reproduce a CI phase exactly.
- **FR-409**: CI configuration MUST run the suite as a sequence of category-scoped phases, with each phase reporting its own duration and pass/fail summary.
- **FR-410**: Heavy categories (`Integration`, `OutOfProcess`, `Http`) MUST drain shared resources between tests — explicit teardown of subprocesses, port release verification, and unique temp dirs per test.
- **FR-411**: Resource-heavy tests that bind a port MUST use a **dynamically allocated port** (port 0 + read back actual port), not a hard-coded port from a small range.
- **FR-412**: Resource-heavy tests that spawn `pwsh` MUST wait for **full process exit and handle release**, not just `WaitForExit()` return.
- **FR-413**: The `Azure` category MUST be skipped by default when Azure credentials are not present (existing pattern; document it explicitly).
- **FR-414**: Test reclassification MUST NOT delete, rewrite, or skip existing tests — only their `Category` trait or project location changes.
- **FR-415**: The chosen approach MUST be reversible — if categorization proves wrong for a given test, moving it between categories MUST be a metadata change (or a file move), not a logic rewrite.

---

## Approach Options

The team should evaluate the following options. They are not mutually exclusive — Option 3 is recommended regardless of which of 1 or 2 is chosen.

### Option 1 — Trait/Category-Based Phasing (Single Project)

Keep `PoshMcp.Tests` as a single project. Add `[Trait("Category", "Unit")]` (etc.) to every test class or test method. Use `dotnet test --filter Category=Unit` to run a single tier. CI runs each category as a separate `dotnet test --filter` invocation.

**Pros**:
- No project restructuring; existing folder layout, shared helpers, and project references all stay put.
- Low blast radius — adding a trait is a one-line attribute change per test class.
- Composes cleanly with existing `[Collection(...)]` fixtures.
- Maintainers can mix filters (`Category=Unit|Category=Integration`) without changing solution files.

**Cons**:
- Categorization is opt-in metadata — easy for a new test to forget a trait and silently fall into the default bucket.
- A single `dotnet test` against the whole project still loads ALL test assemblies and ALL fixtures into one process, so `Unit` runs may still pay startup cost for unrelated assemblies (mitigated, since the process exits after each phase).
- Requires a lint or analyzer to enforce "every test has a Category trait" long-term.

### Option 2 — Separate Test Projects

Split `PoshMcp.Tests` into multiple projects: `PoshMcp.Tests.Unit`, `PoshMcp.Tests.Integration`, `PoshMcp.Tests.OutOfProcess`, `PoshMcp.Tests.Http`, `PoshMcp.Tests.Azure`. Each project has its own `csproj`, references, and `AssemblyInfo`.

**Pros**:
- Hard separation — a unit test physically cannot reference integration helpers unless the project does, making FR-401/402/403 self-enforcing.
- `dotnet test PoshMcp.Tests.Unit.csproj` is unambiguous; no filter syntax to memorize.
- Each project can have its own parallelization settings (e.g., the Unit project could even re-enable parallelism safely).
- Cleaner CI matrix — each project is a separate job.

**Cons**:
- Significant restructuring: file moves, new `csproj` files, `using` cleanups, shared helper duplication or extraction into a `PoshMcp.Tests.Shared` library project.
- Higher PR review cost; greater chance of accidentally breaking a test during the move.
- Solution file churn; possible IDE indexing pain during the transition.
- Existing collection fixtures may need to be moved into the shared library project.

### Option 3 — Per-Test Resource Hygiene Audit (Strongly Recommended Regardless)

Audit and fix resource-heavy tests independently of how categorization is done:

- Every test that binds a port uses **port 0** and reads back the actual port.
- Every test that spawns `pwsh` uses a `try/finally` with explicit `Process.Kill(entireProcessTree: true)` on failure paths and waits for handle release (Windows: `Process.WaitForExit(timeout)` followed by handle-release poll).
- Every test that writes to a temp dir uses `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())` and deletes it in `Dispose`/teardown.
- Long-running collection fixtures that maintain a pool of subprocesses expose an explicit `Drain()` method called between tests.

**Pros**:
- Addresses the **root cause** of flakiness directly, not just by isolating it.
- Improves reliability of the heavy categories themselves, not just the unit tier.
- Low-risk, mechanical changes; can land incrementally per file.

**Cons**:
- Touches many test files; requires careful review to avoid changing test semantics.
- Doesn't on its own give contributors a fast pre-commit tier — that still requires Option 1 or 2.

### Option 4 — Cooldown Between Resource-Heavy Tests (Collection Fixtures with Explicit Drain)

Introduce a `ResourceHeavyTestCollection` collection definition (xUnit `[CollectionDefinition]`) used by tests that bind ports or spawn subprocesses. The collection's fixture exposes a `DrainAsync()` method invoked before each test that waits for: zero `pwsh` child processes owned by the test runner, all bound ports released, all temp paths cleaned.

**Pros**:
- Works with both Option 1 and Option 2.
- Predictable, observable cooldown — the drain step has its own logs.
- Catches leaks where they happen instead of letting them cascade.

**Cons**:
- Adds wall-clock time to the heavy phases (probably acceptable if it eliminates flakes).
- Requires a reliable way to enumerate "child `pwsh` processes owned by this test runner" — needs care on Windows vs. Linux.

---

## Recommendation

**Adopt Option 1 (trait-based phasing) AS THE FIRST STEP, combined with Option 3 (resource hygiene audit) IN PARALLEL.** Defer Option 2 (project split) until after the trait-based phasing has run in CI for two weeks. Consider Option 4 as a targeted follow-up if specific tests remain flaky after Option 3 lands.

**Rationale**:

1. **Option 1 unblocks the user requirement fastest.** Adding a `[Trait("Category", "Unit")]` to the existing fast tests and a documented `dotnet test --filter` command gives contributors the fast pre-commit tier in days, not weeks. This is the hard requirement from the user.

2. **Option 3 fixes the underlying problem.** Categorization alone doesn't make the heavy categories reliable — it just isolates the flakes. The team must do the hygiene audit either way. Doing it in parallel means the heavy categories become trustworthy too, not just quarantined.

3. **Option 2 is the right end state, but the wrong first step.** A project split is a large, disruptive change. We should learn which boundaries actually matter from running with traits first, then split projects along the lines reality validates — not the lines we guessed at on day one.

4. **Option 4 is a sharpened tool for a specific shape of failure.** If, after Option 3, we still see leaks across tests within a category, a drain fixture is the right answer. But predicting where we'll need it is harder than measuring it.

**Sequencing**:

1. Add `Category` traits to every test class. Default new tests to `Unit` only after they pass the hygiene checks in FR-401/402/403.
2. Reclassify currently-misfiled tests (notably `Unit/OutOfProcess/*`, `Unit/ProgramCli*`) out of `Unit`.
3. Document the per-category `dotnet test --filter` commands in the repo README (or a `TESTING.md`).
4. Land the resource hygiene audit (Option 3) incrementally — one file per PR is fine.
5. Update CI to run phases instead of a single `dotnet test PoshMcp.sln`.
6. Measure flake rate over 5+ runs. If categories still flake, add Option 4 drain fixtures to the offenders.
7. Revisit project split (Option 2) after two weeks of green phased CI.

---

## Non-Goals

- **Test rewrites.** This spec does NOT propose changing what any test asserts.
- **Removing or skipping tests.** Tests that flake are not deleted; they are reclassified and fixed.
- **Changing the test framework.** xUnit stays. No migration to NUnit, MSTest, or anything else.
- **Re-enabling parallelism.** The `DisableTestParallelization = true` setting stays. Speed comes from categorization and hygiene, not concurrency.
- **Solving CI infrastructure choice.** This spec is about test behavior, not which CI provider or runner image is used.
- **Coverage thresholds or coverage tooling.** Out of scope.
- **Performance benchmarks.** `PoshMcp.Benchmarks` is unaffected.

---

## Open Questions

1. **OQ-1 — Reference machine for the < 60s target.** What is "the maintainer's reference machine" for FR-404? (Suggested: the same machine that produced the 31s / 8-test isolation baseline on 2026-05-12.)
2. **OQ-2 — Default category for untagged tests.** When CI encounters a test with no `Category` trait, should the run fail (strict) or fall back to a default bucket (permissive)? Strict is recommended but requires an analyzer or pre-merge check.
3. **OQ-3 — Where do `Functional/*` tests land?** Some (`StdioLoggingTests`) clearly spawn processes and are flaky. Are they renamed `Integration`/`Http`, kept as `Functional` with hygiene fixes, or split case-by-case? Recommended: case-by-case during the audit.
4. **OQ-4 — Azure credentials in CI.** The `Azure` category is skipped without credentials locally. Should CI run it in a dedicated, credentialed job, or only on a nightly schedule? (Existing pattern likely already answers this — confirm.)
5. **OQ-5 — Should we add an EditorConfig/analyzer rule to require `[Trait("Category", ...)]` on every `[Fact]`/`[Theory]`?** Strongly recommended but adds tooling work.
6. **OQ-6 — Cooldown duration if Option 4 is needed.** If a drain fixture is added later, what's the upper-bound wait before declaring drain failed? (Suggested initial value: 5 seconds, then tune.)
7. **OQ-7 — Reporting flake rate.** How is "0 flakes across 5 runs" measured and recorded? Manual log inspection, a `--blame-hang` style summary, or a dedicated CI step?

---

## Success Criteria

- The unit tier runs in **< 60 seconds** and passes **5 consecutive runs** with **0 failures** on the reference machine.
- The full phased CI run shows **0 flaky failures over 5 consecutive runs** on `main`.
- Every test in the repository has exactly one `Category` trait (or lives in exactly one category project, if Option 2 is later adopted).
- A maintainer can reproduce any CI phase locally with a single documented command.
