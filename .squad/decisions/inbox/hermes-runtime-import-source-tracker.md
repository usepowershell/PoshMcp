# Hermes decision inbox: runtime import source tracker

- **Date:** 2026-05-16T17:51:27.768-05:00
- **Issue:** #272
- **Context:** Runtime doctor/status/troubleshooting reports need authoritative `tools[].source` attribution, not filesystem-only reconstruction.
- **Decision:** Reuse the same `IToolImportSourceTracker` instance across runtime tool discovery and runtime doctor/report generation, and reset that tracker at the start of each discovery cycle.
- **Rationale:** The tracker already encodes first-writer-wins precedence (`commandName` > `module` > `pattern`). Resetting on each `GetToolsListAsync()` pass preserves correctness across reloads while letting runtime report builders stay byte-parity with CLI doctor without re-running attribution logic.
- **Implications:** Any future runtime surface that renders `moduleImports.tools[]` should accept the live tracker from discovery rather than reconstructing sources from config or tool names.
