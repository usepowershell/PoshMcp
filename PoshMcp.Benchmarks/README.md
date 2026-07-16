# PoshMcp.Benchmarks

BenchmarkDotNet harness comparing the three out-of-process executor configurations
called out in `specs/004-out-of-process-execution/runspace-pool-experiment-plan.md`:

| `HostMode` | Subprocesses | Runspaces / process | Notes |
|------------|--------------|---------------------|-------|
| `Single`   | 1            | 1                   | Baseline — existing single-host, single-runspace executor. |
| `Pool`     | 1            | N (Option A)        | One subprocess, runspace pool of N. |
| `ProcessPool` | N         | 1 each (Option B)   | Pool of N subprocesses, single runspace each. |

`HostMode` is a `[Params]` axis on every scenario, so a single `dotnet run` produces
one row per (mode × scenario × payload-size) combination in one results table.

## Scenarios

| Scenario class                               | What it measures |
|----------------------------------------------|------------------|
| `ColdStartBenchmark`                         | ctor → start → first invoke → dispose, per iteration. |
| `WarmInvokeThroughputBenchmark`              | N concurrent `Invoke-WebRequest` calls against an in-proc HTTP server. |
| `PayloadSizeSerializationBenchmark`          | Round-trip a string of size `PayloadBytes` (1 KB / 16 KB / 256 KB / 1 MB). |
| `ProcessCrashRecoveryBenchmark`              | Kill one underlying `pwsh` host, then time the next successful invoke. |
| `RunspaceCorruptionRecoveryBenchmark`        | Slow invoke in flight → time a fast probe (head-of-line-blocking gate). |
| `HttpSessionBenchmark`                       | HTTP startup + first session call, warm-session latency, concurrent session throughput, and capacity rejection. |

## Running

All commands assume the repo root.

### Run everything (all scenarios, all modes)

```powershell
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter *
```

### Filter by scenario

```powershell
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*WarmInvoke*'
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*ColdStart*'
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*PayloadSize*'
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*ProcessCrash*'
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*RunspaceCorruption*'
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*HttpSession*'
```

### Filter by mode (BDN matches `[Params]` values in the case ID)

```powershell
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*Mode=ProcessPool*'
```

### Quick smoke test (near-zero iterations, just verifies wiring)

```powershell
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter '*WarmInvoke*' --job dry
```

### List discovered cases without running

```powershell
dotnet run --project PoshMcp.Benchmarks --configuration Release -- --list flat
```

## Output

BenchmarkDotNet writes results to `BenchmarkDotNet.Artifacts/` next to the run
working directory (typically `PoshMcp.Benchmarks/BenchmarkDotNet.Artifacts/`).
The Markdown table named `*-report-github.md` is the artifact #195 consumes.

The Markdown report includes these columns relevant to the AC for #194:

| Column           | Source |
|------------------|--------|
| `Mode`           | `HostMode` `[Params]` axis on every scenario. |
| (scenario)       | One BDN case row per `[Benchmark]` method. |
| `PayloadBytes`   | `[Params]` axis on `PayloadSizeSerializationBenchmark`. |
| `Mean`           | BDN built-in. |
| `P95`            | `StatisticColumn.P95` (BDN built-in). |
| `P99`            | `P99StatisticColumn` (custom — see `P99StatisticColumn.cs`). |
| Crash-recovery   | `Mean` column on `ProcessCrashRecoveryBenchmark` rows IS the per-mode recovery time (each iteration kills one host and then awaits the next invoke). |

## Telemetry

Application Insights / OpenTelemetry export (spec-008) is disabled inside the
benchmark process by `Program.cs` setting `ApplicationInsights__Enabled=false`
and clearing `APPLICATIONINSIGHTS_CONNECTION_STRING` before BDN starts. The
defaults in `PoshMcp.Server` are already off, but the env-var override defends
against an inherited connection string in the host shell.

## Runtime caveats

- A full run across all five scenarios × three modes × payload-size axis takes
  on the order of **30–60 minutes** on a modern dev box; allow longer when
  including the 1 MB payload row.
- The crash-recovery scenario kills `pwsh` subprocesses by PID via reflection
  into private fields of `OutOfProcessCommandExecutor` / `OutOfProcessSubprocessPool`.
  This is intentional — the bench-only crash hook lives in `ExecutorFactory.cs`
  to keep production types clean.
- For `Single` and `Pool` modes the crash-recovery benchmark must dispose and
  recreate the executor (only one subprocess existed). The `Mean` column for
  those rows therefore reports cold-start cost, which is the correct answer
  to "time until next successful request" for those configurations.
- Scenarios skip the executor's `SetupAsync` step — built-in cmdlets like
  `Get-Date`, `Get-Random`, `Write-Output`, `Invoke-WebRequest`, and
  `Start-Sleep` are callable directly. The harness measures executor cost,
  not module install / import.
- `HttpSessionBenchmark` is intentionally different: it launches the actual
  HTTP server with `BenchmarkAssets/http-session-benchmark.appsettings.json`.
  Its first-session row includes server tool discovery, the configured
  `Microsoft.PowerShell.Utility` import, inline startup-script setup, and the
  first `initialize` + `tools/call` exchange. Its warm row excludes that
  startup work. The bounded-capacity row fills the configured four session
  runspaces, then reports the response status for one more session's tool call;
  it demonstrates bounded rejection rather than asserting a machine-specific
  duration.

## Quality gates

CI does not run timing benchmarks: shared runners are not stable enough for
time-based pass/fail thresholds. Instead, the `Benchmark contract` CI step
builds the benchmark executable and verifies that the reproducible HTTP/session
scenarios are discoverable. Capture timing reports on a controlled machine with
the same command and compare like-for-like runs (same hardware, SDK, config,
and benchmark filter). Commit or attach the resulting
`*-report-github.md`/CSV when a performance decision needs review; treat large
same-machine regressions as investigation signals, not normal test assertions.
