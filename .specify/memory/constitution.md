<!-- Sync Impact Report
  Version change: 0.0.0 → 1.0.0
  Added sections: All (initial constitution)
  Modified principles: None (new)
  Removed sections: None
  Templates requiring updates: ✅ constitution.md updated
  Follow-up TODOs: None
-->

# PoshMcp Constitution

## Core Principles

### I. PowerShell-First

Every capability MUST originate as a PowerShell command. PoshMcp exists to
transform existing PowerShell expertise into AI-consumable tools with zero
code changes. PowerShell commands are the atomic unit of functionality —
the server discovers, introspects, and exposes them automatically. No
manual tool registration, no wrapper code, no boilerplate. If a PowerShell
expert writes a function, PoshMcp MUST be able to serve it.

### II. Protocol Compliance

PoshMcp MUST strictly adhere to the Model Context Protocol specification
and JSON-RPC 2.0. All request/response handling MUST follow the MCP
standard methods (`initialize`, `tools/list`, `tools/call`). Error
responses MUST use proper MCP error codes. Communication over stdio MUST
be well-formed JSON-RPC. Protocol correctness is non-negotiable — an
incompatible server is a broken server.

### III. Defensive JSON Handling

All JSON parsing MUST check token types before casting (`JValue` vs
`JObject` vs `JArray`). MCP responses can vary in structure — the code
MUST handle structural variation gracefully. Use `Newtonsoft.Json`
(`JObject`, `JArray`, `JValue`) for response parsing and
`JsonConvert.SerializeObject()` for serialization. Never assume a
fixed response shape from PowerShell command output.

### IV. Test-Driven Quality (NON-NEGOTIABLE)

Tests MUST accompany every change. The test suite is organized into three
tiers that MUST all pass before any merge:

- **Unit tests** (`PoshMcp.Tests/Unit/`): Isolated component tests
- **Functional tests** (`PoshMcp.Tests/Functional/`): Feature-specific behavior tests
- **Integration tests** (`PoshMcp.Tests/Integration/`): End-to-end tests using
  `InProcessMcpServer` with `ExternalMcpClient` over real stdio

All tests extend `PowerShellTestBase` for consistent infrastructure.
After any code change, run `dotnet format` and `dotnet test` to verify
formatting and correctness.

### V. Observability by Default

Every execution path MUST be observable. PoshMcp includes OpenTelemetry
integration, correlation IDs (`OperationContext`), structured logging via
`Microsoft.Extensions.Logging`, and health check endpoints (`/health`,
`/health/ready`). New features MUST include appropriate telemetry.
Debugging MUST be possible without attaching a debugger — logs and
metrics MUST tell the story.

### VI. Security and Isolation

PowerShell runspaces MUST be isolated per session in web mode. Command
exposure MUST be configurable via include/exclude patterns in
`appsettings.json`. Azure Managed Identity MUST be supported transparently
when deployed to Azure. No secrets in source code. No unrestricted command
execution. The operator MUST always control what is exposed.

### VII. Simplicity and Performance

Start simple. Avoid premature abstraction. PowerShell runspace creation is
expensive — reuse runspaces when possible. Cache function metadata to
avoid repeated reflection. Dynamic assembly generation MUST be cached
(`PowerShellAssemblyGenerator`). Use async/await throughout with proper
synchronization. Every new abstraction MUST justify its complexity.

## Technology Constraints

- **Runtime**: .NET 8 (`net8.0`), C# with nullable enabled, implicit usings disabled
- **PowerShell**: Microsoft.PowerShell.SDK 7.4.x
- **MCP**: ModelContextProtocol NuGet package (preview)
- **JSON**: Newtonsoft.Json for all JSON manipulation
- **Testing**: xUnit with shared test infrastructure (`PowerShellTestBase`)
- **Observability**: OpenTelemetry (metrics, tracing, console exporter)
- **Deployment**: Docker (multi-stage builds) + Azure Container Apps
- **Configuration**: `appsettings.json` with environment-specific overlays
  (`appsettings.azure.json`, `appsettings.modules.json`)
- **No trailing whitespace** in any file

## Development Workflow

- **Build**: `dotnet build` from solution root
- **Test**: `dotnet test` — all three tiers MUST pass
- **Format**: `dotnet format` — MUST produce no changes before merge
- **Configuration**: PowerShell functions declared in `appsettings.json`
  under `PowerShellConfiguration.FunctionNames`; the server dynamically
  discovers and exposes them
- **Dual Transport**: stdio mode (`PoshMcp.Server`) for MCP clients,
  HTTP mode (`PoshMcp.Web`) for web integration — both MUST be maintained
- **Debugging**: Use `TestOutputHelper` for test log capture; enable
  detailed logging for MCP communication troubleshooting
- **File naming**: PascalCase for C# classes/methods, test files end
  with `Tests.cs`, integration tests in `Integration/` namespace

## Governance

This constitution supersedes all other development practices for PoshMcp.
All changes MUST verify compliance with these principles. Amendments
require documentation of rationale and a migration plan for any breaking
changes. Complexity MUST be justified against the Simplicity principle.
Use DESIGN.md and .squad/decisions.md for architectural decision records.

**Version**: 1.0.0 | **Ratified**: 2026-04-06 | **Last Amended**: 2026-04-06
