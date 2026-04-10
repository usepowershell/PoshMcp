# Hermes Work History
- **20260403T135630Z**: ✓ Docker fixes & scripts reviews compiled and merged into decision ledger.
- **20260408T000000Z**: ✓ Reviewed/recorded deploy.ps1 hardening for transient ACR OAuth EOF failures: bounded retry loops, transient error classification, and improved failure diagnostics.
# Hermes Work History
## Project Context
**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski
**Key Files:**
- `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs` - Singleton runspace management
- `PoshMcp.Server/PowerShell/PowerShellRunspaceImplementations.cs` - Runspace implementations
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` - Dynamic assembly generation
- `PoshMcp.Server/PowerShell/PowerShellCleanupService.cs` - Cleanup lifecycle
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` - Configuration model
### 2026-04-03: Session Summary
**Status:** 2026-03-27 work (PowerShell streams refactoring, multi-tenant review, deployment script patterns) complete.
**Review Results:** Amy's multi-tenant implementation APPROVED (9/10 PowerShell quality).

### 2026-04-08: Serialization normalization fixes recorded

**Context:** Closed out the serializer migration fixes for string and nested object handling.

**Key learnings:**
- Scalar `PSObject.BaseObject` values need an early leaf-value path before property enumeration
- Nested PowerShell and CLR objects should be normalized into JSON-safe scalars, dictionaries, and arrays before `System.Text.Json` runs
- Serialization fixes need paired coverage so live execution and cached outputs preserve the same shape

### 2026-07: Large result set hang analysis (Get-Process)

**Context:** Diagnosed why `Get-Process` and similar cmdlets hang when called via MCP.

## Learnings

**Execution pipeline flow (key facts):**
- `ExecutePowerShellCommandTyped` (`PowerShellAssemblyGenerator.cs:534`) is the single entry point for all tool invocations.
- It calls `runspace.ExecuteThreadSafeAsync<string>(ps => { ... return Task.FromResult(...) })` — the lambda is synchronous; it never awaits anything.
- `InvokePowerShellSafe` (line 1008) calls `ps.Invoke()` — fully synchronous, no CancellationToken support.
- The singleton `PowerShellRunspaceHolder` guards the runspace with `SemaphoreSlim(1,1)`, so a hung invocation blocks all subsequent tool calls.
- A `TimeoutException` catch path exists in the outer try/catch, but nothing in the code ever raises it — there is effectively zero timeout enforcement.

**Serialization depth hazard for CLR objects:**
- `PowerShellObjectSerializer.GetSafeProperties` wraps any CLR object in a `PSObject` and enumerates ALL reflected properties.
- `System.Diagnostics.Process` has ~50 properties. Several (`Modules`, `MainModule`, `Threads`, `Handle`) make Win32 API calls that can block indefinitely on protected or system processes — these stalls are not caught by the surrounding `try/catch` because they don't throw, they block.
- 200-300 processes × 50 properties each = ~10,000–15,000 property accesses per `Get-Process` call.

**`Tee-Object` in the pipeline amplifies memory pressure:**
- Every tool invocation pipes through `Tee-Object -Variable LastCommandOutput` to cache results.
- For `Get-Process`, this keeps all 200-300 live `Process` objects (with OS handles) in memory simultaneously through the serialization pass.

**Recommended fix order:**
1. **Result count cap** (Approach B, quick win): truncate to ~50 results before serialization; include totalCount in response.
2. **Property shaping for known CLR types** (Approach A, medium effort): type-specific shapersfor `Process`, `Service`, `FileInfo` that emit only AI-useful properties.
3. **Async invocation with CancellationToken** (Approach C, high effort): use `InvokePowerShellSafeAsync` in the main execution path and thread the CancellationToken through.

### 2026-07: PropertySetDiscovery and serializer refinement

**Context:** Phase 3 crash recovery — implemented DefaultDisplayPropertySet discovery and refined serializer.

**Key files:**
- `PoshMcp.Server/PowerShell/PropertySetDiscovery.cs` — Discovery of DefaultDisplayPropertySet via Get-Command OutputType + Get-TypeData. Uses temporary runspace, ConcurrentDictionary cache, best-effort (returns null on failure).
- `PoshMcp.Server/PowerShell/PowerShellObjectSerializer.cs` — Refined `NormalizePSPropertyValue`: IDictionary now recursively normalized instead of `.ToString()` (dictionaries are bounded key-value maps). IEnumerable kept as `.ToString()` (expensive to enumerate, e.g., ProcessModuleCollection).

**Design decisions:**
- PropertySetDiscovery uses temporary runspace, NOT the singleton — runs at assembly generation time before the server is fully initialized.
- Two-step lookup: Get-Command → OutputType names → Get-TypeData → DefaultDisplayPropertySet.ReferencedProperties.
- DiscoverAll() shares a single runspace across all commands for startup efficiency.
- IDictionary vs IEnumerable split in shallow path: dictionaries are safe JSON maps; enumerables may trigger OS calls.

### 2026-04-10: Module discovery order during tool generation

**Context:** Validated startup/tool-generation ordering for configured modules and fixed discovery order.

**Key learnings:**
- `McpToolFactoryV2.GetAvailableCommandsWithMetadata` previously attempted `Get-Command -Name` discovery before any explicit `Import-Module` call.
- `PowerShellEnvironmentSetup` exists but is not part of the current startup/tool-generation path, so environment-level module imports are not automatically applied during discovery.
- For reliable by-name discovery when auto-loading is disabled, configured modules must be imported first in the same runspace used by tool generation.
- Safe behavior is best-effort import (warn and continue on import failure) so one bad module does not block all discovery.
