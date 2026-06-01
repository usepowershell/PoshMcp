# Logging and Metrics in PoshMcp

This document describes the observability infrastructure built into PoshMcp: logging, OpenTelemetry metrics, distributed tracing, health checks, and diagnostic endpoints. Every claim here is grounded in the source code as of version 0.14.2.

---

## 1. Logging Infrastructure

### Framework

PoshMcp uses **Microsoft.Extensions.Logging** as the logging abstraction throughout the application. The concrete provider differs by transport mode:

| Transport | Provider | Sink |
|-----------|----------|------|
| HTTP | Microsoft console logger | `stderr` (all levels ≥ Trace routed to stderr via `LogToStandardErrorThreshold = LogLevel.Trace`) |
| Stdio | **No console provider** (cleared on startup) | File sink via Serilog (only when `--log-file` is supplied) |

The stdio transport deliberately clears all console providers (`builder.Logging.ClearProviders()`) to prevent log output from polluting the MCP JSON-RPC pipe on stdout/stderr. If you need log output from a stdio server, you **must** use the `--log-file` flag.

### Log Levels

Standard .NET log levels apply, in ascending severity:

| Level | Description |
|-------|-------------|
| `Trace` | Maximum verbosity — parameter-by-parameter detail |
| `Debug` | Pipeline stage milestones, parameter binding events |
| `Information` | Startup, configuration, tool invocation lifecycle |
| `Warning` | Recoverable errors, health check failures, auth denials |
| `Error` | Unexpected failures requiring attention |
| `Critical` (Fatal) | Not currently used in application code |
| `None` | Logging disabled for that category |

### Default Log Level Configuration

