# Amy — Spec 009 / #216: flake-rate measurement workflow

**By:** Amy (DevOps / Platform / Azure)
**For:** Steven Murawski
**Date:** 2026-05-14

## Decision

Spec 009 FR-418 (and supporting FR-405 / SC-105) is implemented as a **separate workflow file** at `.github/workflows/flake-rate.yml`, not as additional steps inside the existing `ci.yml` from PR #252. The flake-rate workflow re-runs the phased test suite N times (default 5, configurable via `workflow_dispatch` input), aggregates per-test failure counts across iterations, and emits a single markdown summary that is both uploaded as an artifact and mirrored into the workflow run summary.

## Triggers

- `workflow_dispatch` with a single string input `runs` (default `"5"`) so a maintainer can crank N up to e.g. 20 for a deeper measurement on demand without editing the workflow.
- `schedule: '0 7 * * *'` — nightly at 07:00 UTC. Off-peak versus typical maintainer working hours so a long measurement run does not contend with on-demand CI.

The `Azure` phase is intentionally **not** included. Scheduled runs do not have Azure credentials wired (per spec 009 Non-Goals — credentialed CI for Azure is deferred), and Azure tests already skip-when-no-creds, so the phase would only add noise.

## Phasing

Mirrors PR #252 (`ci.yml`) one-for-one: Unit → Integration → OutOfProcess → Http → Functional. Each iteration writes its TRX outputs to a per-iteration directory (`flake-runs/run-${i}/`) so attribution is unambiguous. The repetition loop uses `set +e` and captures per-phase exit codes into `flake-runs/run-${i}/exit-codes.txt` — a phase failure in iteration 3 must NOT skip iterations 4 and 5; the whole point of the workflow is to measure flakes across all iterations.

## Aggregator format

PowerShell (matches the repo idiom). Walks every TRX file via `Select-Xml` with an explicit `XmlNamespaceManager` (TRX uses a default namespace; dotted-property access silently returns nothing).

The output `flake-rate-summary.md` contains:

1. **Run metadata table** — UTC timestamp, commit SHA, ref, runner OS/arch, .NET SDK version, iterations completed, total test invocations, total non-pass invocations, aggregate flake rate, workflow run URL.
2. **Per-test failure-count table** — every test that did not pass in at least one iteration, broken out by outcome (`Failed` / `NotExecuted` / `Other`), with `Total / Iterations` ratio (e.g. `3 / 5`) and the list of iterations affected. Sorted by total non-pass count, highest first.
3. **Per-iteration phase exit codes table** — Unit/Integration/OutOfProcess/Http/Functional per iteration, so a fully red iteration (environmental issue) is visually distinct from a single phase flicking red across iterations (classic flake signature).

**Why markdown over JSON:** the primary consumer is a human looking at the workflow run page. Markdown renders inline in `$GITHUB_STEP_SUMMARY` without an artifact download. Raw TRX is still uploaded separately (`flake-runs-raw` artifact) for anyone who wants to write their own analysis.

**Aggregate rate definition:** `total non-pass instances / total test invocations across all iterations`. This intentionally counts repeated failures of the same test in different iterations as separate flake instances — one test failing 3/5 should weigh more than three different tests each failing 1/5.

## Artifacts

- `flake-rate-summary` — single `flake-rate-summary.md` file. The headline artifact.
- `flake-runs-raw` — full `flake-runs/` directory tree with per-iteration TRX files and exit-code text files. For drill-down.

Both upload `if: always()` so a partial run still surfaces what data exists.

## Cross-PR coordination

- **PR #252 (`ci.yml`):** flake-rate workflow phasing matches `ci.yml` phasing one-for-one. If #252 lands first, both files reference the same phase definitions implicitly. If #252 has not landed when this PR ships, the flake-rate workflow still works (it is a separate workflow file with no dependency on `ci.yml` existing).
- **PR #253 (`TESTING.md`):** TESTING.md is being introduced in PR #253 and Amy did NOT edit it from this branch (cross-PR coordination boundary). The PR body for #216 explicitly notes that TESTING.md should add a "Flake-rate measurement" pointer that links to the workflow run page → "Artifacts" → `flake-rate-summary`. Whoever merges #253 (or a follow-up) should add that pointer.

## Trade-offs accepted

- **Markdown over JSON for the summary.** Easier to read on the run page; harder to machine-parse. Acceptable because raw TRX is uploaded separately for any future automation.
- **PowerShell aggregator instead of bash.** Repo lives in pwsh land; consistent with `docker.ps1`, `install-modules.ps1`, etc. Trade-off is one fewer cross-platform shell at the cost of a less terse parser — `Select-Xml` is cleaner than awk/grep TRX walking.
- **Loop step swallows exit codes.** The bash loop always exits 0, even when individual phases fail. Necessary so all N iterations run; the per-phase exit codes are captured into text files for the aggregator. Side effect: the only way the workflow itself fails is if the build fails, the loop step itself crashes (not a phase failure), or the aggregator throws.
- **Azure phase excluded.** Per spec 009 Non-Goals; revisit when credentialed CI lands.

## Files touched

- `.github/workflows/flake-rate.yml` (new)
- `.squad/agents/amy/history.md` (Learnings appended)
- `.squad/decisions/inbox/amy-spec009-216-flake-rate.md` (this file)
