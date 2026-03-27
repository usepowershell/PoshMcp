# Fry Work History

## Project Context

**Project:** PoshMcp - Model Context Protocol (MCP) server for PowerShell
**Tech Stack:** .NET 8, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
**Primary User:** Steven Murawski

**Test Structure:**
- 53 total tests across unit, functional, and integration levels
- `PoshMcp.Tests/Unit/` - 3 files
- `PoshMcp.Tests/Functional/` - 6 files
- `PoshMcp.Tests/Integration/` - 4 files
- `PoshMcp.Tests/Shared/` - Test infrastructure (PowerShellTestBase, TestOutputLogger)

**Key Test Files:**
- `IntegrationTests.cs` - Full workflow tests
- `McpServerIntegrationTests.cs` - MCP protocol tests
- `SimpleAssemblyTests.cs`, `ParameterTypeTests.cs`, `OutputTypeTests.cs` - Unit tests

## Learnings

*Learnings from work will be recorded here automatically*
