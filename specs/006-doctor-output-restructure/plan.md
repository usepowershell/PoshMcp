# Implementation Plan: Doctor Output Restructure

**Branch**: `doctor-output-restructure` | **Date**: 2026-07-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/doctor-output-restructure/spec.md`

## Summary

The `poshmcp doctor` command produces a flat wall of ~50 lines of plain
text and a flat JSON object with 25+ top-level keys. This restructure
groups diagnostics into logical sections (Summary, Runtime Settings,
Environment Variables, PowerShell, Functions/Tools, MCP Definitions,
Warnings) with a summary banner, status symbols, and section headers in
text output, and matching nested objects in JSON output. The
implementation extracts rendering logic into `DoctorTextRenderer` and the
data model into a `DoctorReport` record hierarchy, making future section
additions trivial.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: `System.Text.Json` for serialization, `ModelContextProtocol.Server` for MCP tool registration
**Storage**: N/A (diagnostic output only)
**Testing**: xUnit with `PowerShellTestBase` infrastructure
**Target Platform**: Cross-platform (.NET 10 runtime)
**Project Type**: CLI / MCP server
**Performance Goals**: Doctor output generation under 2 seconds (existing baseline)
**Constraints**: No trailing whitespace; `dotnet format` clean; all three test tiers pass
**Scale/Scope**: ~300 lines of new code (records + renderer), ~200 lines removed from Program.cs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. PowerShell-First | ✓ Pass | No PowerShell changes; doctor inspects PS state only |
| II. Protocol Compliance | ✓ Pass | JSON output restructure is additive; MCP tool contract updated |
| III. Defensive JSON Handling | ✓ Pass | New records use `System.Text.Json` with explicit serialization |
| IV. Test-Driven Quality | ✓ Pass | Unit tests for renderer and report; integration tests updated |
| V. Observability by Default | ✓ Pass | No observability regression; doctor is itself an observability tool |
| VI. Security and Isolation | ✓ Pass | No security surface changes |
| VII. Simplicity and Performance | ✓ Pass | Extracts complexity from Program.cs into focused classes; no new abstractions beyond record types |
| No trailing whitespace | ✓ Pass | Enforced by `dotnet format` |

## Project Structure

### Documentation (this feature)

```text
specs/doctor-output-restructure/
├── spec.md              # Feature specification
├── plan.md              # This file
└── tasks.md             # Task breakdown
```

### Source Code (repository root)

```text
PoshMcp.Server/
├── Diagnostics/
│   ├── DoctorReport.cs          # Record hierarchy for structured JSON
│   └── DoctorTextRenderer.cs    # Static class for text formatting
├── Program.cs                   # RunDoctorAsync + BuildDoctorJson (simplified)
└── ...