From `appsettings.json`:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
  }
}
```

`Microsoft.AspNetCore` is suppressed to `Warning` to avoid noise from the HTTP middleware layer.

### CLI Log Level Overrides

When running `poshmcp serve` (or the root command), these flags override the configured level:

| Flag | Effective Level |
|------|-----------------|
| *(none)* | `Information` (from config) |
| `--verbose` | `Debug` |
| `--debug` | `Debug` |
| `--trace` | `Trace` |
| `--log-level <level>` | Explicit level string passed to `poshmcp serve` |

The mapping is defined in `Program.cs` and `LoggingHelpers.MapToSerilogLevel()`.

### File Logging (Stdio Mode)

Pass `--log-file <path>` to `poshmcp serve --transport stdio` to enable a rolling file sink via **Serilog**:

- **Rolling interval:** Daily
- **Retained files:** 7
- **Output template:** `[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}`
- **Min level:** Matches the CLI/config level in effect at startup

The file logger is the only output available for stdio servers. Without `--log-file`, all log output is silently discarded.

Dependencies: `Serilog.Extensions.Logging`, `Serilog.Sinks.File`.

---

## 2. What Gets Logged

### Startup and Configuration

| Event | Level | Source |
|-------|-------|--------|
| `Using configuration source: {ConfigurationPath}` | Information | `StdioServerHost`, `HttpServerHost` |
| `RuntimeCachingState initialized and wired into PowerShellAssemblyGenerator` | Information | `McpToolSetupService` |
| `Added {N} total tools (dynamic reload tools are disabled)` | Information | `McpToolSetupService` |
| `Added {N} total tools (including 3 configuration reload tools)` | Information | `McpToolSetupService` |
| `Registered set-result-caching tool (always enabled)` | Information | `McpToolSetupService` |
| `[INFO] Application Insights enabled. Sampling: {N}%` | (Console.Error) | `StdioServerHost`, `HttpServerHost` |
| `[WARN] Application Insights is enabled but no connection string was found` | (Console.Error) | `StdioServerHost`, `HttpServerHost` |

### Assembly Generation (Tool Discovery)

| Event | Level | Source |
|-------|-------|--------|
| `Generating new in-memory assembly for PowerShell commands` | Information | `PowerShellAssemblyGenerator` |
| `Returning cached generated assembly` | Information | `PowerShellAssemblyGenerator` |
| `Successfully generated assembly with {N} command methods` | Information | `PowerShellAssemblyGenerator` |
| `Failed to generate method for command {CommandName}: {Message}` | Error | `PowerShellAssemblyGenerator` |
| `Skipping command {CommandName} — all parameter sets were skipped due to unserializable mandatory parameter types` | Warning | `PowerShellAssemblyGenerator` |
| `Generated utility methods for cached data operations` | Debug | `PowerShellAssemblyGenerator` |
| `Failed to generate utility methods: {Message}` | Error | `PowerShellAssemblyGenerator` |

All command names flowing through assembly generation are passed through `LogSanitizer.Scrub()` before being written to log (see [Log Security](#log-security)).

### Tool Invocation Lifecycle

Tool execution emits a rich sequence of structured log events. The key fields (`ToolName`, `InvocationId`, `ElapsedMs`) appear on every event so log entries can be correlated.

| Stage | Level | Message pattern |
|-------|-------|-----------------|
| Invocation received | Information | `Tool invocation received: ToolName={}, InvocationId={}, ParameterCount={}` |
| Stage: request_received | Debug | `Tool invocation stage: Stage=request_received, ...` |
| Stage: tool_resolved | Debug | `Tool invocation stage: Stage=tool_resolved, ParameterSummary={}` |
| Stage: pipeline_initialized | Debug | `Tool invocation stage: Stage=pipeline_initialized, ...` |
| Per-parameter detail | Debug | `Tool parameter detail: ..., Name={}, Type={}, Value={}` |
| Stage: parameters_bound_normalized | Debug | `Tool invocation stage: Stage=parameters_bound_normalized, ...` |
| Switch parameter bound | Debug | `Bound switch parameter: ..., ParameterName={}, IsPresent={}` |
| Non-switch parameter bound | Debug | `Bound parameter: ..., ParameterName={}, ValueType={}, Value={}` |
| Property selection decision | Information | `Result shaping decision: ..., ApplyPropertySelection={}, SelectedPropertyCount={}, MaxResults={}` |
| Caching decision | Information | `Result caching decision: ToolName={}, EnableCaching={}` |
| Pipeline starting | Information | `PowerShell pipeline starting: ToolName={}, InvocationId={}, ElapsedMs={}` |
| Pipeline completed | Information | `PowerShell pipeline completed: ..., ResultCount={}, ElapsedMs={}` |
| Stage: result_shaping_started | Debug | `Tool invocation stage: Stage=result_shaping_started, ..., ResultCount={}` |
| Stage: result_shaping_completed | Debug | `Tool invocation stage: Stage=result_shaping_completed, ..., JsonLength={}` |
| Stage: result_shaping_empty | Debug | `Tool invocation stage: Stage=result_shaping_empty, ...` |
| Invocation completed (success) | Information | `Tool invocation completed: ToolName={}, Status=success, ElapsedMs={}` |
| Invocation with handled error | Warning | `Tool invocation completed with handled error response: ..., ErrorType={}` |
| Invocation failed | Error | `Tool invocation failed: ..., Status=error, ErrorType=critical_error` |
| Cancellation | Warning | `Tool invocation cancelled: Stage=pipeline_execution, ...` |
| Timeout | Warning | `Tool invocation timed out: Stage=pipeline_execution, ...` |

#### Error Types

The `errorType` field on error/warning log events can be one of:

| Value | Meaning |
|-------|---------|
| `command_not_found` | PowerShell command not found at runtime |
| `execution_failed` | Pipeline threw an exception |
| `powershell_errors` | Pipeline completed but `$Error` stream was non-empty |
| `json_serialization_failed` | Result serialization to JSON failed |
| `operation_cancelled` | CancellationToken was signalled |
| `timeout` | TimeoutException from runspace |
| `unexpected_error` | Unhandled exception inside the thread-safe callback |
| `critical_error` | Exception that escaped the outer try/catch |

### PowerShell Runspace Lifecycle

| Event | Level | Source |
|-------|-------|--------|
| `PowerShell cleanup service started` | Information | `PowerShellCleanupService` |
| `Cleaning up PowerShell runspace` | Information | `PowerShellCleanupService` |
| `PowerShell runspace disposed successfully` | Information | `PowerShellCleanupService` |
| `Error disposing PowerShell runspace` | Warning | `PowerShellCleanupService` |

### Authentication and Authorization

These events are only emitted when authentication is enabled (`Authentication.Enabled: true`):

| Event | Level | Source |
|-------|-------|--------|
| `Auth skipped for tool {ToolName} (AllowAnonymous)` | Debug | `ToolAuthorizationFilter` |
| `Authorization denied for tool {ToolName}: unauthenticated` | Warning | `ToolAuthorizationFilter` |
| `Authorization denied for tool {ToolName}: insufficient_scope` | Warning | `ToolAuthorizationFilter` |
| `Authorization denied for tool {ToolName}: insufficient_role` | Warning | `ToolAuthorizationFilter` |

### Health Checks

| Event | Level | Source |
|-------|-------|--------|
| `PowerShell runspace health check passed` | Debug | `PowerShellRunspaceHealthCheck` |
| `PowerShell runspace health check failed: {Reason}` | Warning | `PowerShellRunspaceHealthCheck` |
| `PowerShell runspace health check cancelled or timed out` | Warning | `PowerShellRunspaceHealthCheck` |
| `PowerShell runspace health check exceeded {N}ms timeout` | Warning | `PowerShellRunspaceHealthCheck` |
| `PowerShell runspace health check threw exception` | Error | `PowerShellRunspaceHealthCheck` |
| `Assembly generation subsystem health check passed` | Debug | `AssemblyGenerationHealthCheck` |
| `Assembly generator creation returned null` | Warning | `AssemblyGenerationHealthCheck` |
| `Assembly generation health check: Cannot introspect PowerShell commands` | Warning | `AssemblyGenerationHealthCheck` |
| `Assembly generation health check failed` | Error | `AssemblyGenerationHealthCheck` |
| `Configuration health check passed` | Debug | `ConfigurationHealthCheck` |
| `Configuration is valid but has no functions, modules, or include patterns defined` | Warning | `ConfigurationHealthCheck` |
| `PowerShell configuration is null` | Error | `ConfigurationHealthCheck` |
| `Configuration health check failed` | Error | `ConfigurationHealthCheck` |

### Configuration Guidance Tool

| Event | Level | Source |
|-------|-------|--------|
| `Processing configuration guidance request` | Information | `ConfigurationGuidanceTools` |

### Log Security

All user-controlled values (tool names, parameter names, parameter values, correlation IDs received from callers) are passed through `LogSanitizer.Scrub()` before being written to any log statement. This mitigates [CWE-117 (Log Forging)](https://cwe.mitre.org/data/definitions/117.html):

- CR (`\r`), LF (`\n`), TAB (`\t`), and other ASCII control characters are replaced with visible escape sequences (`\\r`, `\\n`, `\\t`, `\\xNN`).
- Inputs longer than **2048 characters** are truncated with `…(truncated)`.
- `null` inputs are replaced with the literal string `<null>`.

---

## 3. Correlation IDs and Operation Context

PoshMcp tracks a **correlation ID** and an **operation name** across each tool invocation using `AsyncLocal<T>` so the values flow through async continuations without thread-affinity concerns.

### OperationContext

`PoshMcp.Server.Observability.OperationContext` exposes two `AsyncLocal` slots:

| Property | Format | Example |
|----------|--------|---------|
| `CorrelationId` | `yyyyMMdd-HHmmss-xxxxxxxx` (timestamp + 8-char GUID fragment) | `20260518-074426-a1b2c3d4` |
| `OperationName` | The PowerShell command name for the current invocation | `Get-Process` |

Use `OperationContext.BeginOperation(name)` to create a scoped context that restores the previous values on `Dispose`.

In HTTP mode, the correlation ID is also threaded through an HTTP middleware layer:
- **Incoming:** read from `X-Correlation-ID` request header (or a new ID is generated)
- **Outgoing:** echoed back in `X-Correlation-ID` response header

### LoggerExtensions

`PoshMcp.Server.Observability.LoggerExtensions` provides extension methods that automatically inject the correlation context into every log statement:

```csharp
// Recommended for methods with multiple log statements (one scope, lower overhead)
using (logger.BeginCorrelationScope())
{
    logger.LogInformation("step 1");
    logger.LogInformation("step 2");
}

