# Runspace Pool vs Multi-Process Experiment Plan

**Spec:** 004-out-of-process-execution
**Issue:** #65 — OOP: Experiment with runspace pool parallelism vs multiple processes
**Author:** Hermes (PowerShell Expert)
**Created:** 2026-05-06
**Status:** Plan only — prototypes are follow-up issues

---

## 1. Background and Current State

### What ships today (Phases 1–4 complete)

The single-subprocess OOP path is in production:

- **Subprocess lifecycle** — `OutOfProcessCommandExecutor` launches one
  `pwsh -NoProfile -NonInteractive -File oop-host.ps1` per executor
  instance, attaches stdin/stdout/stderr, runs `ping`, hooks
  `Process.Exited`, and disposes via a `shutdown` request → wait →
  `Kill(entireProcessTree: true)` fallback.
- **ndjson protocol** — JSON-RPC-shaped messages framed by `\n`.
  Methods: `ping`, `setup`, `discover`, `invoke`, `shutdown`. Each
  request carries a GUID `id`; responses are correlated through a
  `ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>`
  drained by a background `ReadLoopAsync`.
- **Concurrency** — `_sendLock` (`SemaphoreSlim(1, 1)`) serializes
  stdin writes. Request correlation is already async-safe (the
  `_pending` map is keyed by id), so the protocol layer does not
  *require* serialization — only the host today does, because there is
  one runspace inside one process.
- **Discovery + invoke** — `discover` imports modules, walks
  `Get-Command` + parameter sets, returns `RemoteToolSchema[]`.
  `invoke` splats parameters, handles `[switch]`, returns
  `ConvertTo-Json -Depth 4 -Compress` output plus a `hadErrors` flag.
- **Setup** — `setup` applies module paths, optional PSGallery trust,
  `Install-Module`, `Import-Module`, file/inline startup scripts.
  Mirrors `PowerShellEnvironmentSetup` ordering.

### What we're trading off

The serialized-single-runspace model gives strong isolation from the
.NET host but has two known costs:

1. **No within-process parallelism.** Two simultaneous `tools/call`
   requests are funneled through one `Invoke()`. For workloads with
   long network waits (Az, Graph), throughput is bottlenecked even
   though the runspace is mostly idle.
2. **Cold start per process.** Importing `Az.Accounts` + `Az.Compute`
   into a fresh pwsh subprocess costs several seconds. If we ever scale
   horizontally by spinning up subprocesses on demand, every new
   instance pays that cost in full.

Issue #65 asks us to design and prototype two competing answers.

---

## 2. Option A — Runspace Pool Inside One Subprocess

### Design overview

Keep the existing single subprocess. Replace the in-host execution
model with `[runspacefactory]::CreateRunspacePool(min, max)`. Requests
arriving over stdin are dispatched onto a background thread that
acquires a `PowerShell` instance bound to the pool, runs the command
asynchronously, and posts the response when complete.

### Concrete shape of `oop-host.ps1` (Option A variant)

The file becomes a small dispatcher around three new components:

1. **InitialSessionState builder** — produced once at startup,
   captures module paths, imported modules, and any startup-script
   side effects so each runspace in the pool is born pre-warmed.
2. **Runspace pool** — `[runspacefactory]::CreateRunspacePool(1, N, $iss, $Host)`,
   `Open()`-ed before the read loop starts. `N` defaults to
   `Environment.ProcessorCount`, configurable via `setup` params.
3. **Per-request dispatcher** — for each ndjson line:
   - Parse `id` / `method` / `params` synchronously on the read thread.
   - For `ping` / `shutdown`: respond inline (no pool work).
   - For `setup`: the pool is closed, the ISS is rebuilt, the pool is
     reopened, then a single ack is sent. (Setup is rare and cannot
     race with invokes.)
   - For `discover` / `invoke`: create a `[powershell]::Create()`
     instance, assign `.RunspacePool = $pool`, call `BeginInvoke()`,
     and register an
     `[AsyncCallback]` (or a poll-based PSEventSubscriber) that posts
     the response when the handle completes.

### Request correlation

The protocol already routes by `id`. The host change is purely on the
emitting side: multiple completion callbacks may want to write to
stdout concurrently. We need exactly one writer.

**Plan:** introduce a thread-safe writer in PowerShell:

