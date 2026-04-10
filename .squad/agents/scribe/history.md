# Scribe Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
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
- 2026-04-10: When a session has no decision inbox items, Scribe should still refresh precheck metrics, write orchestration/session health logs, and summarize oversized agent histories so squad state stays current even without a new decision merge.
