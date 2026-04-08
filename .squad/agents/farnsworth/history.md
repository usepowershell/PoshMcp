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
