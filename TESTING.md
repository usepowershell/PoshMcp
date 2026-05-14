# Testing PoshMcp

This document describes how to run the `PoshMcp.Tests` suite locally and how each category maps to a CI phase. It is the contributor reference for the test-suite categorization introduced by [spec 009 — Test Suite Consistency](specs/009-test-suite-consistency/spec.md).

## TL;DR

```bash
# Fast pre-commit check (target: < 60 seconds, no subprocesses, no ports, no creds)
dotnet test PoshMcp.sln --filter "Category=Unit"

# Everything (~6 minutes, includes heavy categories)
dotnet test PoshMcp.sln
```

If you only have a minute before pushing, run the `Unit` filter. If you are touching out-of-process execution, the HTTP transport, or anything that spawns `pwsh`, also run the matching category locally — see the table below.

---

## Categories

Every test in `PoshMcp.Tests` belongs to exactly one of the categories below (per FR-400). The category is expressed as an xUnit `[Trait("Category", "<name>")]` so it composes with `dotnet test --filter`.

| Category | What it covers | Typical duration | Spawns `pwsh` | Binds ports | Requires creds |
|---|---|---|---|---|---|
| `Unit` | Pure in-process logic: schema generation, parameter coercion, configuration parsing, sanitizers, helpers. No external resources. | < 60 s for the whole tier | No | No | No |
| `Integration` | In-process integration of multiple components: tool factory + runspace, MCP request/response wiring, prompt and resource resolvers driven through the real server pipeline. | 1–3 min | Yes (in-process runspaces) | No | No |
| `OutOfProcess` | The out-of-process subprocess host: `Single`, `Pool`, and `ProcessPool` modes; cancellation propagation; pool sizing; per-request timeouts; host recovery. | 3–6 min | Yes (child `pwsh`) | No | No |
| `Http` | The unified HTTP transport: Kestrel host, MCP-over-HTTP, health endpoints, correlation IDs, auth handlers (without real Entra ID). | 1–2 min | Sometimes | Yes (dynamic ports) | No |
| `Azure` | Tests that exercise real Azure resources (e.g., Application Insights ingestion, Container Apps deployment surface). **Skipped by default when credentials are absent.** | Variable | Varies | Varies | Yes |
| `Functional` | Multi-area scenarios that exercise several components together **without touching external resources** — no disk, network, files, subprocesses, or ports. If a test needs any of those, it belongs in `Integration` (or a more specific resource-bound category), not `Functional`. See FR-416. | < 1 min | No | No | No |

### Per-category commands

Every category has a single command that runs exactly that tier and nothing else:

```bash
# Unit — fast pre-commit tier, < 60s, zero external resources
dotnet test PoshMcp.sln --filter "Category=Unit"

# Integration — in-process integration of multiple components
dotnet test PoshMcp.sln --filter "Category=Integration"

# OutOfProcess — subprocess host, pools, cancellation
dotnet test PoshMcp.sln --filter "Category=OutOfProcess"

# Http — Kestrel + MCP-over-HTTP + health endpoints
dotnet test PoshMcp.sln --filter "Category=Http"

# Azure — real Azure resources; skipped without creds (see below)
dotnet test PoshMcp.sln --filter "Category=Azure"

# Functional — multi-area scenarios, no external resources
dotnet test PoshMcp.sln --filter "Category=Functional"
```

`--filter "Category=X"` matches the xUnit trait, not the folder. A test in `PoshMcp.Tests/Unit/` with `[Trait("Category", "OutOfProcess")]` runs under the `OutOfProcess` filter, not `Unit`. This is intentional — see FR-401/402/403, which require `Unit` tests to spawn no subprocesses, bind no ports, and use no shared temp directories.

---

## Per-category caveats

### `Unit`

- The fast pre-commit tier. Target is **< 60 seconds** on the maintainer's reference machine (FR-404, FR-419). Your laptop may be faster or slower; an over-60s run on a contributor machine is not on its own a spec violation.
- No `pwsh` child processes. No bound ports. No shared temp directories. If a test in this tier appears to violate any of those, it is misclassified — open an issue and reference [spec 009](specs/009-test-suite-consistency/spec.md).
- Should be runnable with no Azure credentials, no Docker, and no network.

### `Integration`

- Heaviest in-process category. Spawns runspaces, exercises the real MCP server pipeline, and may hold resources for several seconds per test.
- Parallelization is disabled at the assembly level (`DisableTestParallelization = true` in `PoshMcp.Tests/AssemblyInfo.cs`) — preserved per FR-407. Tests run serially by design.

### `OutOfProcess`

- Spawns child `pwsh` processes. Each test cleans up its own subprocesses via explicit `Process.Kill(entireProcessTree: true)` and waits for full handle release before returning (FR-412).
- Most expensive category by wall-clock time. Run it locally before merging anything that touches `PoshMcp.Server/PowerShell/OutOfProcess/`.

### `Http`

- Binds dynamically allocated ports (port 0, then read back the actual port — FR-411). No hard-coded ports. Safe to run while other servers are listening.
- Includes auth handler tests, but does **not** call real Entra ID — those would be `Azure` if/when added.

### `Azure`

- **Skipped by default when Azure credentials are not present** (FR-413). The skip is built into the test code; you do not need a separate filter to exclude this category locally.
- To opt in, follow the instructions in [`run-azure-integration-tests.ps1`](run-azure-integration-tests.ps1) and provide the documented environment variables / Azure CLI session.
- Running `Azure` against real Azure resources in CI is currently a non-goal of spec 009 (deferred). CI does not provision credentials for this category.

