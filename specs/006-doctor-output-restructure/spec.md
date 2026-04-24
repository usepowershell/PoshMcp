# Feature Specification: Doctor Output Restructure

**Spec Number**: 006
**Feature Branch**: `doctor-output-restructure`
**Created**: 2026-07-22
**Status**: Draft
**Input**: Redesign `poshmcp doctor` text and JSON output from a flat wall of lines into structured, scannable, extensible sections with status indicators and a summary banner

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Structured Text Output with Section Headers (Priority: P1)

An operator runs `poshmcp doctor` after deploying a new configuration.
Today the output is ~50 lines of unsorted plain text with no visual
hierarchy — settings, tool status, warnings, and PowerShell details are
all interleaved. The operator wants to scan the output and immediately
see: (a) whether the server is healthy, (b) which section has problems,
and (c) what to do about it. The redesigned output groups diagnostics
into clearly labelled sections with Unicode box-drawing headers, status
symbols (✓/✗/⚠), and a summary banner at the top that shows overall
health at a glance.

**Why this priority**: Text output is the primary interface for operators
running doctor from a terminal. A flat dump becomes unusable past ~30
lines and the output is about to grow substantially as auth, logging, and
MCP definitions are added.

**Independent Test**: Run `poshmcp doctor` and verify the output contains
the summary banner, every expected section header, status symbols next to
each item, and that all data currently present in the output still
appears (no information loss).

**Acceptance Scenarios**:

1. **Given** a healthy configuration with all functions found, **When** `poshmcp doctor` is run, **Then** the first three lines contain a box-drawing banner with "PoshMcp Doctor" and "✓ healthy"
2. **Given** a configuration with missing functions, **When** `poshmcp doctor` is run, **Then** the banner shows "⚠ warnings" or "✗ errors" depending on severity, and the Functions/Tools section lists each missing function with ✗ and a reason
3. **Given** a healthy configuration, **When** `poshmcp doctor` is run, **Then** the output contains section headers for: Runtime Settings, Environment Variables, PowerShell, Functions/Tools, MCP Definitions, and Warnings
4. **Given** a configuration with warnings, **When** `poshmcp doctor` is run, **Then** the Warnings section at the bottom lists all warnings with ⚠ prefix and remediation hints
5. **Given** a configuration with resource or prompt validation errors, **When** `poshmcp doctor` is run, **Then** the MCP Definitions section shows error counts and ✗-prefixed error messages

---

### User Story 2 — Structured JSON Output with Nested Sections (Priority: P1)

An MCP client calls the `get-configuration-troubleshooting` tool (or the
operator runs `poshmcp doctor --format json`) and receives a flat JSON
object with 25+ top-level keys. Parsing this programmatically requires
knowing every key name. The redesigned JSON groups diagnostics into
nested objects whose keys match the text section names — `summary`,
`runtimeSettings`, `environmentVariables`, `powerShell`,
`functionsTools`, `mcpDefinitions`, and `warnings`. Each runtime setting
includes both its value and its source. The `summary` object includes an
overall `status` field (`healthy`, `warnings`, or `errors`) computed from
the diagnostic data.

**Why this priority**: JSON output is consumed by MCP clients and
automation pipelines. A flat shape breaks when new fields are added and
forces every consumer to update their parsing. Nested sections with
stable top-level keys provide a forward-compatible contract.

**Independent Test**: Run `poshmcp doctor --format json`, parse the JSON,
and verify: (a) top-level keys are exactly `summary`,
`runtimeSettings`, `authentication`, `logging`,
`environmentVariables`, `powerShell`, `functionsTools`,
`mcpDefinitions`, `warnings`; (b) `summary.status` is one of `healthy`,
`warnings`, `errors`; (c) every value/source pair in `runtimeSettings` is
an object with `value` and `source` keys.

**Acceptance Scenarios**:

