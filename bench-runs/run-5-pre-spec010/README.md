# Run 5 — Pre-spec010 Cold-Start Baseline

**Capture date:** 2026-05-12
**Branch:** `squad/223-bench-baseline`
**Branch SHA:** `16878b84396d9b3b4dcc0f0f9a4980db25561435` (parent of bench commit; HEAD before this commit)
**Spec:** [`specs/010-tool-self-documentation/spec.md`](../../specs/010-tool-self-documentation/spec.md) — FR-572
**Issue:** [#223](https://github.com/usepowershell/poshmcp/issues/223)

## Purpose

Captures pre-change cold-start metrics so spec-010 implementation work
(tool self-documentation) can be measured for regression. This baseline
**gates merge**: post-spec010 cold-start must not regress more than 50%
versus the numbers in this folder.

## Scenarios run

`PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark` — cold start = ctor →
start → first `Get-Date` invoke → dispose, measured per-iteration with
`[InvocationCount(1)]` so the Mean column is per-cold-start cost.

Three host modes parameterized:

| Mode          | Mean    | P95     | P99           |
|---------------|---------|---------|---------------|
| Single        | 5.790 s | 5.794 s | 5,793.955 ms  |
| Pool          | 5.784 s | 5.812 s | 5,815.280 ms  |
| ProcessPool   | 6.996 s | 7.046 s | 7,048.350 ms  |

See [`PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report-github.md`](./PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report-github.md)
for the full BenchmarkDotNet summary table.

## Exact command

```powershell
dotnet run -c Release --project PoshMcp.Benchmarks -- --filter "*ColdStart*"
```

Run from repo root. ApplicationInsights export is disabled by
`Program.cs` env vars so latency reflects executor cost, not telemetry.

## Machine

| Property                    | Value                                  |
|-----------------------------|----------------------------------------|
| OsName                      | Microsoft Windows 11 Enterprise        |
| CsName                      | SJMDEVBOX                              |
| CsNumberOfLogicalProcessors | 32                                     |
| CsTotalPhysicalMemory       | 137,191,112,704 bytes (~127.8 GiB)     |

## Toolchain

- BenchmarkDotNet v0.14.0
- Windows 11 (10.0.26200.8246)
- .NET SDK 10.0.107
- Host: .NET 10.0.7, X64 RyuJIT AVX2

## Artifacts in this folder

- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report-github.md` — BDN GitHub-flavored summary
- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report.csv` — machine-readable
- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report.html` — interactive
- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-20260512-183244.log` — BDN run log
- `stdout.log` — full process stdout/stderr

## Gating rule (post-spec010)

When spec-010 work lands, capture `bench-runs/run-N-post-spec010/` with
the same command and compare per-mode Mean. **Reject** if any mode's
Mean regresses more than 50% versus the corresponding row above.
