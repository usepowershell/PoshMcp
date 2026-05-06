# Hermes Work History
- **20260403T135630Z**: ✓ Docker fixes & scripts reviews compiled and merged into decision ledger.
- **20260408T000000Z**: ✓ Reviewed/recorded deploy.ps1 hardening for transient ACR OAuth EOF failures: bounded retry loops, transient error classification, and improved failure diagnostics.
- **20260418T000000Z**: ✓ Rebased feature/002-tests onto main; resolved 5 add/add conflicts (McpResources + McpPrompts config classes, kept main implementation); removed Skip attrs from 16 integration tests (8 McpResources + 8 McpPrompts); all 16 passed; force-pushed.
# Hermes Work History
## Project Context
**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski
**Key Files:**
- `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs` - Singleton runspace management
- `PoshMcp.Server/PowerShell/PowerShellRunspaceImplementations.cs` - Runspace implementations
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` - Dynamic assembly generation
- `PoshMcp.Server/PowerShell/PowerShellCleanupService.cs` - Cleanup lifecycle
- `PoshMcp.Server/PowerShell/PowerShellConfiguration.cs` - Configuration model
## Pre-2026-05-06 Summary (archived to history-archive.md)
- 2026-04-03: Multi-tenant impl review (Amy) approved 9/10; PowerShell streams refactoring closed.
- 2026-04-08: Serializer migration — scalar PSObject.BaseObject leaf-value path; nested PS/CLR objects normalized before System.Text.Json.
- 2026-04-10/11: OOP execution plan filed; oop-host.ps1 created (Issue #57 phases 2-4); OOP environment customization (#67).
- 2026-04-18: Rebased feature/002-tests, resolved 5 add/add conflicts, removed Skip on 16 integration tests, all green.
- Get-Process hang analysis: ExecutePowerShellCommandTyped → ExecuteThreadSafeAsync (sync lambda) → InvokePowerShellSafe.Invoke() (no CT). Singleton runspace + SemaphoreSlim(1,1) blocks all subsequent calls. Serializer reflects ALL props on CLR objects (Process has ~50, several block on Win32). Tee-Object pipeline retains live Process objects through serialization.
- PropertySetDiscovery + serializer refinement work.
- Recovery learnings: module layout and host-script safety.
- Unserializable parameter type filtering (Issue #89).
- doctor command resolution diagnostics (Issue #91): IsolatedPowerShellRunspace per doctor call; ConfiguredFunctionStatus positional record.
### 2026-05-06: OOP runspace pool vs multi-process R&D plan (Issue #65)

**Context:** Phases 1-4 of spec 004 (subprocess lifecycle, ndjson protocol, setup, discover, invoke) are complete and shipping. Today's single-subprocess executor serializes invokes through _sendLock (SemaphoreSlim(1,1)) — protocol layer is already async-correlated by id, but only one runspace exists.

**Key files surveyed:**
- PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs — single-process lifecycle, ndjson framing, _pending ConcurrentDictionary keyed by GUID id, ReadLoopAsync + StderrLoopAsync, IsNonJsonPowerShellStreamLine backstop for stream pollution
- PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1 — single-runspace host, synchronous Invoke-*Handler functions, Write-NdjsonResponse to [Console]::Out
- specs/004-out-of-process-execution/spec.md — FR-051 mandates serialization today; future pool work will need to revisit

**Plan filed:** specs/004-out-of-process-execution/runspace-pool-experiment-plan.md

**Key design decisions in the plan:**
- Option A (runspace pool in one process): use [runspacefactory]::CreateRunspacePool with InitialSessionState pre-warmed with modules; introduce a sync-locked stdout writer because multiple completion callbacks will write concurrently; clear $Error per-invoke (it's per-runspace state); ship as new file oop-host-pool.ps1 selected by SubprocessHostMode config flag rather than mutating the working host
- Option B (process pool): wrap N OutOfProcessHost instances in OutOfProcessSubprocessPool; dispatch via Channel<OutOfProcessHost> for FIFO fairness; per-host crash/restart logic unchanged; SubprocessPoolSize: 1 collapses to current behavior
- Refactor first: extract OutOfProcessHost from OutOfProcessCommandExecutor so per-process state is reusable by both prototypes (issue #1 in the phasing)
- Benchmark harness: separate PoshMcp.Benchmarks console project (BenchmarkDotNet for latency/throughput, custom harness for crash recovery), 8 scenarios including isolation as a pass/fail
- Hard worry: Option A shares an AppDomain → loses the isolation guarantee that motivated OOP in the first place. Benchmark scenario explicitly measures this and the plan recommends defaulting to B if neither cleanly passes isolation.

**PowerShell-specific concurrency hazards documented:**
- Stdout writes must be lock-protected (one writer for protocol channel)
- $Error is per-runspace, must be cleared inside the script block before each user command runs
- Cmdlets that write directly to [Console]::Out bypass any host-level lock; mitigations are NO_COLOR=1, System.Management.Automation.PSStyle.OutputRendering='PlainText', plus the existing IsNonJsonPowerShellStreamLine backstop on the .NET side
- ISS pre-warming avoids first-touch import cost on each new runspace lease

**Phasing recommended:** 6 follow-up issues — (1) extract OutOfProcessHost, (2) Option A prototype, (3) Option B prototype, (4) benchmark harness, (5) run benchmarks + write findings, (6) adopt winner. (2)/(3)/(4) parallelize after (1).

**Branch / PR:** squad/65-runspace-pool-experiment-plan, draft PR opened, "Refs #65" not "Closes" (issue stays open through prototype work).