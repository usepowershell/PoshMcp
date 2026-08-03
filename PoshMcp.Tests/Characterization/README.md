# v1 Baseline Characterization — Artifact Format

## Purpose

`v1-baseline-characterization.json` is the Phase 0 baseline artifact produced by
`V1BaselineCharacterizationTests`. It records SDK 1.4.1 performance characteristics
so Phase 4 (post-v2 upgrade) can compare relative regressions and improvements.

**No thresholds are enforced.** Tests pass if the server starts and responds
correctly. Timing values are observational.

## Schema Version

`poshmcp/v1-characterization/1.0`

## Top-Level Structure

```json
{
  "schemaVersion": "poshmcp/v1-characterization/1.0",
  "capturedAt": "2026-08-03T17:00:00.000Z",
  "sdkPackageVersion": "ModelContextProtocol 1.4.1",
  "runtimeInfo": { ... },
  "scenarios": [ ... ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `schemaVersion` | string | Always `poshmcp/v1-characterization/1.0`. Increment minor on additive changes, major on breaking schema changes. |
| `capturedAt` | ISO-8601 | UTC timestamp of the characterization run. |
| `sdkPackageVersion` | string | NuGet package and version string. Use exact package name for Phase 4 diff. |
| `runtimeInfo` | object | Host environment at run time. |
| `scenarios` | array | Ordered alphabetically by `scenario`. One entry per scenario class (see below). |

## `runtimeInfo` Object

```json
{
  "dotNetVersion": "10.0.8",
  "os": "Unix 6.8.0.0",
  "logicalProcessors": 4,
  "machineName": "runner-abc123"
}
```

CI runner hardware varies run to run. Use `logicalProcessors` to flag anomalous
results (e.g., single-core VMs produce unrepresentative concurrent-throughput
numbers).

## `scenarios` Array Entry

```json
{
  "scenario": "cold_start_http_with_script",
  "description": "...",
  "unit": "milliseconds",
  "iterations": 5,
  "stats": {
    "mean": 4823.1,
    "p50": 4810.0,
    "p95": 5012.5,
    "p99": 5043.2,
    "min": 4701.0,
    "max": 5060.0,
    "stdDev": 112.4,
    "sampleCount": 5
  },
  "rawSamples": [4701.0, 4790.0, 4810.0, 4890.0, 5060.0]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `scenario` | string | Stable identifier (snake_case). Used as join key in Phase 4 comparison. |
| `description` | string | Human-readable description of what the scenario measures. |
| `unit` | string | `milliseconds` or `megabytes`. |
| `iterations` | int | Number of measurement samples (warmup excluded). |
| `stats.mean` | float | Arithmetic mean of `rawSamples`. |
| `stats.p50` | float | 50th percentile (linear interpolation). |
| `stats.p95` | float | 95th percentile. |
| `stats.p99` | float | 99th percentile. |
| `stats.min` / `max` | float | Range boundaries. |
| `stats.stdDev` | float | Population standard deviation. |
| `stats.sampleCount` | int | Length of `rawSamples`. Always equals `iterations`. |
| `rawSamples` | float[] | Individual measurements in arrival order (not sorted). |

## Scenarios

| `scenario` key | Unit | Measures |
|----------------|------|---------|
| `cold_start_http_with_script` | ms | Server process start + module import + startup script + `initialize` + first `tools/call` |
| `cold_start_http_no_script` | ms | Same path without a startup script. Delta = startup-script cost. |
| `warm_call_latency_ms` | ms | Per-call latency on a pre-initialized session (runspace already acquired) |
| `concurrent_throughput_ms` | ms | Wall-clock ms for 4 concurrent `tools/call` completions on warm sessions |
| `memory_idle_mb` | MB | Server process working-set at idle, before any sessions |
| `memory_light_load_mb` | MB | Working-set after 10 sequential calls on one session |
| `memory_moderate_load_mb` | MB | Working-set after 3 rounds of 4 concurrent calls |

### Startup-script cost (derived)

Phase 4 computes startup-script cost as:

```
startup_script_cost_ms = cold_start_http_with_script.stats.mean
                       − cold_start_http_no_script.stats.mean
```

The two scenarios use identical config except the `Environment.StartupScript` field.

## Server Launch

`CharacterizationHttpServer` launches the server by invoking the pre-built assembly
directly: `dotnet <path>/PoshMcp.dll serve --transport http --url <url> --config <cfg>`.

This ensures `_serverProcess` is the real PoshMcp server process (not a dotnet CLI
host), so working-set readings and cold-start latency measure only the server without
CLI or MSBuild overhead.

The DLL is resolved from the workspace root and the build configuration detected from
the test output path (`AppContext.BaseDirectory` contains `/Release/` or `/Debug/`).
A `FileNotFoundException` is thrown with a clear build instruction if the assembly is
missing, so misconfigured launchers fail immediately.

## Configuration Used

All scenarios use `with-startup-script.appsettings.json` (or its no-script counterpart)
from `PoshMcp.Tests/Characterization/Assets/`. Key settings:

- `RuntimeMode: InProcess` — PowerShell runs in the server process (no subprocess startup overhead)
- `CommandNames: ["Get-Date"]` — single fast built-in cmdlet
- `Modules: ["Microsoft.PowerShell.Utility"]` — one module import
- `SessionRunspaceWarmStandbyCount: 0` — runspace created on first session demand
- `SessionRunspaceCapacity: 4` — max concurrent sessions

Set `SessionRunspaceWarmStandbyCount = 0` for consistent on-demand measurement.
Phase 4 must use the same values.

## Precision Notes

- Cold-start scenarios use `iterations = 5`. With 5 samples, p95 and p99 reflect the
  max observed value (linear interpolation at rank 4.8 and 4.96 respectively on a
  5-element array). They indicate worst-case from the small sample, not production tail latency.
  Increase `ColdStartIterations` in `V1BaselineCharacterizationTests` for tighter estimates
  (accepts longer CI run time).
- Warm-call scenarios use `iterations = 20`, giving more reliable p95/p99.
- Memory measurements are single-sample snapshots (`iterations = 1`). Process
  working-set fluctuates; treat these as order-of-magnitude indicators, not precise measurements.

## Phase 4 Usage

Phase 4 locates this artifact from the `v1-baseline-characterization` CI artifact
(see `.github/workflows/ci.yml`). Comparison logic should:

1. Join on `scenario` key.
2. Compute relative change: `(v2.mean − v1.mean) / v1.mean * 100`.
3. Flag scenarios where relative change exceeds the Phase 4 threshold (e.g., >10% regression).
4. Attach both artifacts as evidence to the Phase 4 PR.
