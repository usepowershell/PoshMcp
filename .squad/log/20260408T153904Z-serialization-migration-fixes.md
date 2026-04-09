# Session Log: serialization migration fixes and web harness alignment

**Timestamp:** 2026-04-08T15:39:04Z  
**Requested by:** Steven Murawski  
**Topic:** Record Hermes serialization fixes, Fry coverage guidance, and Bender's web test harness fix

## Summary
Merged the pending serialization-related decision inbox entries, recorded the configuration-aligned web harness startup fix, and updated agent histories for the completed migration follow-up work.

## Notes
- Hermes fixed the string serialization regression and normalized nested PowerShell and CLR objects into JSON-safe shapes before `System.Text.Json` serialization.
- Fry's pending test decisions were merged so serializer-level and execution-plus-cache coverage remain the regression anchors for string output shape.
- Bender's pending harness decision was merged so web integration startup now reuses the active test build outputs with `--no-build` and matching configuration.
- A duplicate 2026-04-03 `dotnet format` and `dotnet test` directive was detected in the inbox and not duplicated in `decisions.md`.
- The 2026-04-08 directive to avoid VS Code builds/tests while the MCP server is running was added to the ledger.

## Decision Inbox Check
- Processed 6 inbox files.
- Merged 5 unique entries into `decisions.md`.
- Skipped 1 duplicate directive already present in the ledger.

## Status
- Completed `.squad/` logging and memory updates for this batch.
- No product code was changed by this Scribe pass.