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
- `SessionStateProxy.SetVariable()` goes through a per-call lock + dictionary lookup + event chain in the PS SDK. For preference variable reset (8 calls per warm cycle), use the internal session-state table (`SessionStateInternalAccessor.TryGetTables()`) and set `PSVariable.Value` directly — this is already done for variable/function/alias cleanup and should be shared.
- `proxy.Path.SetLocation(root)` is expensive and unnecessary when the runspace is already at the drive root (the common case after the first reset). Always read `CurrentFileSystemLocation` first and skip `SetLocation` when equal to root.
- `ps.Invoke()` in the PS SDK creates a `PowerShellAsyncResult` holding a `ManualResetEvent` (OS WaitHandle) via the `AsyncWaitHandle` property accessed during `WaitForJobCompletion`. This handle accumulates until GC finalization under load. Use `BeginInvoke/EndInvoke` with explicit `asyncResult.AsyncWaitHandle?.Dispose()` in a `finally` block to eliminate per-call handle backlog.
- `PSDataCollection<PSObject>` as the output buffer for `BeginInvoke` also holds internal semaphore handles; wrapping in `using` disposes those. Call `output.ReadAll()` to copy results before disposal.
- `TryGetTables()` should be called once per `ResetCore` call at the start of the try block, before all steps that can use the fast path (preference reset, $Error clear, variable/function/alias cleanup).