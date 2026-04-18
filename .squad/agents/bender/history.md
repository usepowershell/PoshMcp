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

## Archive Note

Detailed prior history was archived to `history-archive.md` on 2026-04-14 when this file exceeded the 15 KB Scribe threshold.
