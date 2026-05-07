# OOP Execution — Benchmark Findings & Adoption Recommendation

**Date:** 2026-05-06
**Issue:** #195 (run benchmarks + write findings) — unblocks #196 (adopt winner as default)
**Inputs:** [`benchmark-results.md`](benchmark-results.md) (run-3, post-#204), `bench-runs/run-3.log`, `bench-runs/run-3-artifacts/`
**Modes compared:** `Single` (existing single-runspace, single-process), `Pool` (Option A — in-process runspace pool, one subprocess), `ProcessPool` (Option B — multi-process pool of single-runspace hosts)

This document interprets the run-3 numbers and proposes a default mode for spec 004. The adoption decision itself belongs to issue #196.

---

## 1. WarmInvoke throughput (concurrency = 10, network-shaped)

| Mode        | Mean     | StdDev   | P99       | Speedup vs Single (mean / P99) |
|-------------|---------:|---------:|----------:|-------------------------------:|
| Single      | 661.2 ms | 22.44 ms | 686.2 ms  | 1.00× / 1.00×                  |
| Pool        | 136.2 ms |  6.34 ms | 143.3 ms  | **4.86× / 4.79×**              |
| ProcessPool | 200.7 ms |  1.11 ms | 201.4 ms  | 3.30× / 3.41×                  |

**Why `Pool` wins.** With 10 concurrent invokes against a single-runspace `Single` host, each call serializes through the `_sendLock` and the lone runspace; throughput is bounded at `1 / per-invoke-time`. `Pool` keeps that single subprocess but multiplies the runspace count. Pre-warming the `InitialSessionState` (modules already imported, types loaded) means leases dispatch directly into a hot runspace with no per-call import or compile cost. There is no per-call process spawn and no IPC channel beyond the one already in use, so the overhead per dispatch is just a runspace lease + a script-block invoke.

**Why `ProcessPool` comes second.** Each invoke crosses a stdin/stdout ndjson boundary into a different `pwsh` process. Once the host is warm and the runspace inside it is initialized, the per-call cost is dominated by JSON serialization, OS pipe transfer, and a context switch — not by anything PowerShell is doing. That's a fixed tax per call that `Pool` simply doesn't pay. 3.30× is still well above the spec's 1.5× CPU-bound floor and clears the 2× serialization bar; it just can't catch a design that avoids the cross-process hop entirely.

**What the tail tells us.** `ProcessPool` posts the tightest spread of the three (StdDev 1.11 ms, P99 201.4 ms — only ~0.7 ms above its mean). `Pool` is good but noisier (StdDev 6.34 ms, P99 ~7 ms over mean). The interpretation is that hard process boundaries protect against in-process contention: a stalled runspace inside `Pool` competes with its peers for the same GC, ThreadPool, and `[Console]::Out`; in `ProcessPool` it can't reach across a process boundary to delay anything else. Isolation is paying off as predictable latency.

## 2. ColdStart (`ctor → start → first invoke → dispose`)

| Mode        | Mean    | P99       |
|-------------|--------:|----------:|
| Single      | 5.403 s | 5,417.9 ms |
| ProcessPool | 5.803 s | 5,821.8 ms |
| Pool        | 5.881 s | 5,929.1 ms |

**Why `Single` wins.** Cold-start is dominated by `pwsh` startup (~5.4 s on this Arm64 host). `Single` does that once, services one invoke, and exits. `Pool` and `ProcessPool` do the same thing plus warmup work — `Pool` fills its runspace pool inside the host before the first lease is granted; `ProcessPool` spawns additional subprocesses to bring the pool to capacity. Both pay an extra ~400–500 ms upfront in exchange for warm throughput later. The cost-per-mode is small in absolute terms (<10% spread) and amortizes to zero after the second invoke; cold-start ceases to be a meaningful axis for any caller that issues more than a single command.

## 3. Payload-size crossover

| Bytes     | Fastest mean | Lowest allocated |
|----------:|:-------------|:-----------------|
|     1,024 | Single (286 μs) | Single (11.78 KB) |
|    16,384 | Single (975 μs) | Pool (124.87 KB)  |
|   262,144 | ProcessPool (14,400 μs) | Pool (2,459.68 KB) |
| 1,048,576 | Pool (51,358 μs) | Pool (13,786.75 KB) |

**Why the crossover happens.** At 1 KB the per-invoke fixed overhead dominates: `Single` has none, `Pool` pays a runspace-lease cost (~6× `Single` at this size), `ProcessPool` pays the cross-process channel cost (~2× `Single`). As payload grows, the constant-time dispatch overhead becomes a smaller fraction of total work and the modes converge.

At 256 KB `ProcessPool` is fastest because the dedicated subprocess is doing serialization in its own GC heap and address space — no contention with caller-side allocations, more headroom in the L2 cache for the JSON write path. Mean drops below `Pool` and well below `Single`.

At 1 MB `Pool` takes the lead, mostly on memory pressure rather than CPU. Allocated bytes for `Pool` are ~13.8 MB versus ~16.3 MB (`Single`) and ~17.4 MB (`ProcessPool`). One subprocess plus N runspaces sharing pooled buffers is allocating less per invoke than spinning up the same payload through a fresh stdin/stdout transfer to a sibling process. At this size GC pressure is the bottleneck.

---

## 4. Recommendation for #196

**Default `HostMode` should be `Pool`** (Option A — in-process runspace pool inside a single subprocess).

The 4.86× warm-throughput win at concurrency 10 is the dominant signal. It clears the spec's per-scenario 4× bar for I/O-shaped workloads, ProcessPool does not, and the realistic load for an MCP server is many concurrent warm invokes from one or more clients — not a stream of one-off cold calls. ColdStart's 400–500 ms penalty is paid once per server lifetime; payload-size results show `Pool` is competitive at small sizes and best at the largest, so there is no payload regime where adopting `Pool` costs more than a small constant. `ProcessPool` should remain available as an opt-in mode (`HostMode=ProcessPool`) for callers who need hard isolation between concurrent invokes or who care about the tighter tail-latency behavior more than absolute throughput — its ~3.3× speedup with the lowest StdDev of the three is the right answer for tail-sensitive or trust-boundary-sensitive workloads. `Single` should remain the documented choice for short-lived CLI invocations where cold-start is the only number that matters.

---

## 5. Caveats

- **`--job short` is fast, not exhaustive.** 3 iterations × 3 warmup × 1 launch is enough to rank the modes confidently on the gaps observed here (~5× and 0.7-ms StdDevs are well outside noise) but should not be cited as production capacity numbers. A `--job long` rerun before any SLO-bearing claim would be appropriate.
- **Single-machine, single-architecture data.** Run-3 is one Windows 11 / Arm64 host. x64 ratios are likely similar but not measured. Anyone retesting on different hardware should re-run the same three benchmark classes before drawing conclusions.
- **Load shape matters.** WarmInvoke is a network-shaped workload (per spec 004). CPU-bound, GC-heavy, or `[Console]::Out`-noisy workloads will close the gap between `Pool` and `ProcessPool` — at the limit, isolation becomes the deciding factor and `ProcessPool`'s tail behavior dominates.
- **Pool's trust boundary is weaker.** Runspaces inside `Pool` share the same process: same GC, same `[Console]::Out`, same loaded modules, same AppDomain-equivalent. A misbehaving cmdlet that writes directly to `[Console]::Out` or pollutes process-global state affects siblings. The custom `PSHost` work tracked from PR #187's review (Farnsworth #1) is a prerequisite for relying on `Pool` as the default in adversarial scenarios.
- **Cancellation propagation is not measured here.** Per Farnsworth's review, `Pool`'s effective capacity under stuck invokes is `N - stuck_invokes`. Run-3 does not exercise stuck-invoke scenarios. The recommendation above assumes the cancellation work tracked separately lands before `Pool` is shipped as default.

---

## 6. Open questions for #196

- **Config keys.** The current `HostMode` enum (`Single` | `Pool` | `ProcessPool`) is in place. #196 needs to decide: does default flip to `Pool` for everyone, or stay `Single` with `Pool` as documented opt-in? Default-flip changes runtime behavior for every existing deployment.
- **Pool sizing default.** Plan currently calls for `Environment.ProcessorCount`. Worth confirming on a constrained container (e.g., 2-vCPU pod) — pool size 2 may not show the same ratios.
- **Opt-in story.** If `Pool` becomes default, what does the docs path look like for callers who want isolation? `ProcessPool` opt-in needs a clear "when to switch" guide rooted in the tail-latency / trust-boundary tradeoff above.
- **Doc updates.** `DESIGN.md` and `README.md` both reference single-runspace assumptions in places. A sweep is needed once #196 lands a default.
- **Cancellation ordering.** Per caveat 5, the cancellation issue needs to land before — or as a hard prerequisite of — the default flip. #196 should make this an explicit gate, not a follow-up.