// Convenience wrappers (one scope per call — use for single isolated statements)
logger.LogInformationWithCorrelation("Something happened");
logger.LogWarningWithCorrelation("Something concerning");
logger.LogErrorWithCorrelation(exception, "Something broke");
logger.LogDebugWithCorrelation("Detail");
logger.LogTraceWithCorrelation("Very detailed info");
```

The scope injects two structured log properties: `CorrelationId` and `OperationName`.

---

## 4. OpenTelemetry Metrics

### Meter Registration

All PoshMcp metrics are published under a single `System.Diagnostics.Metrics.Meter`:

- **Meter name:** `PoshMcp`
- **Meter version:** `1.0.0`
- **Class:** `PoshMcp.Server.Metrics.McpMetrics`

The meter is registered as a singleton service and wired into `McpToolFactoryV2` and `PowerShellAssemblyGenerator` via `SetMetrics()` at startup. Instrumentation follows a strict charter: **metrics must never crash the application** — all recording calls are wrapped in try/catch.

### Active Metrics (Currently Recorded)

These metrics are recorded in production code paths:

#### Tool Execution

| Metric name | Type | Description | Tags |
|-------------|------|-------------|------|
| `mcp_tool_invocation_total` | Counter\<long\> | Count of tool execution attempts, both on start and on completion | `tool_name`, `status` (`started`\|`success`\|`error`\|`cancelled`\|`timeout`), `correlation_id` |
| `mcp_tool_execution_duration_seconds` | Histogram\<double\> | Elapsed time in seconds for each tool invocation | `tool_name`, `correlation_id` |
| `mcp_tool_execution_errors_total` | Counter\<long\> | Count of failed tool executions | `tool_name`, `error_type`, `correlation_id` |
| `mcp_tool_usage_total` | Counter\<long\> | Count of tool invocations (recorded at completion regardless of outcome) | `tool_name`, `correlation_id` |

**Note:** `mcp_tool_invocation_total` is recorded **twice** per invocation: once at the start (status=`started`) and once at the end (status=outcome). Design consumers accordingly.

#### Authentication

| Metric name | Type | Description | Tags |
|-------------|------|-------------|------|
| `poshmcp.auth.tool_denials` | Counter\<long\> | Tool call denials due to authorization failure | `tool_name`, `reason` (`unauthenticated`\|`insufficient_scope`\|`insufficient_role`) |

`poshmcp.auth.attempts` is **defined** but not yet recorded in production code paths.

#### Description Source Resolution (Spec 010 FR-590)

| Metric name | Type | Description | Tags |
|-------------|------|-------------|------|
| `poshmcp.tool_description.source` | Counter\<long\> | Resolution count per tool description precedence step | `step` |
| `poshmcp.parameter_description.source` | Counter\<long\> | Resolution count per parameter description precedence step | `step` |

The `step` tag value is the FR-583 wire vocabulary string from `DescriptionSourceVocabulary.ToWireValue()`:

- **Tool descriptions:** `synopsis` → `description` → `syntax` → `name`
- **Parameter descriptions:** `helpParameter` → `helpMessage` → `validateSet` → `typeFallback`

These counters are also surfaced in `poshmcp doctor` output as `descriptionSource` fields.

### Defined but Not Yet Recorded

The following metrics are **defined** in `McpMetrics` and will be activated by future features. They are reserved names — do not use them in external tooling for purposes other than their described intent:

| Metric name | Intended use |
|-------------|--------------|
| `poshmcp.auth.attempts` | Total authentication attempts |
| `mcp_tool_registration_total` | Tools registered (labeled by source) |
| `mcp_tool_update_total` | Tool definition updates |
| `mcp_tool_deprecation_total` | Tools deprecated or removed |
| `mcp_intent_resolution_success_total` | Future AI-to-tool intent mapping successes |
| `mcp_intent_resolution_failure_total` | Future AI-to-tool intent mapping failures |
| `mcp_intent_resolution_latency_seconds` | Future AI-to-tool intent mapping latency |
| `mcp_tool_usage_by_agent_total` | Invocations by AI agent vs. human |
| `mcp_prompt_success_rate` | Ratio of successful AI-generated prompt invocations |
| `mcp_prompt_retry_total` | Prompt retries due to failures |
| `mcp_prompt_parameter_completion_rate` | Auto-fill rate for prompt parameters |
| `mcp_agent_invocation_total` | Tool invocations initiated by AI agents |
| `mcp_agent_tool_diversity` | Unique tools used per agent over time |

---

## 5. Distributed Tracing

PoshMcp emits OpenTelemetry traces using a `System.Diagnostics.ActivitySource`:

- **Source name:** `PoshMcp.Tools`
- **Source version:** `1.0.0`

For each tool invocation, an activity named `tool.invoke` (kind `Internal`) is started with the following tags:

| Tag | Value |
|-----|-------|
| `tool.name` | Sanitized tool name |
| `tool.parameter_names` | Comma-separated list of parameter names (not values, FR-310 compliance) |

The trace source is registered with the OpenTelemetry SDK via `.AddSource(PowerShellAssemblyGenerator.ToolActivitySource.Name)` in both `StdioServerHost` and `HttpServerHost`.

---

## 6. Metrics and Trace Exporters

### HTTP Transport

The HTTP server configures:
- `AddMeter(McpMetrics.MeterName)` — PoshMcp custom meter
- `AddAspNetCoreInstrumentation()` — standard ASP.NET Core HTTP metrics
- `AddConsoleExporter()` — outputs metrics to `stdout` on a periodic interval
- `AddSource(PowerShellAssemblyGenerator.ToolActivitySource.Name)` — distributed traces

### Stdio Transport

The stdio server configures:
- `AddMeter(McpMetrics.MeterName)` — PoshMcp custom meter
- The **console exporter is disabled** in stdio mode (it would corrupt the MCP pipe)
- `AddSource(PowerShellAssemblyGenerator.ToolActivitySource.Name)` — distributed traces

### Azure Monitor / Application Insights

Optional, zero-overhead when disabled (default). Enabled via `ApplicationInsights.Enabled: true` in `appsettings.json`:

```json
"ApplicationInsights": {
  "Enabled": false,
  "ConnectionString": "",
  "SamplingPercentage": 100
}
```

When enabled:
- Uses `Azure.Monitor.OpenTelemetry.AspNetCore` (`UseAzureMonitor()`)
- Exports **traces and metrics only** — ILogger output is **explicitly suppressed** from App Insights export (FR-311/FR-312) via a `LogLevel.None` filter on the `OpenTelemetry` logger provider
- A `transport.mode` resource attribute (`stdio` or `http`) is attached to all telemetry
- `SamplingPercentage` is clamped to 1–100

Connection string priority:
1. `ApplicationInsights.ConnectionString` in config
2. `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable
3. Warning printed to stderr and App Insights silently disabled