```powershell
$script:StdoutLock = [System.Threading.ReaderWriterLockSlim]::new()
function Write-NdjsonResponse {
    param($Id, $Result, $ErrorObj)
    $line = ConvertTo-NdjsonLine $Id $Result $ErrorObj   # pure compute, no lock
    $script:StdoutLock.EnterWriteLock()
    try {
        [Console]::Out.WriteLine($line)
        [Console]::Out.Flush()
    }
    finally { $script:StdoutLock.ExitWriteLock() }
}
```

`ReaderWriterLockSlim` is overkill (no readers), but `Monitor.Enter`
on a sync object works equally well. The point is one critical
section around `WriteLine + Flush` so no two responses interleave.

Reads are already single-threaded (the dispatcher loop owns stdin),
so no lock is needed there.

### ndjson concurrency hazards

1. **Interleaved partial writes.** Solved by the writer lock above —
   a single `WriteLine + Flush` call cannot be split.
2. **Stream pollution from cmdlet output.** Anything a cmdlet writes
   to the host (`Write-Host`, `Write-Warning` not redirected) goes
   to the same `[Console]::Out`. With multiple runspaces, stray host
   writes risk corrupting the protocol channel even with a writer
   lock, because the cmdlet bypasses the lock entirely.
   - **Mitigation:** keep `$PSStyle.OutputRendering = 'PlainText'`
     and `NO_COLOR=1`. Additionally, replace `[Console]::Out` for the
     pool runspaces with a `TextWriter.Null` adapter, and route any
     intentional diagnostics through `Write-Diag` (stderr).
   - The .NET side already has `IsNonJsonPowerShellStreamLine` to
     catch escaped warnings on stdout — we keep that as a backstop.
3. **`$Error` is per-runspace, not per-invocation.** The current
   invoke handler sets `hadErrors` from `$Error.Count`. With a pool,
   `$Error` belongs to whichever runspace ran the command. Each
   invoke must clear `$Error` *inside* the script block before
   running the user command, then read `$Error.Count` after.

### Setup model

Two viable approaches:

- **Bake into ISS, rebuild on `setup`** (recommended): build the
  `InitialSessionState` once, including imported modules and a
  startup script `ScriptBlock`. On `setup`, close the pool, rebuild
  the ISS, reopen the pool. Each new runspace is therefore pre-warmed
  with no per-runspace import cost beyond the ISS clone.
- **First-touch warm-up**: leave the ISS empty, run a setup script
  on each runspace as it's leased for the first time. Simpler, but
  the first N invokes pay a heavy cost.

Recommendation: start with the ISS approach. Modules survive
runspace lifetime, which matches the current single-runspace
behavior closely.

### Failure containment

This is the part that worries me most and the reason we still need
Option B as a real comparison.

A runspace pool shares one AppDomain. If `Az.Compute` corrupts a
type accelerator or pollutes a static, the corruption survives
runspace disposal. We get parallelism, but we lose the strongest
isolation guarantee that motivated OOP in the first place.

We will measure this directly in the benchmark harness (see §4).

### Backpressure

`BeginInvoke` against a saturated pool queues internally. We can
either:

- Let the pool queue (default), with a configured max queue length
  beyond which we return a `busy` error;
- Or surface backpressure to .NET by making the host respond
  immediately with a `queued` notification and the eventual result
  separately. (This requires a protocol extension — out of scope for
  the first prototype.)

First prototype: rely on `RunspacePool` queuing, expose
`SubprocessConcurrency` and `SubprocessQueueLimit` configuration
knobs.

### Files touched (Option A prototype)

- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` — refactored
  dispatcher + pool, or
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1` — new
  variant selectable via configuration so we can A/B without losing
  the current host. Recommendation: ship as a new file behind a
  `SubprocessHostMode: "Single" | "Pool"` config flag. Keeps the
  existing host as a known-good baseline during benchmarking.
- `OutOfProcessCommandExecutor.cs` — minor: select host script;
  remove `_sendLock` if measurements show it's pure overhead once
  the host is parallel-safe (it's not — `_sendLock` protects stdin
  writes from .NET-side concurrency, which we still want).

---

## 3. Option B — Pool of pwsh Processes

### Design overview

Keep `oop-host.ps1` as it is. Spin up `N` subprocesses managed by
.NET. Dispatch each `invoke` to whichever subprocess is free.
Discovery happens once, against any one subprocess (schemas are
identical). Setup runs against every subprocess at startup.

### Concrete shape

A new `OutOfProcessSubprocessPool` class wraps `N` instances of
`OutOfProcessCommandExecutor` (or a refactored `OutOfProcessHost`
that holds the per-process state). The public surface
(`ICommandExecutor`) stays the same.