### `Functional`

- Multi-area scenarios that stay in-process. Per FR-416, any `Functional` test that ends up touching external resources (disk, network, subprocesses, ports) must be reclassified as `Integration` (or a more specific resource-bound category). This is a rule applied at categorization time, not a case-by-case judgment call.

---

## Default bucket for untagged tests

Per FR-417, a test with no `Category` trait falls back to a documented default bucket. The default bucket is **not** `Unit` — untagged tests cannot accidentally enter the fast pre-commit tier.

The default bucket is **`Integration`**: an untagged test runs in the `Integration` phase, not `Unit`. The policy is committed in [`PoshMcp.Tests/AssemblyInfo.cs`](PoshMcp.Tests/AssemblyInfo.cs) (see the Spec 009 policy comment block) and reflects FR-417. When in doubt, run the full suite without a `--filter` argument to ensure your test executes:

```bash
dotnet test PoshMcp.sln
```

There is no build-time analyzer enforcing trait presence at this stage (deferred — see spec Non-Goals). New tests should still receive an explicit `[Trait("Category", "...")]` so they execute in the intended CI phase.

---

## Reproducing a CI phase locally

CI runs the suite as a sequence of category-scoped phases (FR-409). Each phase reports its own pass/fail summary and duration so a red build identifies the failing category immediately. The mapping below lets a maintainer reproduce any single CI phase with one local command.

| CI phase | Local command |
|---|---|
| Unit | `dotnet test PoshMcp.sln --filter "Category=Unit"` |
| Integration | `dotnet test PoshMcp.sln --filter "Category=Integration"` |
| OutOfProcess | `dotnet test PoshMcp.sln --filter "Category=OutOfProcess"` |
| Http | `dotnet test PoshMcp.sln --filter "Category=Http"` |
| Azure | `dotnet test PoshMcp.sln --filter "Category=Azure"` (requires creds; skipped without them) |
| Functional | `dotnet test PoshMcp.sln --filter "Category=Functional"` |

`Unit` runs first so a fast-tier regression fails the build before any heavier phase starts. The ordering of the remaining phases, the runner image, and the reporting format are owned by `.github/workflows/ci.yml` (issue #215). When in doubt about what CI actually ran, the workflow file is the source of truth.

### Flake-rate measurement

Per FR-418, CI includes a dedicated step that runs the full phased suite N times (initial N = 5) and reports a single flake-rate summary artifact identifying which tests failed in any of the N runs and how often. That step is configured in the CI workflow; it is not a separate local command — running the equivalent locally is `for /l %i in (1,1,5) do dotnet test PoshMcp.sln` (or the bash equivalent).

---

## Troubleshooting

### A test passes locally but fails in CI

Run the same category in isolation locally first. Most cross-test interference shows up only when a category runs end-to-end. If isolation passes, try the full phased sequence locally before assuming the failure is environmental.

### A `Unit` test starts spawning `pwsh` or binding a port

That test is misclassified. `Unit` tests must not spawn subprocesses, bind ports, or write to shared temp directories (FR-401/402/403). Move it to `Integration`, `OutOfProcess`, or `Http` as appropriate, or fix the test so it stays in-process.

### `OutOfProcess` flake — host did not exit cleanly

Check that the test waits for full process exit and handle release (FR-412), not just `Process.WaitForExit()` return. On Windows, child file handles may still be open briefly after `WaitForExit` returns; the test should poll for handle release with a short timeout before asserting cleanup.

### `Http` test fails with "address already in use"

The test is using a hard-coded port. Per FR-411, `Http` tests must bind dynamically allocated ports (port 0, then read back the actual port). Update the test to use port 0 and read the actual bound port from the listener.

### `Azure` tests are running locally and prompting for credentials

That should not happen — they are skipped by default when credentials are absent (FR-413). If you have Azure CLI signed in or `AZURE_*` environment variables set, the skip predicate sees credentials and runs the tests. Sign out of Azure CLI or unset the variables to restore the skip behavior, or use the explicit category filter to exclude them: `dotnet test PoshMcp.sln --filter "Category!=Azure"`.

### A test currently lives under `PoshMcp.Tests/Unit/` but is not in the `Unit` category

Folder location is no longer the source of truth — the `Category` trait is. Some tests under `Unit/` (notably `Unit/OutOfProcess/*` and `Unit/ProgramCli*` historically) actually exercise heavy infrastructure and have been reclassified out of `Unit` per spec 009. Trust the trait, not the folder.

---

## Reference

- **Spec:** [`specs/009-test-suite-consistency/spec.md`](specs/009-test-suite-consistency/spec.md)
- **Test project:** [`PoshMcp.Tests/`](PoshMcp.Tests/)
- **CI workflow:** [`.github/workflows/ci.yml`](.github/workflows/ci.yml)
- **Integration fixtures:** [`integration/README.md`](integration/README.md)
- **Test fixtures:** [`PoshMcp.Tests/Fixtures/README.md`](PoshMcp.Tests/Fixtures/README.md)

For the underlying changes that make per-category execution possible — trait additions, resource hygiene fixes, and CI phase wiring — see issues #212 (categorization), #214 (this document), and #215 (CI workflow).
