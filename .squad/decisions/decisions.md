# Decisions Ledger

## Active Decisions

### 2026-04-09T17:18Z: User directive — aggressive commit strategy
**By:** Steven Murawski (via Copilot)
**Status:** Active
**Decision:** Continue to cache status aggressively — commit after every logical chunk of work. Do not batch commits.
**Rationale:** Crash recovery protection. User request after losing in-flight work from Bender and Hermes during a crash.
**Impact:** All agents (Bender, Hermes, Scribe) should commit frequently. Pipeline analysis phases proceed with frequent state checkpoints.
**Created:** 2026-04-09T17:18Z

### 2026-04-16: Replace local Docker builds with ACR Build Tasks in deploy.ps1
**By:** Amy Wong (DevOps/Platform/Azure)
**Status:** Implemented
**Date:** 2026-04-16
**Decision:** Refactored `infrastructure/azure/deploy.ps1` to use `az acr build` instead of local `docker build` + `docker push`.
**Rationale:** Docker Desktop is a heavy prerequisite not all machines have. `az acr build` offloads to cloud ACR Build Tasks, requiring only Azure CLI with active login. Removes Docker login/push retry logic and ACR reachability probes.
**Impact:** Deployment script now requires only `az` CLI instead of Docker Desktop. All existing functionality (tenant handling, subscriptions, resource groups, Bicep, post-deploy verification) unchanged.

### 2026-07-15: MCP Resources and Prompts Architecture
**By:** Farnsworth (Architect)
**Status:** Proposed
**Spec:** `specs/002-mcp-resources-and-prompts/spec.md`
**Decision:** Seven architectural decisions for MCP resources/prompts layer:
1. Config placement: `McpResources` and `McpPrompts` are top-level `appsettings.json` sections (not nested under `PowerShellConfiguration`)
2. URI scheme: `poshmcp://resources/{slug}` recommended but not enforced; Doctor emits warning for non-conforming URIs
3. Command execution: shared runspace, read-only by convention (operator responsibility, not server-enforced)
4. Argument injection: pre-assign to runspace as `$argName = value` (not `-ArgumentList`)
5. File-backed prompt arguments: out of scope for v1 — MCP client handles template rendering
6. Resource caching: intentionally absent — operators build caching into PowerShell commands if needed
7. Resource subscriptions: out of scope — `resources/subscribe` and change notifications deferred
8. SDK registration pattern: all four handler types registered on MCP server builder in `Program.cs` via SDK extension methods

### 2026-04-17: Spec Restructure — Numbering Scheme (003–005)
**By:** Farnsworth (Lead / Architect)
**Status:** Adopted
**Date:** 2026-04-17
**Decision:** Three loose spec files restructured into speckit format (consistent with specs 001 and 002):
- Spec 003: `specs/003-powershell-interactive-input/` — Interactive prompt handling (FR-035–FR-043, SC-016–SC-020)
- Spec 004: `specs/004-out-of-process-execution/` — Out-of-process pwsh subprocess (FR-044–FR-054, SC-021–SC-025)
- Spec 005: `specs/005-large-result-performance/` — Large result set performance (FR-055–FR-064, SC-026–SC-030)
**Rationale:** Sequential numbering continues from FR-034/SC-015. Original loose spec files preserved as design reference/RFC history. Implementation details stripped; specs written in user-value terms per speckit standard. Prompt handling precedes OOP in numbering because interactive input exists in both modes and informs OOP design.
**Impact:** All future FRs start at FR-065, SCs at SC-031. Team must consult this log and spec files to avoid collisions.
