# Runspace Pool vs Multi-Process Experiment Plan

**Spec:** 004-out-of-process-execution
**Issue:** #65 — OOP: Experiment with runspace pool parallelism vs multiple processes
**Author:** Hermes (PowerShell Expert)
**Created:** 2026-05-06
**Last revised:** 2026-05-06
**Status:** Plan only — prototypes are follow-up issues

> **Note on path drift:** Issue #65 references `specs/out-of-process-execution.md`.
> The current canonical location is `specs/004-out-of-process-execution/`. This
> plan uses the canonical path; the issue body is stale.

---

## 1. Background and Current State

### What ships today

The single-subprocess OOP path is in production. Spec 004 does not (yet)
contain a phase manifest; the description below enumerates the shipped
capabilities directly rather than referencing phase numbers:

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
  stdin writes from the .NET side. Request correlation is already
  async-safe (the `_pending` map is keyed by id), so the protocol
  layer does not *require* serialization on the host side — only the
  host today does, because there is one runspace inside one process.
- **Discovery + invoke** — `discover` imports modules, walks
  `Get-Command` + parameter sets, returns `RemoteToolSchema[]`.
  `invoke` splats parameters, handles `[switch]`, returns
  `ConvertTo-Json -Depth 4 -Compress` user output (the response
  envelope itself uses `-Depth 10`) plus a `hadErrors` flag.
- **Setup** — `setup` applies module paths, optional PSGallery trust,
  `Install-Module`, `Import-Module`, file/inline startup scripts.
  Mirrors `PowerShellEnvironmentSetup` ordering.

### Pre-existing bug worth its own issue

