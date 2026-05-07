# OOP Execution — Benchmark Results (Run-3, post-#204)

**Date:** 2026-05-06
**Base commit:** `e4cf7d9` (`fix(oop): wrap ConvertTo-Json to handle shadowed Content property (#203) (#204)`)
**Branch:** `squad/195-bench-findings`
**Issue:** #195

## Methodology

- Harness: `PoshMcp.Benchmarks` (BenchmarkDotNet v0.14.0)
- Filter: `*ColdStartBenchmark* *PayloadSizeSerializationBenchmark* *WarmInvokeThroughputBenchmark*`
- Job: `--job short` (3 iterations, 3 warmup, 1 launch)
- Build: `dotnet build PoshMcp.sln -c Release`
- Run command (from worktree root):
  `dotnet run --project PoshMcp.Benchmarks -c Release --no-build -- --filter '*ColdStartBenchmark*' '*PayloadSizeSerializationBenchmark*' '*WarmInvokeThroughputBenchmark*' --job short`
- Total wall time: 5m 6s (18 benchmarks)
- Matrix: 3 `HostMode` values — `Single`, `Pool` (in-process runspace pool, Option A), `ProcessPool` (multi-process pool, Option B)

### Environment

- BenchmarkDotNet v0.14.0
- Windows 11 (10.0.26200.8328)
- Processor: Unknown (Arm64)
- .NET SDK 10.0.202
- Host runtime: .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD
- GC: Concurrent Server

### Status of prior runs

`bench-runs/run-1.log`, `run-1-artifacts/`, `run-2.log`, and `run-2-artifacts/` were captured before PR #204 landed. Their `Single` and `ProcessPool` rows on `WarmInvokeThroughputBenchmark` are unreliable because of the `BasicHtmlWebResponseObject.Content` `ConvertTo-Json` failure that #204 fixed. Those files are kept on disk as historical reference only; **run-3 is the canonical dataset for spec 004.**

## ColdStart — `ctor → start → first invoke → dispose`

Job: `IterationCount=3, InvocationCount=1, WarmupCount=3, LaunchCount=1`

| Mode        | Mean    | StdDev   | P95     | P99           |
|-------------|--------:|---------:|--------:|--------------:|
| Single      | 5.403 s | 14.4 ms  | 5.416 s | 5,417.913 ms  |
| Pool        | 5.881 s | 43.6 ms  | 5.924 s | 5,929.122 ms  |
| ProcessPool | 5.803 s | 18.3 ms  | 5.820 s | 5,821.786 ms  |

**Observation:** Single is fastest cold-start by ~400–500 ms. Pool and ProcessPool both pay an additional warm-up cost (pool fill / extra subprocess spawns) on first invoke. Cold-start is dominated by `pwsh` startup; the absolute spread is small (<10%).

## PayloadSizeSerializationBenchmark — round-trip a string of size `PayloadBytes` through `Write-Output`

Job: `IterationCount=3, WarmupCount=3, LaunchCount=1`

| Mode        | PayloadBytes | Mean        | P95         | P99       | Allocated   |
|-------------|-------------:|------------:|------------:|----------:|------------:|
| Single      | 1,024        |    286.3 μs |    296.5 μs |  0.297 ms |    11.78 KB |
| Single      | 16,384       |    974.6 μs |    993.3 μs |  0.994 ms |   126.96 KB |
| Single      | 262,144      | 19,822.2 μs | 29,674.0 μs | 30.946 ms |  2,486.01 KB |
| Single      | 1,048,576    | 57,208.6 μs | 59,305.6 μs | 59.355 ms | 16,339.57 KB |
| Pool        | 1,024        |  1,667.5 μs |  1,804.0 μs |  1.808 ms |    12.30 KB |
| Pool        | 16,384       |  2,475.0 μs |  2,691.4 μs |  2.702 ms |   124.87 KB |
| Pool        | 262,144      | 19,115.3 μs | 24,715.4 μs | 25.381 ms |  2,459.68 KB |
| Pool        | 1,048,576    | 51,358.4 μs | 53,772.7 μs | 53.976 ms | 13,786.75 KB |
| ProcessPool | 1,024        |    536.8 μs |    632.9 μs |  0.646 ms |    12.25 KB |
| ProcessPool | 16,384       |  1,128.1 μs |  1,149.1 μs |  1.151 ms |   125.50 KB |
| ProcessPool | 262,144      | 14,400.0 μs | 16,105.9 μs | 16.345 ms |  2,830.12 KB |
| ProcessPool | 1,048,576    | 55,332.0 μs | 56,932.2 μs | 57.051 ms | 17,363.35 KB |

**Observation:** At small payloads (≤16 KB), `Single` is fastest (no pool dispatch overhead). At medium payloads (256 KB), `ProcessPool` is fastest — its serialization path has lower mean and tail than `Pool` or `Single`. At very large payloads (1 MB), `Pool` edges out both, mostly on lower allocated bytes (~13.8 MB vs ~16.3 MB / ~17.4 MB). `Pool` shows higher fixed overhead at the small end (1024-byte mean is ~6× `Single`), consistent with a per-invoke channel/lease cost.

## WarmInvokeThroughputBenchmark — warm invoke @ N concurrency (network-shaped, target 4× bar)

Job: `IterationCount=3, WarmupCount=3, LaunchCount=1`

| Mode        | Concurrency | Mean     | StdDev   | P95      | P99        | Allocated  |
|-------------|------------:|---------:|---------:|---------:|-----------:|-----------:|
| Single      | 10          | 661.2 ms | 22.44 ms | 683.4 ms | 686.233 ms | 236.00 KB  |
| Pool        | 10          | 136.2 ms |  6.34 ms | 142.5 ms | 143.321 ms | 231.29 KB  |
| ProcessPool | 10          | 200.7 ms |  1.11 ms | 201.4 ms | 201.406 ms | 252.02 KB  |

**Speedup vs Single:**

| Mode        | Mean speedup | P99 speedup |
|-------------|-------------:|------------:|
| Pool        | 4.86×        | 4.79×       |
| ProcessPool | 3.30×        | 3.41×       |

**Observation:** With the `BasicHtmlWebResponseObject` serialization defect resolved by #204, both `Pool` and `ProcessPool` now produce valid measurements at concurrency 10 on a network-shaped workload. `Pool` clears the per-scenario 4× bar (4.86×). `ProcessPool` falls short of 4× but stays well above the 1.5× CPU-bound floor, with the tightest StdDev / P99 spread of the three modes (1.11 ms / 201.4 ms).

## Files

- Raw log: `bench-runs/run-3.log`
- BenchmarkDotNet artifacts (csv / html / github md): `bench-runs/run-3-artifacts/`
- Historical (pre-#204) reference: `bench-runs/run-1.log`, `bench-runs/run-1-artifacts/`, `bench-runs/run-2.log`, `bench-runs/run-2-artifacts/` — not committed; left in worktree.

## Next

Findings + recommendation for issue #196 (adopt winner) follow in `benchmark-findings.md` (separate commit).
