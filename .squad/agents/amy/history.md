# Amy Work History

## Recent Status (2026-04-10)

**Summary:** Observability, Azure infrastructure, and deployment-documentation foundations remain complete. Active emphasis is on release hygiene, infrastructure troubleshooting, and keeping the decision pipeline accurate as `.squad` grows.

**Current Role:** Infrastructure and decision coordination. Primary areas: health checks, Azure Container Apps, deployment scripts, documentation verification, and release/version workflows.

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

## Recent Learnings

### 2026-04-12: v0.5.1 patch release

- Bumped `PoshMcp.Server/PoshMcp.csproj` version from `0.5.0` → `0.5.1` (patch increment following "Bump version to 0.5.0" commit).
- Pack command: `dotnet pack .\PoshMcp.Server\PoshMcp.csproj -c Release -o .\artifacts\nupkg` → produces `poshmcp.0.5.1.nupkg` (~25 MB).
- Update command: `dotnet tool update -g poshmcp --version 0.5.1 --add-source .\artifacts\nupkg --ignore-failed-sources`
- Verified: `poshmcp --version` → `0.5.1+fad23f66007916f0c2145e7c5e0eb8a20925c8dd`
- `dotnet tool update` handles both first-time install and upgrades; no need to uninstall first.
- No running poshmcp.exe processes were present; process-stop guard was a no-op but remains required as a pre-check.

### 2026-04-09 to 2026-04-10: Release and operational hygiene

- `PoshMcp.Server/PoshMcp.csproj` is the source of truth for global tool versioning.
- Stop any running `poshmcp` process before `dotnet tool update -g poshmcp` to avoid access-denied uninstall/update failures.
- Scribe health checks and archive gates matter once `.squad` histories and decisions pass their size thresholds.

### 2026-04-03: Deployment docs and Azure workflow validation

- After large doc cleanups, verify code fences, redirects, command examples, and cross-links explicitly.
- Subscription-scoped Bicep deployment means manual examples must use `az deployment sub create`, not group-scoped commands.
- Deployment scripts with mixed imperative and declarative steps still need the resource group created before ACR and other imperative resource commands.

### 2026-03-27: Platform foundations that remain current

- Health checks and correlation IDs are the baseline observability layer, with explicit `Task.WaitAsync()` timeout enforcement.
- Azure Container Apps plus managed identity, scale-to-zero, and layered docs remain the deployment baseline.
- Multi-tenant deployment safety depends on tenant switching plus subscription-to-tenant validation.

### 2026-04-12: Sequential PR merge session (#92–#95)

- Processed PRs #92, #93, #94, #95 in order — all touching `Program.cs` and related CLI/schema areas.
- Rebase pattern: worktrees start at an already-up-to-date state for the first PR; subsequent PRs require a live rebase after each preceding merge lands on main.
- `dotnet restore` is required before `dotnet test --no-restore` when worktrees haven't been built yet; the `--no-restore` flag fails with `NETSDK1004` on a cold worktree.
- `gh pr merge --delete-branch` produces a non-zero exit code in worktree setups (`fatal: 'main' is already used by worktree`) but the squash merge itself succeeds — the exit code is a false failure from the local branch-delete step, not from the GitHub merge.
- Test counts grew across the session: 343 → 343 → 355 → 388 (PR #94 added 12 tests for update-config flags; PR #95 added 33 tests for unserializable type handling).
- All 4 PRs merged cleanly with zero conflicts. The `Program.cs` changes were additive (new CLI flags, advisory warning) and non-overlapping.
- Force-push must specify the remote branch name explicitly (`git push --force-with-lease origin <branch>`) when the worktree branch has no upstream tracking configured.

## Archive Note

Detailed session history was archived to `history-archive.md` on 2026-04-10 when this file exceeded the 15 KB Scribe threshold.


