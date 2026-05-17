# Hermes decision note — log-forging revision

**Date:** 2026-05-17T08:15:00-05:00
**Author:** Hermes
**Status:** Proposed
**Related:** #277, PR #278

## Decision
In `PoshMcp.Server\PowerShell\PowerShellAssemblyGenerator.cs`, every log sink that can receive user-controlled or environment-controlled string data must sanitize that value with `LogSanitizer.Scrub()` at the `ILogger` call site, and prefer structured logging over interpolated log strings.

## Why
Farnsworth's PR review found additional nearby sinks outside the original CodeQL alert set. The safe pattern is to treat command names, property names, filter scripts, and exception messages as untrusted at log sinks even when they are only helper diagnostics, because CodeQL closes `cs/log-forging` only when the sink arguments themselves are scrubbed.

## Applied in this revision
- generation-time command failure/skip logs
- `_MaxResults` validation warning
- cached output sort/filter/group helper diagnostics
- invalid filter-script warning with scrubbed script and scrubbed exception message
