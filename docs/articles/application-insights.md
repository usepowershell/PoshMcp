---
uid: application-insights
title: Application Insights
---

# Application Insights

Monitor PoshMcp with Azure Application Insights for distributed tracing, metrics, and diagnostics.

## Overview

PoshMcp integrates with Azure Monitor OpenTelemetry to send telemetry to Application Insights. When enabled, existing tool execution spans are automatically exported — giving you end-to-end visibility into MCP tool calls, latency, and errors without code changes.

The integration is **disabled by default** — existing deployments are unaffected until you opt in.

## Configuration

Add or update the `ApplicationInsights` section in `appsettings.json`:

```json
{
  "ApplicationInsights": {
    "Enabled": true,
    "ConnectionString": "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://eastus-0.in.applicationinsights.azure.com/",
    "SamplingPercentage": 100
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | bool | `false` | Activates the Application Insights exporter |
| `ConnectionString` | string | `""` | Application Insights connection string |
| `SamplingPercentage` | int | `100` | Percentage of telemetry to send (1–100) |

## Environment Variable

The connection string can also be set via the `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable. If the config value is empty, the environment variable is used instead.

```bash
export APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=..."
```

This is the recommended approach for containerized deployments and CI/CD pipelines where secrets should not live in config files.

> **Precedence:** Environment variable is used when the `ConnectionString` in `appsettings.json` is empty. If both are set, the config file value wins.

## Sampling

Use `SamplingPercentage` to control telemetry volume and cost:

- `100` — Send all telemetry (default, good for development)
- `50` — Send half of all traces/metrics
- `10` — Send 10% (recommended for high-traffic production)

```json
{
  "ApplicationInsights": {
    "Enabled": true,
    "ConnectionString": "InstrumentationKey=...",
    "SamplingPercentage": 10
  }
}
```

Values must be between 1 and 100 inclusive.

## Doctor Validation

The `poshmcp doctor` command validates your Application Insights configuration:

```bash
poshmcp doctor
```

It checks for:

| Check | Severity | Condition |
|-------|----------|-----------|
| Missing connection string | Warning | `Enabled: true` but no connection string in config or environment |
| Invalid connection string format | Error | Value does not start with `InstrumentationKey=` and is not a valid ingestion endpoint URL |
| Invalid sampling percentage | Error | `SamplingPercentage` is outside the 1–100 range |

A missing connection string produces a warning (not an error) because the server still starts normally — it just won't export telemetry.

## Troubleshooting

**No telemetry appearing in Application Insights:**

1. Verify `Enabled` is `true`
2. Check the connection string — copy it directly from the Azure Portal under your Application Insights resource → Properties → Connection String
3. Ensure network connectivity to the ingestion endpoint
4. Check server logs for a startup warning about missing/invalid connection string

**"Invalid connection string" error from doctor:**

The connection string must start with `InstrumentationKey=` or be a full ingestion endpoint URL. Check for trailing whitespace or truncated values.

**Excessive telemetry volume / costs:**

Lower `SamplingPercentage` to reduce volume. Start at `10` for production workloads and increase if you need more coverage.

**Noisy Azure SDK logs:**

OpenTelemetry logs from the Azure Monitor SDK are suppressed by default — only traces and metrics flow to Application Insights. If you still see noise, check that you haven't overridden the log level for `Azure.*` categories.

## Graceful Degradation

If Application Insights is enabled but the connection string is missing or invalid, PoshMcp:

- Logs a warning at startup
- Starts normally — all MCP functionality works
- Simply does not export telemetry

This ensures a misconfiguration never prevents the server from running.
