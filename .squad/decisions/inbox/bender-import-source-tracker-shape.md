# 2026-05-16T17:02:54.700-05:00 — Import source tracker shape for issue #272

## Decision

Use a dedicated `IToolImportSourceTracker` that mirrors the spec-010 description tracker contract: thread-safe, per-discovery-cycle, first-writer-wins, and keyed by PowerShell command name.

## Why

Doctor needs authoritative per-tool attribution without re-running discovery. Recording the resolved source at the same discovery call sites keeps parity between InProcess and OutOfProcess modes and avoids any new `Get-Command` or `Get-Module` work on the doctor path.

## Consequences

- `McpToolFactoryV2` records in-process sources during `GetCommandsByName`, `GetCommandsByModule`, and `GetCommandsByPattern`.
- OOP discovery records directly from `RemoteToolSchema.SourceModule` / `SourcePattern` / `SourceDetail`.
- If an older OOP host omits `Source*` fields, doctor reports `tools[].source = "unknown"` instead of reviving the old heuristic.