1. **Given** a healthy configuration, **When** `poshmcp doctor --format json` is run, **Then** the JSON has a `summary` object with `status: "healthy"` and `generatedAtUtc` timestamp
2. **Given** a healthy configuration, **When** the JSON is parsed, **Then** `runtimeSettings` is an object with keys `transport`, `logLevel`, `sessionMode`, `runtimeMode`, `mcpPath`, `configurationPath`, each containing `value` and `source` properties
3. **Given** a configuration with missing functions, **When** the JSON is parsed, **Then** `functionsTools.configuredFunctionStatus` contains entries with `found: false` and `summary.status` is `"warnings"` or `"errors"`
4. **Given** resource validation errors exist, **When** the JSON is parsed, **Then** `mcpDefinitions.resources` contains `configured`, `valid`, `errors`, and `warnings` fields
5. **Given** the `get-configuration-troubleshooting` MCP tool is called at runtime, **When** the response is received, **Then** it uses the same nested JSON structure as `poshmcp doctor --format json`

---

### User Story 3 — Extensible Section Architecture (Priority: P2)

A developer needs to add a new diagnostic section (e.g., Authentication
or Logging config). Today, adding a section requires interleaving new
`Console.WriteLine` calls into the middle of a 100-line method in
Program.cs and manually inserting new keys into an anonymous object in
`BuildDoctorJson`. The redesigned architecture uses a `DoctorReport`
record hierarchy for the data model and a `DoctorTextRenderer` static
class for text formatting. Adding a new section means: (1) add a new
record to `DoctorReport`, (2) populate it in `RunDoctorAsync`, (3) add a
render method to `DoctorTextRenderer`. No existing code needs to change.

**Why this priority**: The doctor output is about to grow with auth,
logging, and environment variable sections. Without an extensible
architecture, each addition increases coupling and test fragility.

**Independent Test**: Add a stub section (e.g., `authentication`) with
placeholder data and verify it appears in both text and JSON output
without modifying the renderer's main control flow method.

**Acceptance Scenarios**:

1. **Given** the `DoctorReport` record hierarchy exists, **When** a developer adds a new nested record for a section, **Then** the JSON serializer automatically includes it as a nested object with the correct key name
2. **Given** `DoctorTextRenderer` exists, **When** a developer adds a new `RenderXxxSection` method, **Then** they can add it to the render pipeline without modifying any existing render method
3. **Given** the refactored code, **When** `dotnet test` is run, **Then** all existing doctor-related tests pass without modification (the refactor is behaviour-preserving)

---

### Edge Cases

- What happens when all environment variables are unset? The Environment Variables section renders each variable with value `(not set)`.
- What happens when zero functions are configured? The Functions/Tools section shows `0/0 configured functions found` with no item list.
- What happens when PowerShell diagnostics fail (e.g., runspace creation error)? The PowerShell section shows `version: unknown` and an error message instead of module paths.
- What happens when the config file path is invalid? The summary banner shows `✗ errors` and the Runtime Settings section shows the path with an error annotation.
- What happens with very long module paths or tool names? No truncation — the terminal handles wrapping. Indentation is consistent regardless of content length.
- What happens when both resources and prompts have zero configuration? The MCP Definitions section still renders with `0 configured | 0 valid` for each.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-100**: System MUST group doctor text output into labelled sections: Summary banner, Runtime Settings, Environment Variables, PowerShell, Functions/Tools, MCP Definitions, Warnings
- **FR-101**: System MUST display a summary banner as the first output element containing the overall health status using ✓ (healthy), ⚠ (warnings), or ✗ (errors) symbols
- **FR-102**: System MUST compute overall health status from diagnostic data: `errors` if any functions are missing or any resource/prompt validation errors exist; `warnings` if configuration warnings exist; `healthy` otherwise
- **FR-103**: System MUST prefix each diagnostic item in text output with a status symbol: ✓ for healthy/found, ✗ for error/missing, ⚠ for warning
- **FR-104**: System MUST render section headers using Unicode box-drawing characters in the format `── Section Name ──────────────────────`
- **FR-105**: System MUST include source annotations for each runtime setting in text output in the format `(source)` after the value
- **FR-106**: System MUST structure JSON output as nested objects with top-level keys: `summary`, `runtimeSettings`, `environmentVariables`, `powerShell`, `functionsTools`, `mcpDefinitions`, `warnings`
- **FR-107**: System MUST represent each runtime setting in JSON as an object with `value` and `source` properties (e.g., `"transport": { "value": "stdio", "source": "env" }`)
- **FR-108**: System MUST include a `summary` object in JSON with `status` (string enum: `healthy`/`warnings`/`errors`), `generatedAtUtc` (ISO 8601 timestamp), and `configurationPath` (string)
- **FR-109**: System MUST preserve all data currently present in doctor output — no information loss from the restructure
- **FR-110**: System MUST extract text rendering logic into a dedicated `DoctorTextRenderer` static class in `PoshMcp.Server/Diagnostics/DoctorTextRenderer.cs`
- **FR-111**: System MUST extract the JSON data model into a `DoctorReport` record hierarchy in `PoshMcp.Server/Diagnostics/DoctorReport.cs`
- **FR-112**: System MUST update the `get-configuration-troubleshooting` MCP tool to return the new nested JSON structure
- **FR-113**: System MUST include an Environment Variables section showing the current values of `POSHMCP_TRANSPORT`, `POSHMCP_CONFIGURATION`, `POSHMCP_MCP_PATH`, `POSHMCP_SESSION_MODE`, `POSHMCP_RUNTIME_MODE`, `POSHMCP_LOG_LEVEL`, `POSHMCP_LOG_FILE`, and `ASPNETCORE_ENVIRONMENT`
- **FR-114**: System MUST consolidate all warnings from all sections into the Warnings section at the bottom of text output, with remediation hints where applicable
- **FR-115**: The `DoctorReport` JSON serialization MUST use camelCase property names to match the existing convention

