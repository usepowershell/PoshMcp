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

### 2026-04-10: Config doctor MCP exposure review

**Context:** Reviewed the architecture and MCP contract for exposing doctor-style diagnostics through the MCP tool surface without making it universally available.

**Key learnings:**
- The existing runtime doctor command already centralizes config, transport, and tool expectation diagnostics inside `PoshMcp.Server/Program.cs`, so MCP exposure should reuse that contract instead of creating a second diagnostic path.
- Diagnostic MCP exposure belongs behind the same configuration gate used for dynamic reload tooling so normal production surfaces stay minimal by default.
- Contract review for this feature should stay anchored to `GetExpectedToolNames(...)` and the doctor command's resolved-settings flow, because those are the stable points that define what "healthy" means for config-aware tooling.