**Dispatch strategy:**

- Maintain a `Channel<OutOfProcessHost>` of available hosts.
- `InvokeAsync` does `await channel.Reader.ReadAsync()` to lease,
  runs the request, returns the host to the channel in `finally`.
- This is a straightforward producer-consumer queue — fair, simple,
  no round-robin index to manage.

### Lifecycle

- **Startup**: launch N processes in parallel, run `ping` then
  `setup` on each. If any single process fails setup, decide
  policy: fail-fast (recommend for first prototype, matches current
  behavior) or degraded-pool (continue with the survivors).
- **Crash**: per-host `Process.Exited` triggers a replacement
  spawn. Replacement runs `ping` + `setup` before being returned to
  the channel.
- **Shutdown**: `shutdown` to each, wait for graceful exit, kill
  stragglers. Same logic per host as today.

### Queue semantics and fairness

The channel is FIFO, so requests are dispatched in arrival order
to the first free host. A noisy long-running request will tie up
its host but cannot starve others — different from a serialized
single host.

**Per-request timeouts** stay at the host level; if a host stops
responding, the existing kill-and-restart logic kicks it out of
the channel and replaces it. We do not need a separate pool-level
timeout in the first prototype.

### Config knobs

```jsonc
{
  "PowerShellConfiguration": {
    "RuntimeMode": "OutOfProcess",
    "SubprocessHostMode": "ProcessPool",
    "SubprocessPoolSize": 4,
    "SubprocessTimeoutSeconds": 30,
    "SubprocessMaxRestarts": 3
  }
}
```

`SubprocessPoolSize: 1` collapses to the current behavior — a clean
fallback if the pool path misbehaves.

### Failure containment

This is Option B's structural advantage. A bad command corrupts only
its own pwsh process. Crash/restart logic is per-host; the rest of
the pool keeps serving traffic. This is the closest thing to
"zero blast radius" the OOP design can offer.

### Files touched (Option B prototype)

- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessHost.cs` —
  extracted from `OutOfProcessCommandExecutor` so the per-process
  unit is reusable.
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessSubprocessPool.cs` —
  new, implements `ICommandExecutor` over a `Channel<OutOfProcessHost>`.
- `OutOfProcessCommandExecutor.cs` — kept as the `Single` mode
  implementation; minor refactor to share lifecycle code with
  `OutOfProcessHost`.
- `PoshMcp.Server/PowerShell/OutOfProcess/RuntimeMode.cs` /
  `PowerShellConfiguration.cs` — add `SubprocessHostMode` and
  `SubprocessPoolSize`.
- `Program.cs` — selects between executor implementations based
  on configuration.

---

## 4. Benchmark Harness Design

### Goals

We want apples-to-apples numbers across three configurations:

1. **Baseline** — current single subprocess, single runspace.
2. **Option A** — single subprocess, runspace pool of N.
3. **Option B** — pool of N subprocesses, single runspace each.

Same N (default `Environment.ProcessorCount`) for A and B so the
"degree of parallelism" variable is held constant.

### Scenarios