---

## 7. Health Checks

Health checks are available in **HTTP transport mode only**. They are not available when running as a stdio server.

### Endpoints

| Endpoint | Purpose | Auth |
|----------|---------|------|
| `GET /health` | Aggregated health report (JSON). Returns HTTP 200 for Healthy, HTTP 503 for Degraded or Unhealthy; check `status` field. | `AllowAnonymous` |
| `GET /health/ready` | Readiness probe. Returns HTTP 200 on `Healthy`, HTTP 503 on `Degraded` or `Unhealthy`. | `AllowAnonymous` |

Both endpoints return JSON in the following shape:

```json
{
  "status": "Healthy",
  "checks": [
    {
      "name": "powershell_runspace",
      "status": "Healthy",
      "description": "PowerShell runspace responsive",
      "duration": 12.3,
      "data": {}
    },
    ...
  ],
  "totalDuration": 45.6
}
```

### Registered Checks

#### `powershell_runspace` — `PowerShellRunspaceHealthCheck`

Executes the PowerShell expression `1 + 1` in the active runspace with a **500ms timeout**.

| Outcome | Status | Cause |
|---------|--------|-------|
| Expression returns a result | `Healthy` | — |
| Expression returns no results or has errors | `Unhealthy` | Runspace is available but not behaving correctly |
| Timed out (> 500ms) | `Unhealthy` | Runspace is blocked or degraded |
| Exception | `Unhealthy` | Runspace is broken |

