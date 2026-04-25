# Feature Specification: Optional Application Insights Logging

**Spec Number**: 008
**Feature Branch**: `008-application-insights-logging`
**Created**: 2026-04-25
**Status**: Decided
**Input**: Optional Application Insights telemetry sink, configurable via appsettings, using Azure.Monitor.OpenTelemetry.AspNetCore

---

## User Scenarios & Testing

### Scenario 1 (P1): Azure-Hosted Operator Enables Application Insights

**Why this priority**: The primary motivation for this feature is enabling observability for PoshMcp instances running in Azure. This is the core use case.

**Independent Test**: Start HTTP server with `ApplicationInsights.Enabled: true` and a valid connection string; invoke a tool; verify no startup errors and the Azure Monitor exporter was wired.

**Acceptance Scenarios**:

- SC-100: Given `Enabled: true` and `ConnectionString` set in appsettings — App Insights exporter is wired at startup
- SC-101: Given `Enabled: true` and connection string provided only via `APPLICATIONINSIGHTS_CONNECTION_STRING` env var — env var used as fallback, exporter wired
- SC-102: Given `Enabled: true` but no connection string from any source — warning logged to stderr, startup continues, no crash, App Insights not wired
- SC-103: Given `Enabled: false` (default) — no App Insights code loaded, zero overhead

### Scenario 2 (P2): Local Developer Keeps App Insights Disabled

**Why this priority**: The default config must not require Azure credentials. Existing local dev workflows must be fully unaffected.

**Independent Test**: Start server with default config (no `ApplicationInsights` section or `Enabled: false`); verify OTel console output still works and no App Insights errors appear.

**Acceptance Scenarios**:

- SC-104: Given default appsettings (no `ApplicationInsights` section) — server behaves identically to pre-feature behavior
- SC-105: Given `Enabled: false` explicitly — existing Serilog and OTel pipelines unaffected
- SC-106: Given `Enabled: false` — zero performance overhead (no SDK wiring, no sampling, no exports)

### Scenario 3 (P3): Operator Validates Config with `poshmcp doctor`

**Why this priority**: Operators need to catch misconfiguration before deploying, without making live network calls.

**Independent Test**: Run `poshmcp doctor` with `Enabled: true` and various connection string values; verify format validation output is correct for each case.

**Acceptance Scenarios**:

- SC-107: Given `Enabled: true` with valid connection string format — doctor reports OK
- SC-108: Given `Enabled: true` with empty or malformed connection string — doctor reports config error
- SC-109: Given `SamplingPercentage` outside 1–100 — doctor reports validation warning

---

## Edge Cases

- `Enabled: true` but connection string missing → warn and continue (no crash, no App Insights)
- Connection string format invalid (not starting with `InstrumentationKey=` or `https://`) → doctor flags it; runtime proceeds with warning
- `SamplingPercentage: 0` or `> 100` → doctor warns; runtime clamps to valid range
- Existing OTel metrics (`McpMetrics`) automatically flow through Azure Monitor exporter when enabled
- Both stdio mode and HTTP mode are supported; transport mode added as a custom dimension

---

## Requirements

### Functional Requirements

- **FR-300**: System MUST accept `ApplicationInsights.Enabled` bool configuration (default: `false`)
- **FR-301**: System MUST accept `ApplicationInsights.ConnectionString` string configuration (default: empty)
- **FR-302**: System MUST accept `ApplicationInsights.SamplingPercentage` int configuration (default: `100`)
- **FR-303**: When `Enabled: false`, NO App Insights code or packages MUST be loaded at runtime
- **FR-304**: When `Enabled: true` and `ConnectionString` is empty, system MUST fall back to `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable
- **FR-305**: When `Enabled: true` and no connection string is available from any source, system MUST log a warning and continue without crashing
- **FR-306**: Package used MUST be `Azure.Monitor.OpenTelemetry.AspNetCore` (NOT legacy `Microsoft.ApplicationInsights.AspNetCore`)
- **FR-307**: Implementation MUST add a `ConfigureApplicationInsights(IServiceCollection, IConfiguration, bool isStdioMode)` method in `Program.cs`
- **FR-308**: Method MUST call `services.AddOpenTelemetry().UseAzureMonitor(...)` when enabled with a valid connection string
- **FR-309**: Transport mode (`stdio` or `http`) MUST be added as a custom dimension on all telemetry
- **FR-310**: Tool parameter NAMES MUST be included in custom properties
- **FR-311**: Tool parameter VALUES MUST NOT be sent to App Insights
- **FR-312**: PowerShell command output MUST NOT be sent to App Insights
- **FR-313**: `poshmcp doctor` MUST validate connection string format when `Enabled: true`
- **FR-314**: `poshmcp doctor` MUST validate `SamplingPercentage` is between 1–100 when `Enabled: true`
- **FR-315**: Doctor MUST NOT make live network calls to validate App Insights configuration
- **FR-316**: Existing Serilog file logging MUST continue unchanged
- **FR-317**: Existing OTel metrics (`McpMetrics`) MUST flow through Azure Monitor exporter when App Insights is enabled
- **FR-318**: `appsettings.json` MUST include an `ApplicationInsights` section with `Enabled: false` as the default

---

## Parameter Design

### appsettings.json Section

```json
{
  "ApplicationInsights": {
    "Enabled": false,
    "ConnectionString": "",
    "SamplingPercentage": 100
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | `bool` | `false` | Master switch. When `false`, no App Insights SDK is wired. |
| `ConnectionString` | `string` | `""` | App Insights connection string. Falls back to env var if empty. |
| `SamplingPercentage` | `int` | `100` | Percentage of telemetry to sample (1–100). |

### Environment Variable Override

```
APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=xxx;IngestionEndpoint=https://...
```

Resolution chain (highest to lowest priority):
1. `appsettings.json` → `ApplicationInsights.ConnectionString`
2. Environment variable → `APPLICATIONINSIGHTS_CONNECTION_STRING`
3. Empty / unset → App Insights disabled with warning

---

## Execution Flow

### Startup Sequence

1. `Program.cs` reads `ApplicationInsights` config section
2. If `Enabled: false` → return immediately, no SDK wiring
3. If `Enabled: true` → resolve connection string (config → env var)
4. If no connection string → log warning, return without wiring
5. Call `services.AddOpenTelemetry().UseAzureMonitor(options => { ... })`
6. Set `SamplingRatio` from `SamplingPercentage / 100.0f`
7. Add transport mode custom dimension

### Method Signature

```csharp
private static void ConfigureApplicationInsights(
    IServiceCollection services,
    IConfiguration configuration,
    bool isStdioMode)
```

### What Gets Sent vs. Suppressed

| Telemetry Type | Status | Notes |
|----------------|--------|-------|
| ILogger structured logs | ✅ Sent | Respects configured LogLevel |
| OTel traces/spans | ✅ Sent | HTTP request traces, custom spans |
| McpMetrics (OTel metrics) | ✅ Sent | Tool invocations, duration, errors |
| Custom properties: parameter names | ✅ Sent | Safe schema metadata |
| Custom properties: transport mode | ✅ Sent | `stdio` or `http` |
| Tool parameter values | ❌ Not sent | May contain secrets |
| PowerShell command output | ❌ Not sent | Contains user data |
| Full exception stack traces from user scripts | ❌ Not sent | User script errors stay in logs |
