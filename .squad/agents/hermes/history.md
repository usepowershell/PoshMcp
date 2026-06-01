# Hermes Work History

## Current Summary (compacted 2026-06-01T00:00:00Z)
Detailed prior PowerShell/OOP entries were archived to `history-archive.md` because this file exceeded the 15KB Scribe hard gate. Keep this file focused on active PowerShell implementation patterns and recent diagnostics.

## Learnings
- PowerShell type-name collision: alias `System.Management.Automation.PowerShell` as `PSPowerShell` when working inside the `PoshMcp.Server.PowerShell` namespace.
- Reusing a `PowerShell` instance across invocations requires `ps.Commands.Clear()` between iterations; cleanup in catch paths should be best-effort.
- OOP wire-format extensions should be additive nullable fields plus parallel payloads; Newtonsoft unknown-field and missing-null behavior gives backward compatibility in both directions.
- Runtime doctor parity requires the same `IToolImportSourceTracker` instance used during discovery; reset it at the start of each discovery cycle and pass it into all report builders.
- Log-forging remediation must scrub untrusted values at the actual `ILogger` call site, including command names, property names, filter scripts, and exception messages.
- HTTP non-MCP requests without `Mcp-Session-Id` must use the stable `default` runspace key; do not key health probes by connection or trace IDs.
- Static MCP command resources in HTTP mode currently execute through `SessionAwarePowerShellRunspace`; noun-derived resources in OOP runtime can use `ICommandExecutor`, which explains different live behavior between static and noun resources.