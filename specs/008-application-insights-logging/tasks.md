# Test Tasks: Optional Application Insights Logging (Spec 008)

**Spec**: `specs/008-application-insights-logging/spec.md`
**Written by**: Fry (Tester)
**Date**: 2026-04-25

---

## Prerequisites

- .NET 10 SDK installed
- PoshMcp.Server builds successfully
- `poshmcp` CLI installed globally
- (Optional) Azure subscription with Application Insights resource for manual validation tests

---

## Section 1: Acceptance Criteria Checklist

| ID | Scenario | Acceptance Criterion |
|----|----------|----------------------|
| SC-100 | App Insights enabled, connection string in appsettings | Azure Monitor exporter wired at startup; no startup errors |
| SC-101 | App Insights enabled, connection string in env var only | Env var used as fallback; exporter wired |
| SC-102 | App Insights enabled, no connection string | Warning logged to stderr; startup continues; no crash |
| SC-103 | App Insights disabled (default) | No App Insights code loaded; no SDK overhead |
| SC-104 | Default appsettings (no `ApplicationInsights` section) | Server behaves identically to pre-feature |
| SC-105 | `Enabled: false` explicitly set | Serilog and OTel console pipelines unaffected |
| SC-106 | `Enabled: false` performance | Zero overhead — no SDK wiring, no sampling |
| SC-107 | Doctor — valid connection string format | `poshmcp doctor` reports OK |
| SC-108 | Doctor — missing or malformed connection string | `poshmcp doctor` reports config error |
| SC-109 | Doctor — `SamplingPercentage` out of range | `poshmcp doctor` reports validation warning |

---

## Section 2: Configuration Validation Tests

**CV-01** — Enabled=false (default): Start server without any `ApplicationInsights` config. Verify startup succeeds and no App Insights-related log entries appear.

**CV-02** — Enabled=true, connection string in appsettings: Set `ApplicationInsights.Enabled: true` and a syntactically valid connection string. Start server. Verify Azure Monitor exporter is registered (check startup logs for OTel exporter wiring).

**CV-03** — Enabled=true, connection string via env var: Set `Enabled: true` with empty `ConnectionString` in appsettings, but set `APPLICATIONINSIGHTS_CONNECTION_STRING` env var. Verify exporter is wired using the env var value.

**CV-04** — Enabled=true, no connection string: Set `Enabled: true`, leave `ConnectionString` empty, unset env var. Verify a `[WARN]` message appears on stderr and startup completes without error.

**CV-05** — SamplingPercentage boundaries: Test `SamplingPercentage: 1`, `SamplingPercentage: 100`, and `SamplingPercentage: 50`. Verify each starts without error. Test `SamplingPercentage: 0` and `SamplingPercentage: 101` — verify doctor reports validation warning.

---

## Section 3: Doctor Integration Tests

**DI-01** — Valid format check: Run `poshmcp doctor` with `Enabled: true` and a connection string starting with `InstrumentationKey=`. Verify doctor reports the section as valid.

**DI-02** — Invalid format check: Run `poshmcp doctor` with `Enabled: true` and a connection string that does NOT start with `InstrumentationKey=` or a valid HTTPS URL. Verify doctor reports a format error.

**DI-03** — No network calls: Run `poshmcp doctor` in an environment with no network access. Verify it completes without timeout or network error — validation is format-only.

**DI-04** — Disabled skip: Run `poshmcp doctor` with `Enabled: false`. Verify doctor does NOT validate connection string format (no false positives when disabled).

---

## Section 4: Telemetry Isolation Tests

**TI-01** — Serilog unaffected: Start server with App Insights enabled. Verify file logging output matches pre-feature output format — no duplicate log entries, no missing entries.

**TI-02** — OTel console unaffected: In HTTP mode with App Insights enabled, verify OTel console exporter still produces output. In stdio mode, verify console output is suppressed (existing behavior unchanged).

**TI-03** — Parameter values not sent: Invoke a tool that accepts parameters. Verify (via App Insights Live Metrics or debug logging) that parameter values are NOT included in custom properties. Verify parameter names ARE included.

**TI-04** — Transport mode dimension: Invoke tools in both stdio and HTTP modes. Verify custom dimension `transport` is set to `"stdio"` and `"http"` respectively.

---

## Section 5: Azure Integration Tests (Manual)

These tests require a real Azure subscription and Application Insights resource.

**AI-01** — End-to-end telemetry: Deploy PoshMcp to Azure App Service with `APPLICATIONINSIGHTS_CONNECTION_STRING` set. Invoke several MCP tools. After ~2 minutes, verify traces and metrics appear in Application Insights → Live Metrics and Search.

**AI-02** — Sampling verification: Set `SamplingPercentage: 50`. Run 100 tool invocations. Verify approximately 50% appear in App Insights (within ±15% variance). Verify all 100 invocations appear in local Serilog logs (sampling applies only to App Insights export).
