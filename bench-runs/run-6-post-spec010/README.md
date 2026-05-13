# Run 6 — Post-spec010 Cold-Start Re-measure

**Capture date:** 2026-05-13
**Branch:** `squad/232-bench-gate`
**Branch SHA:** `48db59aea3cf4ee5e63dd9a3b110a7ba4cfee83d` (parent of bench commit; HEAD before this commit)
**Spec:** [`specs/010-tool-self-documentation/spec.md`](../../specs/010-tool-self-documentation/spec.md) — FR-572, step 9
**Issue:** [#232](https://github.com/usepowershell/poshmcp/issues/232)
**Baseline:** [`bench-runs/run-5-pre-spec010/`](../run-5-pre-spec010/)

## Purpose

Re-runs `ColdStartBenchmark` after spec-010 (tool self-documentation) waves
1–7 landed on `main`. Compares per-mode Mean against
[run-5](../run-5-pre-spec010/) to enforce the FR-572 **<50%** cold-start
regression gate.

## Scenarios run

`PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark` — cold start = ctor →
start → first `Get-Date` invoke → dispose, measured per-iteration with
`[InvocationCount(1)]` so the Mean column is per-cold-start cost.

Three host modes parameterized:

| Mode          | Mean    | P95     | P99           |
|---------------|---------|---------|---------------|
| Single        | 6.470 s | 6.557 s | 6,569.304 ms  |
| Pool          | 6.483 s | 6.526 s | 6,528.059 ms  |
| ProcessPool   | 7.742 s | 7.875 s | 7,916.422 ms  |

See [`PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report-github.md`](./PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report-github.md)
for the full BenchmarkDotNet summary table and
[`COMPARISON.md`](./COMPARISON.md) for the per-mode regression check.

## Gating outcome

✅ **PASS** — all three modes regressed under 13% (Single +11.74%, Pool
+12.08%, ProcessPool +10.66%), well under the 50% gate.

## Exact command

```powershell
dotnet run -c Release --project PoshMcp.Benchmarks -- --filter "*ColdStart*"
```

Run from repo root. ApplicationInsights export is disabled by
`Program.cs` env vars so latency reflects executor cost, not telemetry.

> **Note (worktree):** This run was captured inside a git worktree at
> `poshmcp-232`. The benchmark binary was launched directly from
> `PoshMcp.Benchmarks/bin/Release/net10.0/PoshMcp.Benchmarks.exe` with
> `--artifacts bench-runs/run-6-post-spec010` to keep BDN's working
> directory inside the worktree. Functionally equivalent to the run-5
> command above.

## Machine

| Property                    | Value                                  |
|-----------------------------|----------------------------------------|
| OsName                      | Microsoft Windows 11 Enterprise        |
| CsName                      | SJMDEVBOX                              |
| CsNumberOfLogicalProcessors | 32                                     |
| CsTotalPhysicalMemory       | 137,191,112,704 bytes (~127.8 GiB)     |

## Toolchain

- BenchmarkDotNet v0.14.0
- Windows 11 (10.0.26200.8390)
- .NET SDK 10.0.108
- Host: .NET 10.0.8, X64 RyuJIT AVX2

## Artifacts in this folder

- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report-github.md` — BDN GitHub-flavored summary
- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report.csv` — machine-readable
- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-report.html` — interactive
- `PoshMcp.Benchmarks.Scenarios.ColdStartBenchmark-20260513-091132.log` — BDN run log
- `stdout.log` — full process stdout/stderr
- `COMPARISON.md` — per-mode regression check vs run-5
