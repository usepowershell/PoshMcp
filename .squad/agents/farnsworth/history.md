# Farnsworth Work History
## Project Context
Project: PoshMcp - Model Context Protocol (MCP) server for PowerShell
Tech Stack: .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
Primary User: Steven Murawski
Current Priorities:
- Improve maintainability (structured errors, config validation)
- Enhance resilience (circuit breakers, timeouts, retry logic)
- Boost observability (metrics, health checks, diagnostics)
### 2026-04-03: Session Summary
**Status:** 2026-03-27 work (Docker architecture, Azure infrastructure, health checks) complete and tested.
**Current Phase:** Phase 1 observability features (health checks, correlation IDs) and Azure Container Apps infrastructure deployed.

## Learnings

### 2026-07: Large result set performance proposal

**Key findings from codebase analysis:**
- `Tee-Object -Variable LastCommandOutput` is injected unconditionally into every command pipeline at `PowerShellAssemblyGenerator.cs:677-680`
- This caching supports four utility tools: `get-last-command-output`, `sort-last-command-output`, `filter-last-command-output`, `group-last-command-output`
- Serialization walks all gettable `PSObject.Properties` up to depth 4 in `PowerShellObjectSerializer.cs`
- No property filtering exists — Get-Process returns ~80 properties per object when PowerShell's own display uses 5
- Pipeline construction lives in `ExecutePowerShellCommandTyped()` (static method called by IL-generated code)
- Configuration model is `PowerShellConfiguration.cs` — currently has no per-function override capability
- Proposed adding `PerformanceConfiguration` and `FunctionOverride` classes; Select-Object injection; conditional Tee-Object
- Proposal written to `specs/large-result-performance.md`

**Architecture decisions:**
- Tee-Object should default to OFF (opt-in) — most MCP callers never use replay tools
- Select-Object with DefaultDisplayPropertySet should default to ON — 95%+ payload reduction for common commands
- `_AllProperties` framework parameter (underscore prefix convention) lets callers bypass filtering per-call
- Property discovery via `Get-TypeData` and `[OutputType]` avoids executing commands at schema generation time
- Per-function overrides via `FunctionOverrides` dictionary in config; explicit `DefaultProperties` list takes priority

### 2026-04-08: dotnet tool packaging architecture

**Decision summary:**
- Package `PoshMcp.Server` only as a dotnet tool (`PackAsTool=true`, `ToolCommandName=poshmcp`). The Web project is a hosted HTTP service — wrong shape for a CLI tool.
- Tool command: `poshmcp`. Users invoke `poshmcp serve` or `poshmcp list-tools`.
- Add `<Pack>false</Pack>` to all three `<Content>` items in Server.csproj. SDK-style `<Content>` items go into NuGet `contentFiles/` by default; user-owned config files must not be bundled. `default.appsettings.json` as `EmbeddedResource` is already correct.
- `Microsoft.PowerShell.SDK` embeds the PowerShell runtime — users do NOT need a standalone `pwsh` install. Document prominently.
- No `PublishSingleFile` or `PublishTrimmed` — dotnet tools use standard publish output. Dynamic assembly generation in `McpToolFactoryV2` is not trim-safe.
- Add `.config/dotnet-tools.json` local manifest for developer/CI workflow. Global install is primary for end users.
- `net10.0` is the hard prerequisite; must be in README.
- Start version at `0.1.0`.
- PackageId: `PoshMcp`, Authors: `Steven Murawski`, License: `MIT` (needs LICENSE file at repo root).

### 2026-04-10: Recovery guidance for out-of-process runtime and doctor tooling

**Key learnings:**
- Out-of-process end-to-end tests should stay as documented stubs until `Program.cs` and `InProcessMcpServer` expose a supported `--runtime-mode` surface.
- Recovery work should normalize all live startup helpers to `POSHMCP_TRANSPORT`; `POSHMCP_MODE` is retired architectural residue.
- If the out-of-process design resumes, a persistent `pwsh` subprocess over localhost TCP remains the lowest-complexity cross-platform direction.
- Troubleshooting surfaces should stay read-only and explicitly gated even when exposed as built-in MCP tools.

### 2026-04-11: Out-of-process execution comprehensive plan

**Key architecture decisions:**
- Communication protocol changed from localhost TCP (2026-04-10 note) to **stdin/stdout ndjson** — simpler, no port/firewall concerns, native `Process` stream redirection.
- 6-phase plan: stubs (fix 13 build errors) → subprocess lifecycle → discovery → invocation → IL assembly gen → integration tests.
- `oop-host.ps1` PowerShell script runs inside persistent `pwsh` subprocess, handles discover/invoke/ping/shutdown via ndjson.
- Crash recovery: auto-restart with exponential backoff (3 retries/5 min), re-discovery after restart.
- No mixed mode in v1 — RuntimeMode is server-wide. Per-function routing deferred.
- 5 new types in `PoshMcp.Server.PowerShell.OutOfProcess` namespace: `RuntimeMode` enum, `ICommandExecutor`, `RemoteToolSchema`+`RemoteParameterSchema`, `OutOfProcessCommandExecutor`, `OutOfProcessToolAssemblyGenerator`.
- Full spec: `specs/out-of-process-execution.md`
- Key files already referencing OOP: `Program.cs` (lines 13, 934, 945, 1928, 2029, 2375-2389), `McpToolFactoryV2.cs` (lines 15, 39, 72, 314-355), `PowerShellConfiguration.cs` (line 15).

### 2026-04-11: CLIXML evaluation for OOP transport

**Decision:** Rejected CLIXML in favor of keeping ndjson/JSON for OOP subprocess communication.
**Core reasoning:** MCP output is JSON — CLIXML would add a triple conversion pipeline (CLIXML→PSObject→FlattenPSObject→JSON) replacing a direct JSON path. The server has the PS SDK loaded so CLIXML is technically parseable, but the type fidelity it provides is lost at the JSON output boundary. CLIXML is also 5-10x larger on the wire, adding performance penalty for large result sets (already flagged as Risk #4 in spec).
**Key insight:** OOP isolation targets heavy module loading, not the SDK. The SDK can deserialize CLIXML, but doing so reconstructs full PSObject graphs that we immediately flatten — waste of CPU/memory for throwaway fidelity.
**If types prove problematic:** Handle surgically in `oop-host.ps1` with per-type handlers rather than switching transport format. ConvertTo-Json -Depth 4 -Compress remains the correct approach.
**Decision file:** `.squad/decisions/inbox/farnsworth-clixml-evaluation.md`
