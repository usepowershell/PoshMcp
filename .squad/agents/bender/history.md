# Bender Work History

## Recent Status (2026-04-14)

**Summary:** Backend execution and diagnostic reliability work remains the core focus. Current emphasis is out-of-process lifecycle hardening, tooling diagnostics performance, and minimizing redundant PowerShell execution paths.

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

## Recent Learnings

### 2026-04-14: Doctor diagnostics should not recompute expensive introspection

- Avoid duplicate execution of `DiagnoseMissingCommands` across runtime and JSON builder paths.
- Pass precomputed status data into JSON serialization helpers where available.
- Guard expensive fallback calls behind null/empty checks to preserve standalone correctness.

### 2026-04-14: Authorization overrides must map generated tool names back to base command names


### 2026-04-15: Cross-agent auth handoff with Fry review

- Fry's independent review confirmed docs wording and precedence tests should mirror the implemented override resolver order.
- Keep pairing resolver changes with both code-level precedence tests and docs updates to avoid drift between runtime behavior and operator guidance.

### 2026-04-11 to 2026-04-12: Out-of-process execution patterns

- `OutOfProcessCommandExecutor` should centralize subprocess lifecycle, NDJSON request/response matching, and cancellation timeout behavior.
- Cache discovery schemas after successful discover calls to avoid repeated roundtrips.
- Keep subprocess stdin writes serialized and pair responses via request IDs.

### 2026-04-09 to 2026-04-10: Large-result and harness stability patterns

- Large command results need shaping/limits and asynchronous invocation paths to reduce hang risk.
- Integration harnesses should avoid redundant builds and ensure child process tree cleanup on failures.

### 2026-03-27 baseline pattern still valid

- `app.Run()` is blocking in ASP.NET Core; any logic after it in the same flow is unreachable and should be removed.

### 2026-04-15: MimeType nullable fix (#129)

- Changed `McpResourceConfiguration.MimeType` from `string = "text/plain"` to `string?` (no default).
- The model-level default was shadowing the validator's `IsNullOrWhiteSpace` check — validator warning for absent MimeType never fired.
- `McpResourceHandler` already applied `IsNullOrWhiteSpace(r.MimeType) ? "text/plain" : r.MimeType` in both `HandleListAsync` and `HandleReadAsync` — no handler code changes needed.
- Test stub `McpResourceDefinition` in `PoshMcp.Tests/Models/McpResourceConfig.cs` updated to `string? MimeType` to mirror server type.
- Binding test `McpResourceDefinition_MimeType_DefaultsToTextPlain_WhenOmitted` renamed to `McpResourceDefinition_MimeType_IsNull_WhenOmitted` and asserts `null` — runtime fallback is in handler, not model.
- Pre-existing build warnings (5x CS8602 in `McpToolFactoryV2.cs`) are unrelated and not introduced by this fix.
- **Commit:** `6a93c3d` on `squad/129-fix-mimetype-nullable`

### 2026-04-18: Issue #129 MimeType Fix Completion (PR #130)

- Fix committed `6a93c3d` and rebased by Coordinator into worktree `poshmcp-129`.
- PR #130 opened at https://github.com/usepowershell/PoshMcp/pull/130.
- All 39 backend tests pass; validator warning now fires correctly.
- Handoff to Fry: test verification found no Skip attribute needed; test logic already correct.

## Archive Note

Detailed prior history was archived to `history-archive.md` on 2026-04-14 when this file exceeded the 15 KB Scribe threshold.