#### `assembly_generation` — `AssemblyGenerationHealthCheck`

Verifies PowerShell introspection capability by executing `Get-Command -Name Get-Date`.

| Outcome | Status | Cause |
|---------|--------|-------|
| Command info returned | `Healthy` | — |
| No results returned | `Degraded` | PowerShell available but introspection failing |
| Generator creation fails | `Degraded` | Internal error in assembly generator setup |
| Exception | `Unhealthy` | Hard failure |

#### `configuration` — `ConfigurationHealthCheck`

Validates the loaded `PowerShellConfiguration` and reports counts.

| Outcome | Status | Cause |
|---------|--------|-------|
| Config valid with ≥1 command/module/pattern | `Healthy` | Returns `FunctionCount`, `ModuleCount`, `IncludePatternCount`, `ExcludePatternCount`, `AuthEnabled`, `AuthSchemes`, `ToolsWithAuthOverrides` in the `data` dictionary |
| Config valid but no functions/modules/patterns | `Degraded` | Server would expose zero tools |
| Config is null | `Unhealthy` | Configuration binding failed |
| Exception | `Unhealthy` | Hard failure |

### Docker Health Check

The base `Dockerfile` includes a Kubernetes-compatible `HEALTHCHECK` instruction:

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
```

This polls `/health` every 30 seconds with a 3-second timeout, allowing 10 seconds for initial startup and 3 retries before marking the container unhealthy.

---

## 8. Diagnostic Endpoints and Tools

### `poshmcp doctor` CLI Command

The `doctor` command produces a structured diagnostic snapshot. It does **not** require a running server — it runs a one-shot startup, discovers tools, and exits.

```bash
poshmcp doctor                   # human-readable text output
poshmcp doctor --format json     # machine-readable JSON
poshmcp doctor --config ./my-config.json
```

The `DoctorReport` JSON object contains:

| Section | Contents |
|---------|----------|
| `summary` | Overall health: config errors count, tool count, warnings count |
| `runtimeSettings` | Resolved transport, log level, runtime mode, config path, and their sources |
| `environmentVariables` | Relevant env var values at diagnostic time |
| `powerShell` | PowerShell version, module paths, startup script status |
| `functionsTools` | Configured function list, discovered tool list with import source and disposition (`exposed`\|`filteredOut`\|`discoveryFailed`), description source per tool |
| `mcpDefinitions` | Configured MCP resources and prompts |
| `authentication` | Auth enabled/disabled, schemes, default policy, per-tool overrides |
| `identity` | Current caller identity (when running with auth) |
| `configurationErrors` | List of error strings collected during diagnostic |
| `warnings` | List of warning strings |
| `outOfProcess` | OOP executor diagnostics (only meaningful when `RuntimeMode=OutOfProcess`) |

### MCP Diagnostic Tools (HTTP Transport)

Two special MCP tools provide runtime diagnostics to AI clients:

| Tool (MCP name) | Available when | Returns |
|-----------------|----------------|---------|
| `get-configuration-guidance` | Registered when `EnableConfigurationTroubleshootingTool` is true | Current config settings, effective transport, recommended next steps |
| `get-configuration-troubleshooting` | `EnableConfigurationTroubleshootingTool: true` in config | Similar to `poshmcp doctor`, exposes the DoctorReport to the MCP client |

Both tools are registered by `McpToolSetupService` alongside the user's PowerShell tools.

### `set-result-caching` Tool

Always registered (not gated by `EnableDynamicReloadTools`). Allows the AI client to toggle result caching on or off at runtime without restarting the server. Changes take effect on the next invocation.

---

## 9. Configuration Reference

### Logging Section (`appsettings.json`)

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
  },
  "File": {
    "Path": ""
  }
}
```