### Key Entities

- **DoctorReport**: Top-level record containing all diagnostic sections as nested records. Serializes directly to the structured JSON output.
- **DoctorSummary**: Nested record within DoctorReport holding `status`, `generatedAtUtc`, and `configurationPath`.
- **ResolvedSetting**: Reusable record representing a configuration value with its resolution source (`value: string?`, `source: string`). Used for each runtime setting.
- **FunctionsToolsSection**: Nested record holding configured function count, found count, status list, and tool names.
- **McpDefinitionsSection**: Nested record with `resources` and `prompts` sub-objects, each containing `configured`, `valid`, `errors`, and `warnings`.
- **DoctorTextRenderer**: Static class responsible for converting a `DoctorReport` into formatted text output. One public entry method, one private method per section.

### Text Output Design

```
╔═══════════════════════════════════════╗
║  PoshMcp Doctor  ✓ healthy            ║
╚═══════════════════════════════════════╝

── Runtime Settings ──────────────────────
  configuration : appsettings.json              (default)
  transport     : stdio                         (env)
  log-level     : Information                   (default)
  session-mode  : (not set)                     (default)
  runtime-mode  : InProcess                     (default)
  mcp-path      : (not set)                     (default)

── Environment Variables ─────────────────
  POSHMCP_TRANSPORT       : stdio
  POSHMCP_CONFIGURATION   : (not set)
  POSHMCP_MCP_PATH        : (not set)
  POSHMCP_SESSION_MODE    : (not set)
  POSHMCP_RUNTIME_MODE    : (not set)
  POSHMCP_LOG_LEVEL       : (not set)
  POSHMCP_LOG_FILE        : (not set)
  ASPNETCORE_ENVIRONMENT  : (not set)

── PowerShell ────────────────────────────
  version       : 7.4.6
  module-paths  : 3
    - /usr/local/share/powershell/Modules
    - /root/.local/share/powershell/Modules
    - /opt/microsoft/powershell/7/Modules
  oop-module-paths : 0

── Functions/Tools ───────────────────────
  ✓ 5/5 configured functions found
  tools discovered: 12
  - Get-Process → get_process [✓ FOUND] matched: get_process_name, get_process_id
  - Get-Service → get_service [✓ FOUND] matched: get_service_default
  - Get-Missing → get_missing [✗ MISSING] reason: command not found in session
  tool names:
  - get_process_name
  - get_process_id
  - get_service_default

── MCP Definitions ───────────────────────
  resources : 3 configured | 3 valid
  prompts   : 2 configured | 2 valid

── Warnings ──────────────────────────────
  ⚠ FunctionNames is deprecated. Migrate to CommandNames.
    💡 poshmcp update-config --add-command Get-Process --remove-command Get-Process
```

### JSON Output Design

