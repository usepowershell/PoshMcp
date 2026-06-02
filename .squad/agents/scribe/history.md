# Scribe Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Project Description:**
PoshMcp dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable, AI-consumable tools via the Model Context Protocol. It features persistent PowerShell runspaces, dynamic tool discovery, multi-user isolation (web mode), and OpenTelemetry metrics.

**Current Priorities:**
- Improve maintainability (structured errors, config validation)
- Enhance resilience (circuit breakers, timeouts, retry logic)
- Boost observability (metrics, health checks, diagnostics)

**Role:** Automated session logging, decision merging, and history management.

## Learnings

*Learnings from work will be recorded here automatically*
- 2026-04-08: Added lifecycle-focused tool invocation logging notes for a perceived `Get-Process` hang; recorded that build and targeted test validation passed.
- 2026-04-09: Consolidating transport-foundation, HTTP implementation, and gate decisions in one merge pass keeps `.squad/decisions.md` coherent and avoids losing green-criteria evidence.
- 2026-04-09: Merging the CLI config command inbox item into `.squad/decisions.md` with a single canonical entry and then removing inbox residue keeps the decision ledger clean and auditable.
- 2026-04-10: When `decisions.md` is already above the archival threshold, check existing entries for age violations before assuming the inbox is the only archival source.
- 2026-04-10: Recovery batches with overlapping agent findings are easier to audit when Scribe merges them into a few canonical ledger entries instead of copying each inbox file verbatim.
- 2026-04-14: For docs-only workflow spawns, merge the inbox item into canonical decisions and add a cross-agent update so architecture/docs leads inherit deployment-trigger context.
- 2026-04-15: Enforce the archival hard gate first when `decisions.md` exceeds 50KB; move out-of-window entries to `.squad/decisions-archive.md` before merging new inbox proposals so retention and canonical merge rules both stay true.
- 2026-04-23: For scaffold + deploy enhancements, record one canonical decision per inbox proposal and then clear inbox files immediately to prevent duplicate merges in later sessions.
- 2026-04-23: For docs update closeout passes, explicitly log that `.squad/decisions/inbox/` was checked and empty so the no-merge outcome is auditable.
- 2026-04-24: Combined docs + release-note closeout batches should produce two orchestration entries (one per agent) and one session log while keeping all changes constrained to `.squad/` files.
- 2026-04-24: Docker build behavior shifts should be captured as one canonical decision with explicit follow-up fixes (like build-arg ordering before context), then mirrored into a session log plus per-task Bender orchestration entries.
- 2026-04-24: When workflow/script build behavior is corrected by another agent, merge the inbox proposal into one concise canonical decision and create matching per-agent orchestration logs for traceability.
- 2026-04-24: For version bump and packaging batches, preserve cross-agent continuity by logging three anchors together: canonical decision merge (with artifact path), one per-agent orchestration entry, and one short session log.
- 2026-05-19: When `decisions.md` is still above the 50 KB gate, check whether any entries older than the retention cutoff actually remain before attempting a second archive pass. If only recent entries are left, merge the inbox, clear it, and record that no additional archival candidates existed.
- 2026-05-19: If release agents already updated their own history files and those files are dirty, do not reopen them during Scribe closeout. Add only new squad artifacts in clean files, then commit a path-scoped `.squad/` slice.
- 2026-05-02: Cut release v0.9.17 following release process principles: clean build verified, version updated in PoshMcp.csproj, committed with context (token diagnostics and idle session timeout), tagged, and pushed. Remote accepted both commit and tag successfully.
- 2026-05-20: For noun-resource closeout batches, merge overlapping design and implementation inbox notes into one canonical decision entry when they describe the same behavior slice, then clear the processed inbox files and record the validator evidence in the session log.
- 2026-05-28: For Entra auth troubleshooting batches, record the issuer-shape mismatch explicitly (`sts` v1 token issuer vs v2-only accepted issuer), note whether `requestedAccessTokenVersion` is set to `2`, and capture no-op inbox checks so the run remains auditable.
- 2026-06-02: Release-attempt closeout should log partial stabilization progress per agent, record the exact remaining blocker preventing full-green status, and keep all commit scope limited to `.squad/` artifacts when code changes stay uncommitted.
- 2026-06-02: Release-finalization closeout should explicitly record three-way alignment between main HEAD, green workflow state, and remote tag peel commit so the release state is auditable without rerunning checks.

## 2026-05-16: Issue #272 spawn — IToolImportSourceTracker implementation

**Spawn:** Bender + Fry background agents (claude-sonnet-4.6, general-purpose, ~18 min + ~15 min)

**What happened:**
- **Bender:** Implemented `IToolImportSourceTracker` interface + in-process implementation + OOP/OutOfProcess implementation. Wired `DoctorService` consumer with "unknown" fallback for multi-module configs. Added unit tests + OOP parity tests. Build passing. Branch: `squad/272-import-source-tracker`.
- **Fry:** Wrote unit test files `DoctorToolImportSourceTests.cs` and `ToolImportParityTests.cs`. Could not run tests due to .NET version mismatch (available: .NET 9; required: .NET 10).

**Outcome:** PR #276 opened at https://github.com/usepowershell/PoshMcp/pull/276. Ready for review. Resolves issue #272 per Spec 010 interface-design pattern for per-tool source attribution.

**Artifacts:** PR #276, branch `squad/272-import-source-tracker`

## 2026-05-16 — v0.14.1 Release (via Scribe)

Release v0.14.1 shipped successfully. Version bump, release notes, and GitHub release creation completed by Amy. Commit a2a89b3, tag v0.14.1 pushed to origin, release published.

## 2026-05-20 — Noun-resource tightening closeout

Merged three decision inbox notes into two canonical ledger entries covering `AssociatedResourceUri` behavior and zero-required-parameter noun-resource gating. Recorded one session log, one orchestration log, updated relevant agent histories, and moved squad focus from idle to noun-resource closeout.

## 2026-05-28 — JWT issuer troubleshooting batch

Recorded a no-op decision inbox check (empty), one orchestration entry, and one session log for the cross-agent troubleshooting pass. Captured consensus finding: auth failures align with issuer mismatch (STS v1 issuer observed while validation is v2-only), with Entra-side follow-up to verify `requestedAccessTokenVersion = 2` and use a temporary dual-issuer acceptance workaround as needed.

## 2026-06-02 — v0.16.4 release finalization logged

Recorded a no-op decision inbox check (empty), one orchestration entry for Amy's release verification, and one session log confirming `v0.16.4` finalization with full commit/tag/workflow alignment at `9a4cc931ac5720f0fb305fccebe6da9dad8c6b66`.