> **Note:** The `Logging.File.Path` key is present in `appsettings.json` but the file sink is **not** activated by this key. File logging requires the `--log-file <path>` CLI flag passed to `poshmcp serve --transport stdio`.

### Application Insights Section

```json
"ApplicationInsights": {
  "Enabled": false,
  "ConnectionString": "",
  "SamplingPercentage": 100
}
```

| Field | Default | Notes |
|-------|---------|-------|
| `Enabled` | `false` | Set to `true` to activate App Insights export |
| `ConnectionString` | `""` | Falls back to `APPLICATIONINSIGHTS_CONNECTION_STRING` env var |
| `SamplingPercentage` | `100` | 1–100; clamped at runtime |

### CLI Flags for Logging

| Flag | Command | Effect |
|------|---------|--------|
| `--verbose` | root, serve | Sets level to Debug |
| `--debug` | root, serve | Sets level to Debug |
| `--trace` | root, serve | Sets level to Trace |
| `--log-level <level>` | serve | Explicit level (Trace/Debug/Information/Warning/Error/Critical/None) |
| `--log-file <path>` | serve (stdio only) | Enables Serilog file sink at the given path |

---

## 10. Docker and Container Logging

### Default Behavior

The base container image (`poshmcp:latest`) runs in HTTP transport mode by default:

```dockerfile
ENV POSHMCP_TRANSPORT=http
ENV ASPNETCORE_ENVIRONMENT=Production
```

In HTTP mode, the console logger is active. All log output goes to **stdout/stderr**, which Docker captures and makes available via `docker logs` or equivalent container runtime commands.

### Transport Mode and Logging

| `POSHMCP_TRANSPORT` | Console logging | File logging |
|---------------------|-----------------|--------------|
| `http` (default) | Active, to stderr | Not available |
| `stdio` | **Disabled** | Only if `--log-file` is passed via the entrypoint |

The `docker-entrypoint.sh` passes `--transport "$POSHMCP_TRANSPORT"` to `poshmcp serve` but does not pass `--log-file` by default. To enable file logging in a container, override the entrypoint or use a custom `docker-compose.yml` command:

```yaml
command: ["dotnet", "/app/server/PoshMcp.dll", "serve", "--transport", "stdio", "--log-file", "/var/log/poshmcp/server.log"]
```

### Log Level in Containers

Set the log level via environment variable (using .NET configuration environment variable convention):

```bash
docker run -e Logging__LogLevel__Default=Debug poshmcp:latest
```

Or override via a custom `appsettings.json` mounted at `/app/server/appsettings.json`.

### Health Check in Containers

The Docker `HEALTHCHECK` polls `/health` every 30 seconds. Kubernetes operators can use the same endpoint for liveness and readiness probes:

```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 30

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 10
```

### Application Insights in Containers

Pass the connection string via environment variable — no config file change needed:

```bash
docker run \
  -e ApplicationInsights__Enabled=true \
  -e APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=...;IngestionEndpoint=..." \
  poshmcp:latest
```

---

## 11. Implementation Notes for Contributors

- **Never crash on metrics:** All `McpMetrics` recording calls are wrapped in try/catch. This is a design charter requirement.
- **Scrub before logging:** Any user-controlled value (tool name, parameter name, parameter value, external IDs) must go through `LogSanitizer.Scrub()` at the call site.
- **Use `BeginCorrelationScope()` for multi-statement methods:** The per-call convenience wrappers (`LogInformationWithCorrelation`, etc.) create one scope per call and carry performance overhead on hot paths.
- **Health checks are HTTP only:** The three health check implementations are only registered in `HttpServerHost.RegisterHealthChecks()`. They are not available in stdio mode.
- **Stdio console logging is intentionally absent:** Do not re-add a console logger to the stdio host. The MCP JSON-RPC pipe uses stdout/stderr and any log output there will corrupt the protocol stream.