```json
{
  "summary": {
    "status": "healthy",
    "generatedAtUtc": "2026-07-22T14:30:00Z",
    "configurationPath": "/app/server/appsettings.json"
  },
  "runtimeSettings": {
    "configurationPath": { "value": "/app/server/appsettings.json", "source": "default" },
    "transport": { "value": "stdio", "source": "env" },
    "logLevel": { "value": "Information", "source": "default" },
    "sessionMode": { "value": null, "source": "default" },
    "runtimeMode": { "value": "InProcess", "source": "default" },
    "mcpPath": { "value": null, "source": "default" }
  },
  "environmentVariables": {
    "POSHMCP_TRANSPORT": "stdio",
    "POSHMCP_CONFIGURATION": null,
    "POSHMCP_MCP_PATH": null,
    "POSHMCP_SESSION_MODE": null,
    "POSHMCP_RUNTIME_MODE": null,
    "POSHMCP_LOG_LEVEL": null,
    "POSHMCP_LOG_FILE": null,
    "ASPNETCORE_ENVIRONMENT": null
  },
  "powerShell": {
    "version": "7.4.6",
    "modulePathEntries": 3,
    "modulePaths": [
      "/usr/local/share/powershell/Modules",
      "/root/.local/share/powershell/Modules",
      "/opt/microsoft/powershell/7/Modules"
    ],
    "oopModulePathEntries": 0,
    "oopModulePaths": []
  },
  "functionsTools": {
    "configuredFunctionCount": 5,
    "configuredFunctionsFound": 5,
    "configuredFunctionsMissing": 0,
    "toolCount": 12,
    "toolNames": ["get_process_name", "get_process_id", "get_service_default"],
    "configuredFunctionStatus": [
      {
        "functionName": "Get-Process",
        "expectedToolName": "get_process",
        "found": true,
        "matchedToolNames": ["get_process_name", "get_process_id"],
        "resolutionReason": null
      }
    ]
  },
  "mcpDefinitions": {
    "resources": { "configured": 3, "valid": 3, "errors": [], "warnings": [] },
    "prompts": { "configured": 2, "valid": 2, "errors": [], "warnings": [] }
  },
  "warnings": [
    "FunctionNames is deprecated. Migrate to CommandNames in your appsettings.json."
  ]
}
```

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-040**: `poshmcp doctor` text output contains a Unicode box-drawing summary banner as the first three lines, with one of `✓ healthy`, `⚠ warnings`, or `✗ errors`
- **SC-041**: `poshmcp doctor` text output contains at least 5 section headers using the `── Name ───` format
- **SC-042**: `poshmcp doctor --format json` output parses to a JSON object with exactly the top-level keys: `summary`, `runtimeSettings`, `environmentVariables`, `powerShell`, `functionsTools`, `mcpDefinitions`, `warnings`
- **SC-043**: Every data point present in the current (pre-restructure) doctor output is still present in the restructured output — verified by diffing the information content
- **SC-044**: Adding a new section to doctor output requires creating at most 2 new files (a record in `DoctorReport.cs` and a render method in `DoctorTextRenderer.cs`) and modifying at most 2 existing methods (`RunDoctorAsync` and one line in the render pipeline)
- **SC-045**: All existing doctor-related tests in `PoshMcp.Tests/` continue to pass after the restructure
- **SC-046**: The `get-configuration-troubleshooting` MCP tool returns the new nested JSON structure

## Assumptions

- The existing `ConfiguredFunctionStatus` record is preserved and embedded within the new `FunctionsToolsSection`; it is not redesigned
- The `effectivePowerShellConfiguration` blob (the full serialized `PowerShellConfiguration`) is dropped from the top-level JSON output because it duplicates data already available via `get-configuration-guidance`; if consumers need it, it can be re-added as a subsection later
- Authentication and Logging sections are placeholders in this spec — the parallel work on issue #137 will populate their data; the section structure and JSON keys are defined here so both efforts converge
- The text output uses UTF-8 Unicode characters (box-drawing, ✓, ✗, ⚠) which are supported by all modern terminals; no ASCII fallback is provided
- JSON property naming follows camelCase convention consistent with the existing `BuildDoctorJson` output
- The `DoctorReport` record hierarchy uses C# `record` types for immutability and automatic `Equals`/`ToString`; no mutable classes
- The summary health status computation is deterministic: `errors` > `warnings` > `healthy`, with no configurable thresholds