PoshMcp.Tests/
├── Unit/
│   ├── DoctorReportTests.cs     # Serialization and health computation tests
│   └── DoctorTextRendererTests.cs # Text formatting tests
├── Integration/
│   └── (existing doctor tests updated)
└── ...
```

**Structure Decision**: New files are placed in a `Diagnostics/` folder
within the existing `PoshMcp.Server` project. This is a lightweight
grouping — no new project, no new namespace hierarchy beyond
`PoshMcp.Server.Diagnostics`. Tests follow the existing convention of
mirroring the source structure under `Unit/`.

## Phases

### Phase 1: Extract DoctorReport Record Hierarchy (data model)

Define C# records that represent the structured JSON output. This is a
pure addition — no existing code changes. The records include:

- `DoctorReport` (top-level): contains all sections as properties
- `DoctorSummary`: `Status`, `GeneratedAtUtc`, `ConfigurationPath`
- `ResolvedSetting`: `Value` (string?), `Source` (string)
- `RuntimeSettingsSection`: 6 `ResolvedSetting` properties
- `EnvironmentVariablesSection`: dictionary of env var name → value
- `PowerShellSection`: version, module paths, OOP module paths
- `FunctionsToolsSection`: counts, status list, tool names
- `McpDefinitionsSection`: resources + prompts sub-objects
- Health status computation as a static method on `DoctorReport`

All records use `[JsonPropertyName]` attributes for camelCase output.

### Phase 2: Extract DoctorTextRenderer Static Class (text formatting)

Create `DoctorTextRenderer` with:

- `Render(DoctorReport report) → string`: main entry point
- Private methods: `RenderBanner`, `RenderRuntimeSettings`,
  `RenderEnvironmentVariables`, `RenderPowerShell`,
  `RenderFunctionsTools`, `RenderMcpDefinitions`, `RenderWarnings`
- Section header helper: `RenderSectionHeader(string name) → string`
- Status symbol helper: `StatusSymbol(bool ok) → string` (✓/✗)

This is a pure addition — no existing code changes yet.

### Phase 3: Wire DoctorReport into RunDoctorAsync and BuildDoctorJson

Refactor `RunDoctorAsync` in Program.cs to:

1. Build a `DoctorReport` instance from the existing diagnostic data
2. For JSON format: serialize the `DoctorReport` directly
3. For text format: pass `DoctorReport` to `DoctorTextRenderer.Render`

This phase removes the inline `Console.WriteLine` calls and the anonymous
object in `BuildDoctorJson`, replacing them with the new types.

### Phase 4: Add Environment Variables Section

Populate the `EnvironmentVariablesSection` by reading the 8 relevant
environment variables (`POSHMCP_TRANSPORT`, `POSHMCP_CONFIGURATION`,
`POSHMCP_MCP_PATH`, `POSHMCP_SESSION_MODE`, `POSHMCP_RUNTIME_MODE`,
`POSHMCP_LOG_LEVEL`, `POSHMCP_LOG_FILE`, `ASPNETCORE_ENVIRONMENT`).
Add the corresponding text render method.

### Phase 5: Add Summary Banner with Health Computation

Implement the health status computation logic:
- `errors`: any missing functions OR any resource/prompt validation errors
- `warnings`: any configuration warnings (legacy FunctionNames, etc.)
- `healthy`: none of the above

Wire the banner into `DoctorTextRenderer.RenderBanner` using box-drawing
characters.

### Phase 6: Update get-configuration-troubleshooting MCP Tool

Update `CreateConfigurationTroubleshootingToolInstance` to use
`DoctorReport` serialization instead of `BuildDoctorJson`. This ensures
the MCP tool returns the same nested JSON structure as the CLI.

### Phase 7: Tests

Add unit tests for:
- `DoctorReport` serialization (verify JSON structure and key names)
- `DoctorReport.ComputeStatus` (healthy/warnings/errors scenarios)
- `DoctorTextRenderer.Render` (verify section headers, symbols, banner)
- `DoctorTextRenderer` individual section methods

Update existing integration tests:
- `ProgramDoctorToolExposureTests` to expect new JSON structure
- Any tests that assert on doctor text output format

### Phase 8: Cleanup

- Remove the old `BuildDoctorJson` method from Program.cs (now replaced)
- Remove inline `Console.WriteLine` doctor output from Program.cs
- Run `dotnet format` to ensure no trailing whitespace
- Run full test suite (`dotnet test`) to verify no regressions

## Complexity Tracking

No constitution violations — no new projects, no repository patterns, no
external dependencies. The only new abstractions are C# record types
(which are the simplest possible data carriers) and one static class.

| Decision | Rationale | Alternative Rejected |
|----------|-----------|---------------------|
| Records, not classes | Immutable value types with structural equality; ideal for diagnostic snapshots | Mutable classes would add unnecessary complexity |
| Static renderer, not instance | No state needed; pure function from DoctorReport → string | Instance class would require DI registration for no benefit |
| Single Diagnostics/ folder, not new project | ~200 lines of code doesn't justify a new assembly | Separate project would violate Simplicity principle |