| Scenario | Cmdlet / script | What it stresses |
|----------|-----------------|------------------|
| **CPU-light, fast** | `Get-Date` (or a `1+1` script block) | Pure protocol + dispatch overhead |
| **CPU-bound** | `1..100000 \| ForEach-Object { $_ * $_ } \| Measure-Object -Sum` | Parallelism limit (CPU) |
| **I/O-bound (sleep)** | `Start-Sleep -Seconds 2` | Idle parallelism — should scale ~linearly with N for A and B |
| **Network-shaped** | `Invoke-WebRequest http://localhost:<test-server>` against an in-test HTTP server with 500 ms latency | Realistic Az/Graph workload |
| **Heavy serialization** | `Get-Process \| Select-Object -First 50` | Output normalization cost (already a known hotspot) |
| **Cold-start cost** | Time from process launch → first `invoke` returning | Startup latency — favors A (one process) on subsequent scaling, hurts B |
| **Crash recovery** | Trigger an OOM or `[System.Environment]::Exit(1)` mid-stream, then issue another invoke | Recovery latency — A may not recover at all if shared state is corrupted; B should recover cleanly |
| **Isolation** | Tool that mutates `$global:` state, then a second tool that reads it | Confirms whether Option A leaks state across runspaces (it shouldn't, but verify) |

### Metrics

Per scenario, record:

- **Latency** — p50, p95, p99 over a fixed request count (e.g. 500).
- **Throughput** — requests/second under sustained concurrent load
  (10, 50, 100 concurrent clients).
- **Memory** — peak working set of the .NET host *and* the pwsh
  process(es) sampled every second during the run.
- **Startup time** — wall-clock from `StartAsync` to first successful
  `invoke`.
- **Recovery time** — wall-clock from induced crash to next
  successful `invoke`.

### Harness shape

A new `PoshMcp.Benchmarks` console project (not a test project —
benchmark runs are too long for CI):

- BenchmarkDotNet for the latency/throughput scenarios.
- A small custom harness for crash/recovery (BenchmarkDotNet is
  awkward for "kill the process mid-run" tests).
- Runs against a real `OutOfProcessCommandExecutor` /
  `OutOfProcessSubprocessPool` configured in-process. No MCP client
  in the loop — we measure the executor, not the JSON-RPC stack.
- Outputs Markdown tables to `specs/004-out-of-process-execution/benchmark-results/`.

For the network-shaped scenario, the harness spins up an
`HttpListener` on `127.0.0.1:0` that responds after a configurable
delay. Self-contained, no external dependency.

### Pass/fail criteria for the recommendation

After the harness runs, we adopt whichever option meets all of:

- Throughput at 10 concurrent clients ≥ 4× the single-subprocess
  baseline on the I/O-bound and network-shaped scenarios.
- p95 latency on the CPU-light scenario no worse than 1.5× the
  baseline (i.e. the dispatch overhead is acceptable).
- Crash of one execution unit must NOT produce a server-visible
  error on requests routed elsewhere within 100 ms of the crash.

If neither option meets the isolation criterion, default to Option B.
If both meet it and Option A is materially cheaper on memory and
startup, prefer A.

---

## 5. Recommended Phasing (Follow-up Issues)

These should be filed as separate issues, blocked on this plan being
reviewed.

1. **#TBD — Extract `OutOfProcessHost`**. Refactor
   `OutOfProcessCommandExecutor` so the per-process state
   (process, streams, send lock, pending map, read loops) lives in a
   reusable `OutOfProcessHost` type. No behavior change. Unblocks
   both prototypes.

2. **#TBD — Option A prototype: runspace pool host**. Add
   `oop-host-pool.ps1` and a `SubprocessHostMode: "Pool"` config
   flag. Implement the synchronized writer, ISS-based pre-warm, and
   per-runspace `$Error` handling. Existing integration tests must
   pass against both modes.

3. **#TBD — Option B prototype: process pool executor**. Add
   `OutOfProcessSubprocessPool` and a `SubprocessHostMode:
   "ProcessPool"` config flag with `SubprocessPoolSize`. Existing
   integration tests must pass against the pool with size 1, 2, and 4.

4. **#TBD — Benchmark harness**. New `PoshMcp.Benchmarks` project
   implementing the scenarios in §4, with results landing in
   `specs/004-out-of-process-execution/benchmark-results/`.

5. **#TBD — Run benchmarks and write findings**. Three runs per
   scenario per mode, results table committed alongside a written
   analysis.

6. **#TBD — Adopt the winner**. Make the recommended mode the
   default; demote the loser to opt-in or remove it. Update spec 004
   and `DOCKER.md`.

The order matters: 1 unblocks 2 and 3, which can run in parallel. 4
can start in parallel with 2 and 3 (it only needs the executor
interface). 5 blocks on 2, 3, and 4. 6 blocks on 5.

---

## 6. Open Questions for Reviewer

1. **Default `N` for the pool.** `Environment.ProcessorCount` is the
   obvious choice but for I/O-bound Az workloads we may want
   higher. Do we ship a sensible default and let users tune, or pick
   a fixed small number (4) until we have telemetry?
2. **Should Option A ship at all if Option B wins?** Maintaining two
   host scripts is real cost. If B is the clear winner, I'd remove
   the pool host rather than carry it as a configurable mode.
3. **Network-shaped scenario fidelity.** A local `HttpListener` is
   easy but doesn't reproduce Az SDK overhead (token caching,
   retries). Worth a single end-to-end test against a real Azure
   tenant for the final recommendation? Steven's call — it's an
   ops cost.
4. **Cancellation propagation.** Today, `cancellationToken` cancels
   the .NET-side wait but does not cancel the in-flight pwsh work.
   Worth fixing as part of this work, or hold for a separate issue?
   (My vote: separate issue — out of scope.)