The current `Invoke-InvokeHandler` in `oop-host.ps1` (around lines
558–565) reads `$Error.Count` after `& $commandName` to set
`hadErrors`, but **does not clear `$Error` first**. Errors from prior
invocations leak across calls in the existing single-runspace mode.
This is independent of the pool experiment and should be filed as a
standalone bug-fix issue (see §5, follow-up #0). The pool work
amplifies the problem (see §2 below) but does not cause it.

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
model with `[runspacefactory]::CreateRunspacePool(min, max, $iss, $host)`
where `$host` is a **custom `PSHost` implementation** (see stream
hygiene below — this is required, not optional). Requests arriving
over stdin are dispatched onto a background thread that acquires a
`PowerShell` instance bound to the pool, runs the command
asynchronously, and posts the response when complete.

### Concrete shape of `oop-host.ps1` (Option A variant)

The file becomes a small dispatcher around four new components:

1. **InitialSessionState builder** — produced once at startup,
   captures module paths, imported modules, and any startup-script
   side effects so each runspace in the pool is born pre-warmed.
2. **Custom `PSHost` + `PSHostUserInterface`** — used to construct
   the pool. Routes any `Write-Host` / `Write-Warning` / progress /
   prompt output to stderr (or a `TextWriter.Null`-equivalent sink),
   never to `[Console]::Out`. This is the only realistic way to
   prevent stream pollution in the pool — see "stream hygiene"
   below for why we cannot just swap `Console.Out`.
3. **Runspace pool** — `[runspacefactory]::CreateRunspacePool(1, N, $iss, $customHost)`,
   `Open()`-ed before the read loop starts. `N` defaults to
   `Environment.ProcessorCount`, configurable via `setup` params.
4. **Per-request dispatcher** — for each ndjson line:
   - Parse `id` / `method` / `params` synchronously on the read thread.
   - For `ping` / `shutdown`: respond inline (no pool work).
   - For `setup`: run the **quiesce protocol** (see "Setup model"
     below). Not a simple inline rebuild.
   - For `discover` / `invoke`: create a `[powershell]::Create()`
     instance, assign `.RunspacePool = $pool`, call `BeginInvoke()`,
     and register an `[AsyncCallback]` (or a poll-based
     `PSEventSubscriber`) that posts the response when the handle
     completes.

### Request correlation

The protocol already routes by `id`. The host change is purely on the
emitting side: multiple completion callbacks may want to write to
stdout concurrently. We need exactly one writer.

**Plan:** introduce a thread-safe writer in PowerShell:

```powershell
$script:StdoutSync = [object]::new()
function Write-NdjsonResponse {
    param($Id, $Result, $ErrorObj)
    $line = ConvertTo-NdjsonLine $Id $Result $ErrorObj   # pure compute, no lock
    [System.Threading.Monitor]::Enter($script:StdoutSync)
    try {
        [Console]::Out.WriteLine($line)
        [Console]::Out.Flush()
    }
    finally { [System.Threading.Monitor]::Exit($script:StdoutSync) }
}
```

A simple `Monitor.Enter` is sufficient — there are no readers to
contend with. The point is one critical section around
`WriteLine + Flush` so no two responses interleave.

Reads are already single-threaded (the dispatcher loop owns stdin),
so no lock is needed there.

### Stream hygiene (corrected)

Three classes of host output need to be kept off the protocol channel:

1. **Interleaved partial response writes.** Solved by the writer lock
   above — a single `WriteLine + Flush` call cannot be split.
2. **Cmdlet output to the host (`Write-Host`, `Write-Warning`,
   progress, prompts).** **Important correction:** `Console.Out` is a
   process-global static. It cannot be scoped per runspace via the
   `InitialSessionState`. The realistic mitigations are:
   - **Custom `PSHost` per pool** (primary mechanism): pass a custom
     `PSHost` + `PSHostUserInterface` to `CreateRunspacePool`. Route
     `WriteLine` / `Write` / `WriteWarningLine` / `WriteErrorLine` /
     `WriteProgress` / prompt methods to stderr or a discard sink.
     This intercepts everything that goes through `$Host.UI`.
   - **Environment hardening** (defense in depth):
     `$PSStyle.OutputRendering = 'PlainText'` and `NO_COLOR=1`.
   - **Backstop on the .NET side**: keep `IsNonJsonPowerShellStreamLine`
     to drop any line that still slips through.
3. **Per-pipeline stream divergence.** Beyond `$Error`, each pipeline
   has its own warning / verbose / information / debug streams. Any
   `hadErrors`-style summary derived from one stream must be computed
   from the per-pipeline `PowerShell.Streams.*` collections (e.g.
   `$ps.Streams.Error.Count`), not the runspace-wide automatic
   variables, or the parity with single-runspace behavior diverges.

### Per-pipeline `$Error` and stream handling

The current `Invoke-InvokeHandler` reads runspace-wide `$Error` after
the call. In a pool, `$Error` belongs to whichever runspace executed
the command. Two corrections are needed:

- **Inside each invoke**, clear `$Error` before running the user
  command, then read `$Error.Count` after. This also fixes the
  pre-existing single-runspace bug noted in §1.
- **Prefer per-pipeline streams** when reachable. When the dispatcher
  uses `[powershell]::Create().RunspacePool = $pool`, it has direct
  access to `$ps.Streams.Error`, `$ps.Streams.Warning`, etc. These
  are scoped to the single invocation and are the right source of
  truth for `hadErrors` (and any future warning/verbose surfaces),
  independent of runspace-wide automatic variables.

### Setup model

Two viable approaches:

- **Bake into ISS, rebuild on `setup`** (recommended): build the
  `InitialSessionState` once, including imported modules and a
  startup script `ScriptBlock`. On `setup`, run the quiesce protocol
  below.
- **First-touch warm-up**: leave the ISS empty, run a setup script
  on each runspace as it's leased for the first time. Simpler, but
  the first N invokes pay a heavy cost.

Recommendation: ISS approach. Modules survive runspace lifetime,
which matches the current single-runspace behavior closely.

**Quiesce protocol (required — corrects an earlier "cannot race" claim):**

`OutOfProcessCommandExecutor.SendRequestAsync` does **not** gate
`setup` requests against in-flight `invoke` requests today. The only
serialization is `_sendLock` around stdin writes. Closing and
reopening the pool while invokes are queued or active will either
drop them or hang. The host must:

1. Mark the host "draining" — new `invoke` requests receive a `busy`
   error (or queue at the .NET layer if we add executor-level gating;
   see follow-up #2 description).
2. Wait for all in-flight `BeginInvoke` handles to complete (or
   timeout — bounded by `SubprocessTimeoutSeconds`).
3. Close the pool, rebuild the ISS, reopen the pool.
4. Clear "draining" and ack the `setup`.

This protocol may also need a corresponding gate on the .NET side so
`setup` does not interleave with `invoke` at the executor layer.
Decide during the prototype whether the gate lives in the host, the
executor, or both.

### Failure containment

This is the part that worries me most and the reason we still need
Option B as a real comparison.

A runspace pool shares one AppDomain. If `Az.Compute` corrupts a
type accelerator or pollutes a static, the corruption survives
runspace disposal. We get parallelism, but we lose the strongest
isolation guarantee that motivated OOP in the first place.

We will measure this directly in the benchmark harness — but with a
**runspace-level corruption probe**, not a process-kill (see §4
below). Process-kill in Option A measures process restart, which is
the same gate as the baseline; it does not test what makes A
different.

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

**Observability is required, not optional.** Operators have no signal
to tune `SubprocessQueueLimit` without metrics. The prototype must
emit at minimum:

- `runspace_pool_queue_depth` (gauge, sampled or event-driven).
- `runspace_pool_lease_wait_ms` (histogram, per-invoke).
- `runspace_pool_active_count` (gauge).

These can be emitted via the existing diagnostics surface or a
follow-up Application Insights wiring (spec 008). Either way, they
ship with the Option A prototype.

### Files touched (Option A prototype)

- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` — refactored
  dispatcher + pool, or
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1` — new
  variant selectable via configuration so we can A/B without losing
  the current host. Recommendation: ship as a new file behind a
  `SubprocessHostMode: "Single" | "Pool"` config flag. Keeps the
  existing host as a known-good baseline during benchmarking.
- `OutOfProcessCommandExecutor.cs` — minor: select host script;
  preserve `_sendLock` (it protects stdin writes from .NET-side
  concurrency, which we still want regardless of host parallelism).
  Possibly add executor-level gating for `setup` vs `invoke` (see
  quiesce protocol above).

---

## 3. Option B — Pool of pwsh Processes

### Design overview

Keep `oop-host.ps1` as it is. Spin up `N` subprocesses managed by
.NET. Dispatch each `invoke` to whichever subprocess is free.
Discovery happens once, against any one subprocess (schemas are
identical — see "discovery cache" note below). Setup runs against
every subprocess at startup.

### Concrete shape

A new `OutOfProcessSubprocessPool` class wraps `N` instances of
`OutOfProcessCommandExecutor` (or a refactored `OutOfProcessHost`
that holds the per-process state). The public surface
(`ICommandExecutor`) stays the same.

**Dispatch strategy:**

- Maintain a `Channel<OutOfProcessHost>` of available hosts **plus a
  `ConcurrentDictionary<int, HostState>` keyed by process id**. The
  dictionary is the source of truth for "what hosts exist"; the
  channel is the lease queue. The channel alone is insufficient —
  a host that crashes mid-lease is not in the channel and a naive
  implementation has no way to reconcile pool size.
- `InvokeAsync` does `await channel.Reader.ReadAsync()` to lease,
  marks the host as `Leased` in the dictionary, runs the request,
  and returns the host to the channel in `finally` (only if the host
  is still alive — see crash handling below).

### Lifecycle

- **Startup**: launch hosts in order. Run `ping` + `setup` against
  the first host synchronously — if that fails, fail-fast (this is
  the meaningful smoke test, equivalent to current single-host
  behavior). For hosts 2..N, run `ping` + `setup` with bounded
  retries and exponential backoff. A transient `Install-Module`
  failure on host 5 of 8 should not take down the server; the pool
  starts degraded and logs a warning. Minimum healthy size below
  which startup fails: configurable, default `max(1, N/2)`.
- **Crash mid-lease**: `Process.Exited` fires for a host that is
  currently `Leased`.
  1. Mark the host `Dead` in the dictionary (do **not** add it back
     to the channel).
  2. The pending `InvokeAsync` for that host completes with an error
     (the read loop or the request `TaskCompletionSource` already
     surfaces this when the stream closes).
  3. Spawn a replacement host. Run `ping` + `setup`.
  4. Only after `setup` succeeds, add the replacement to the channel
     and to the dictionary, and remove the dead entry.
  5. Pool size is reconciled from the dictionary, never from the
     channel. There is no "N+1 / N-1 silently" failure mode.
- **Crash while idle**: same as above without step 2.
- **Shutdown**: `shutdown` to each, wait for graceful exit, kill
  stragglers. Same logic per host as today.

### Discovery cache

Discovery runs once (against any one host) and the resulting schema
is cached on the .NET side, **keyed off the pool, not any individual
host**. When a host restarts, its setup re-imports the same modules
(deterministic from configuration), so the cached schema remains
valid. No per-host re-discovery is needed in steady state. Worth
calling out so the next reader doesn't assume otherwise.

### Queue semantics and fairness

The channel is FIFO, so requests are dispatched in arrival order
to the first free host. A noisy long-running request will tie up
its host but cannot starve others — different from a serialized
single host.

**Per-request timeouts** stay at the host level; if a host stops
responding, the existing kill-and-restart logic kicks it out of
the channel and replaces it. We do not need a separate pool-level
timeout in the first prototype.

**Operability bonus (per-request kill).** In Option B, killing a
single host on a per-request timeout no longer takes down the
server's only PowerShell — it kills 1 of N. The current
"kill subprocess on timeout" behavior is structurally safer in B
than today, even before proper cancellation propagation lands as a
separate work item.

### Config knobs

```jsonc
{
  "PowerShellConfiguration": {
    "RuntimeMode": "OutOfProcess",
    "SubprocessHostMode": "ProcessPool",
    "SubprocessPoolSize": 4,
    "SubprocessTimeoutSeconds": 30,
    "SubprocessMaxRestarts": 3,
    "SubprocessMinHealthyForStartup": 2
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
  new, implements `ICommandExecutor` over a `Channel<OutOfProcessHost>`
  + `ConcurrentDictionary<int, HostState>`.
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

**Baseline capture.** The baseline is measured by the same harness in
the same run as A and B (same scenario list, same machine, same
session). We do not rely on a pre-captured baseline. This eliminates
machine-and-version drift.

**Application Insights / observability disabled.** The harness runs
with AI / spec-008 logging disabled (`ApplicationInsights:Enabled =
false` or equivalent) to keep latency numbers clean. AI-enabled
numbers can be a follow-up scenario but should not pollute the
core comparison.

### Scenarios

| Scenario | Cmdlet / script | What it stresses |
|----------|-----------------|------------------|
| **CPU-light, fast** | `Get-Date` (or a `1+1` script block) | Pure protocol + dispatch overhead |
| **CPU-bound** | `1..100000 \| ForEach-Object { $_ * $_ } \| Measure-Object -Sum` | Parallelism limit (CPU) |
| **I/O-bound (sleep)** | `Start-Sleep -Seconds 2` | Idle parallelism — should scale ~linearly with N for A and B |
| **Network-shaped** | `Invoke-WebRequest http://127.0.0.1:<ephemeral>` against an in-test `HttpListener` bound to `127.0.0.1:0` with 500 ms latency | Realistic Az/Graph workload |
| **Heavy serialization** | `Get-Process \| Select-Object -First {10,100,1000}` (parameterized) | Output normalization cost — varies payload to expose B's IPC overhead and A's `ConvertTo-Json` contention |
| **Cold-start cost** | Time from process launch → first `invoke` returning | Startup latency — favors A (one process) on subsequent scaling, hurts B |
| **Process crash recovery (Option B gate)** | Trigger `[System.Environment]::Exit(1)` mid-stream in one host, then issue another invoke | Recovery latency — measures B's per-host crash isolation. Same as baseline for A (single process); not a useful A discriminator |
| **Runspace-level corruption (Option A gate)** | One invoke pollutes a type accelerator / mutates a shared static; a parallel invoke verifies it is unaffected | The real isolation gate for A — can A confine corruption between runspaces? |
| **Cross-invocation state leak** | Tool that mutates `$global:` state, then a second tool that reads it | Confirms whether Option A leaks state across runspaces (it shouldn't, but verify) |

`HttpListener` portability note: bind to `127.0.0.1:0`. This avoids
the URL ACL requirement that Windows imposes on non-loopback
bindings and works on .NET 10 across platforms.

### Metrics

Per scenario, record:

- **Latency** — p50, p95, p99 over a fixed request count (e.g. 500).
- **Throughput** — requests/second under sustained concurrent load
  (10, 50, 100 concurrent clients).
- **Memory** — peak working set of the .NET host *and* the pwsh
  process(es) sampled every second during the run.
- **Startup time** — wall-clock from `StartAsync` to first successful
  `invoke`.
- **Recovery time** — wall-clock from induced crash (process or
  runspace-level) to next successful `invoke`.
- **(Option A only)** Pool queue depth and lease wait time, sourced
  from the metrics emitted by the prototype.

### Harness shape

A new `PoshMcp.Benchmarks` console project (not a test project —
benchmark runs are too long for CI):

- BenchmarkDotNet for the latency/throughput scenarios.
- A small custom harness for crash/recovery and corruption tests
  (BenchmarkDotNet is awkward for "kill the process mid-run" or
  "mutate then probe" tests).
- Runs against a real `OutOfProcessCommandExecutor` /
  `OutOfProcessSubprocessPool` configured in-process. No MCP client
  in the loop — we measure the executor, not the JSON-RPC stack.
- Outputs Markdown tables to `specs/004-out-of-process-execution/benchmark-results/`.

For the network-shaped scenario, the harness spins up an
`HttpListener` on `127.0.0.1:0` that responds after a configurable
delay. Self-contained, no external dependency.

### Pass/fail criteria for the recommendation

**Per-scenario thresholds** (a single 4× bar across all scenarios is
unachievable on CPU-bound work and would reject a winning design on
a scenario that isn't its job):

| Scenario class | Threshold vs baseline (10 concurrent clients) |
|----------------|-----------------------------------------------|
| I/O-bound, network-shaped | Throughput ≥ 4× baseline |
| Heavy serialization (large payloads) | Throughput ≥ 2× baseline |
| CPU-bound | Throughput ≥ 1.5× baseline |
| CPU-light | p95 latency within 1.5× baseline (parity, not speedup) |

Plus the isolation criterion (both options must meet at least one):

- **Option A passes isolation** if the runspace-level corruption
  scenario shows the parallel invoke is unaffected.
- **Option B passes isolation** if process crash of one host produces
  no server-visible error on requests routed elsewhere within
  100 ms of the crash.

If both options meet their respective isolation gate, prefer A when
it is materially cheaper on memory and startup and clears the
throughput bars; prefer B otherwise. If A fails the isolation gate,
default to B regardless of throughput.

---

## 5. Recommended Phasing (Follow-up Issues)

These should be filed as separate issues, blocked on this plan being
reviewed.

0. **#TBD — Bug-fix: clear `$Error` before invoke in single-runspace
   host.** Independent of the experiment outcome. One-line fix to
   `Invoke-InvokeHandler` plus a regression test that issues two
   invokes where the first fails and asserts the second's
   `hadErrors` is `false`.

1. **#TBD — Extract `OutOfProcessHost`**. Refactor
   `OutOfProcessCommandExecutor` so the per-process state
   (process, streams, send lock, pending map, read loops) lives in a
   reusable `OutOfProcessHost` type. No behavior change. Existing OOP
   integration tests must pass *and* a new unit-level test for
   `OutOfProcessHost` lifecycle (start → ping → setup → shutdown →
   restart) ships with this issue. "No behavior change" without test
   coverage at the seam silently degrades. Unblocks both prototypes.

2. **#TBD — Option A prototype: runspace pool host**. Add
   `oop-host-pool.ps1` and a `SubprocessHostMode: "Pool"` config
   flag. Implement the synchronized writer, custom `PSHost` for
   stream isolation, ISS-based pre-warm, per-runspace `$Error`
   handling, the quiesce protocol for `setup`, and the queue-depth
   / lease-wait metrics. Existing integration tests must pass
   against both modes.

3. **#TBD — Option B prototype: process pool executor**. Add
   `OutOfProcessSubprocessPool` and a `SubprocessHostMode:
   "ProcessPool"` config flag with `SubprocessPoolSize`. Use
   `Channel<OutOfProcessHost>` + `ConcurrentDictionary<int,
   HostState>` so crash-mid-lease is reconciled correctly.
   First-host setup is fail-fast; hosts 2..N retry with backoff and
   may degrade to `SubprocessMinHealthyForStartup`. Existing
   integration tests must pass against the pool with size 1, 2, and 4.

4a. **#TBD — Benchmark harness infrastructure**. New
    `PoshMcp.Benchmarks` project with `HttpListener` test server,
    BenchmarkDotNet config, scenario stubs, and Markdown output
    plumbing. Can run in parallel with #2 and #3 because it does
    not depend on the prototype implementations — only on the
    `ICommandExecutor` interface.

4b. **#TBD — Wire harness to executors**. Connect the harness from
    #4a to `OutOfProcessCommandExecutor`, the runspace-pool host,
    and `OutOfProcessSubprocessPool`. Blocked on #2 and #3.

5. **#TBD — Run benchmarks and write findings**. Three runs per
   scenario per mode, results table committed alongside a written
   analysis.

6. **#TBD — Adopt the winner**. Make the recommended mode the
   default; demote the loser to opt-in. **`SubprocessHostMode:
   "Single"` MUST remain a supported fallback for at least one full
   release** — removing the loser entirely on the same PR that
   adopts the winner is too aggressive given how new this work is.
   Update spec 004 (including a note that if Option A wins, the
   "subprocess isolation protects the server" contract from US2
   weakens at the runspace level) and `DOCKER.md`.

The order matters: #0 is independent and can land any time. #1
unblocks #2 and #3, which can run in parallel. #4a runs in parallel
with #2 and #3. #4b blocks on #2 and #3. #5 blocks on #4b. #6
blocks on #5.

---

## 6. Open Questions for Reviewer

1. **Default `N` for the pool.** `Environment.ProcessorCount` is the
   obvious choice but for I/O-bound Az workloads we may want
   higher. Do we ship a sensible default and let users tune, or pick
   a fixed small number (4) until we have telemetry?
2. **Should Option A ship at all if Option B wins?** Maintaining two
   host scripts is real cost. If B is the clear winner, I'd remove
   the pool host rather than carry it as a configurable mode — but
   not on the same PR that adopts B (see #6 above).
3. **Network-shaped scenario fidelity.** Local `HttpListener` is
   sufficient for the prototype gate. Real-Azure validation belongs
   in a follow-up acceptance test, not the bench harness — it should
   not block the comparison on tenant access.
4. **Cancellation propagation.** Today, `cancellationToken` cancels
   the .NET-side wait but does not cancel the in-flight pwsh work.
   Worth fixing as part of this work, or hold for a separate issue?
   (My vote: separate issue — out of scope. Note that in Option B,
   per-request kill-on-timeout is already structurally safer than
   today even without proper cancellation.)

---

## 7. Revision History

- **2026-05-06 (initial)** — First draft of plan: Option A
  (runspace pool), Option B (process pool), benchmark harness,
  six follow-up issues.
- **2026-05-06 (revision)** — Incorporated review feedback from
  Cubert (fact-check) and Farnsworth (architectural review):
  - Dropped the "Phases 1–4 complete" framing; described shipped
    capabilities directly. Added path-drift footnote for Issue #65.
  - Promoted the pre-existing single-runspace `$Error`
    non-clearing bug to its own follow-up issue (#0) and noted it
    in §1 explicitly.
  - **Option A:** corrected the `[Console]::Out` redirection claim
    (process-global, cannot scope per runspace) — replaced with a
    custom `PSHost` / `PSHostUserInterface` mechanism. Specified
    the quiesce protocol for `setup` so it cannot race with
    in-flight invokes. Added per-pipeline stream guidance
    (`PowerShell.Streams.*`) beyond just `$Error`. Required
    queue-depth / lease-wait metrics in the prototype.
  - **Option B:** added `ConcurrentDictionary<int, HostState>`
    alongside the channel to reconcile crash-mid-lease. Switched
    from blanket fail-fast to "first host fail-fast, hosts 2..N
    retry with backoff" with `SubprocessMinHealthyForStartup`.
    Documented the discovery-cache key and the per-request-kill
    operability bonus.
  - **Benchmark harness:** specified that baseline is captured in
    the same run; pinned the `127.0.0.1:0` `HttpListener` binding;
    split crash-recovery into a process-crash test (B's gate) and
    a runspace-corruption test (A's gate); replaced the single 4×
    bar with per-scenario thresholds; parameterized payload size
    on the serialization scenario; required AI/spec-008 logging
    disabled.
  - **Phasing:** added #0 (`$Error` bug-fix); split #4 into 4a
    (infrastructure, parallel) and 4b (wiring, blocked); required
    a unit-level lifecycle test on #1; required `Single` mode to
    survive at least one full release after adoption (#6).
