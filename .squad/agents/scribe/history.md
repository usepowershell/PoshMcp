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
- 2026-05-02: Cut release v0.9.17 following release process principles: clean build verified, version updated in PoshMcp.csproj, committed with context (token diagnostics and idle session timeout), tagged, and pushed. Remote accepted both commit and tag successfully.
