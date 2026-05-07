# Cancellation Propagation Design (issue #188)

**Status:** Proposed
**Owner:** Bender (Backend Developer)
**Date:** 2026-05-06
**Scope:** Out-of-process execution path only. In-process runspace cancellation is tracked separately and is NOT addressed here.
**Related:** Spec 004 (Out-of-Process Execution), unblocks #196 (default-mode flip to `Pool`).

---

## 1. Problem statement

`CancellationToken` plumbed through `OutOfProcessHost.SendRequestAsync` only governs the .NET-side wait. When the token fires:

1. `_sendLock.WaitAsync(ct)` and the stdin write may abort (only relevant before the request is in flight).
2. The linked `timeoutCts` may fail the local `TaskCompletionSource` with `TimeoutException` if the per-request timeout elapses.
3. **The pwsh subprocess never learns about the cancellation.** It keeps running the pipeline to completion (or hangs).

Net effect:

- The `_pending` map entry is removed in `finally` only after `tcs.Task` observes a result. Caller cancellation does not currently set the TCS — the awaiter just stays parked. Since there is no `ct.Register` hook today, **caller cancellation has effectively no effect on a request that is already in flight** beyond what the per-request timeout produces.
- The host's single-threaded stdin dispatcher (`oop-host.ps1`) cannot even *read* a follow-up cancel message while it is blocked inside `Invoke-InvokeHandler`. Head-of-line blocking is structural.
- For `Pool` mode (`oop-host-pool.ps1`), invokes run on a runspace pool with N worker threads, so the dispatcher loop is not blocked — but there is no plumbing to map `requestId → PowerShell` and call `BeginStop()`.
- For `ProcessPool` mode (`OutOfProcessSubprocessPool`), per-request kill-on-timeout already exists as a backstop, but it kills the host process even when a soft cancel would have sufficed.

Until cancellation is plumbed through to the host pipeline, `Pool` cannot become the default mode (#196): a single runaway script wastes the only host until the per-request timeout kills it, blocking every subsequent invoke for that duration.

---

## 2. Mode-by-mode analysis

### 2.1 `Single` (`oop-host.ps1`) — one host process, one runspace, one pipeline at a time

- **Dispatcher loop** is single-threaded synchronous: `[Console]::ReadLine()` → `switch ($method)` → `Invoke-InvokeHandler` → blocking `& $cmdInfo @boundParams`.
- The handler holds the dispatcher hostage for the full duration of the user pipeline. No other request — including a `cancel` for the in-flight invoke — can be read.
- **Cancellation requires:**
  1. Decoupling invoke execution from the dispatcher loop. The invoke must run on a background `[powershell]` instance via `BeginInvoke()`. The dispatcher continues reading stdin in parallel.
  2. A registry of `requestId → [powershell]` (`ConcurrentDictionary[string,powershell]`) so a `cancel` request can find the live pipeline.
  3. On `cancel`, call `$ps.BeginStop($null, $null)`. PowerShell will eventually unwind the pipeline; the existing completion callback writes the response (with the `cancelled` flag set) and frees the registry entry.

### 2.2 `Pool` (`oop-host-pool.ps1`) — one host process, N runspaces, N concurrent pipelines

- Invokes already run on the C# `PoolDispatcher`'s N worker threads. The PS dispatcher loop is free; it can read `cancel` immediately.
- The `PoolDispatcher` already creates a `[powershell]` per `PoolWorkItem` and calls `ps.Invoke()` synchronously on the worker thread. To cancel, we need the worker to be interruptable from outside.
- **Cancellation requires:**
  1. Track active items by id: `ConcurrentDictionary<string, PoolWorkItem> _active` inside `PoolDispatcher`. Populate on `Submit`, remove in the worker `finally`.
  2. Add `PoolDispatcher.Cancel(string requestId)` that looks up the item and calls `item.Ps.BeginStop(null, null)`. The worker's `ps.Invoke()` returns shortly thereafter; existing response-write path runs unchanged. The response includes a `cancelled` flag.
  3. Add an `Invoke-CancelHandler` PS function that calls `$script:Dispatcher.Cancel($requestId)` and emits a small ack frame.
- All response writes are already serialized through `PoolStdout.Lock`, so no new locking is needed.

### 2.3 `ProcessPool` (`OutOfProcessSubprocessPool`) — N independent host processes, one pipeline each

- The pool already kills a host on `TimeoutException` from `host.SendRequestAsync` (the per-request kill-on-timeout escape hatch from #200). That remains the backstop.
- The **new behavior** is purely free of charge: by improving `OutOfProcessHost` to forward caller cancellation to the host as a `cancel` frame, the pool's `InvokeAsync` automatically gets soft-cancel propagation. Each leased host's script (which is `oop-host.ps1` in `ProcessPool` mode — the pool currently uses Single-mode hosts internally) handles the cancel via the new Single-mode path.
- The kill-on-timeout fallback is preserved exactly as today. Sequence becomes:
  1. Caller cancels token → `OutOfProcessHost` sends `cancel` frame → host script attempts `BeginStop()`.
  2. .NET awaiter completes with `OperationCanceledException` immediately (we do not wait for the host to acknowledge — see §3).
  3. If the host acks/responds normally, the slot stays healthy and is returned to the pool.
  4. If the host wedges (cmdlet stuck in unmanaged code), the *next* invoke on this slot will time out and trigger the existing kill path.

---

## 3. Wire protocol additions

A single new method, `cancel`, plus a single new optional field, `cancelled`, on existing response frames.

### 3.1 `cancel` request (`.NET → host`)

```json
{"id":"<cancel-frame-id>","method":"cancel","params":{"requestId":"<original-invoke-id>"}}
```

- `cancel-frame-id` is a fresh GUID generated by `OutOfProcessHost`. It is **not** registered in `_pending` — we do not await the cancel ack.
- The host **must** still respond to the cancel frame so its own dispatcher does not log "missing response". The ack is small:

```json
{"id":"<cancel-frame-id>","result":{"cancelled":true,"requestId":"<original-invoke-id>"}}
```

- `cancelled` in the ack is `true` if the host found the in-flight pipeline and signaled it; `false` if the request id was unknown (already completed, or wrong id). The .NET side ignores the ack body.

### 3.2 Cancelled invoke response (`host → .NET`)

When the original `invoke` unwinds because of a `BeginStop`, the host writes the *normal* response frame with an additional `cancelled` boolean:

```json
{"id":"<original-invoke-id>","result":{"output":"null","hadErrors":true,"cancelled":true,"errors":["The pipeline has been stopped."],"warnings":[]}}
```

The .NET side does not consume `cancelled` on the response — by then the awaiter has already observed `OperationCanceledException` and the `_pending` entry has been removed (the response is dropped with a debug log). The flag exists for log inspection and future analytics.

### 3.3 Frame ordering

- A `cancel` may arrive *after* the host has already finished the invoke and written its response. The host MUST ack with `cancelled:false` and otherwise no-op.
- A `cancel` for an unknown request id MUST ack with `cancelled:false` and not error.
- A `cancel` MUST NOT block the host dispatcher under any circumstances.

---

## 4. Implementation plan — .NET side

### 4.1 `OutOfProcessHost.SendRequestAsync` (the central change)

Add a cancellation-token registration that fires a cancel frame and trips the TCS with `OperationCanceledException`:

```csharp
var cancelRegistration = cancellationToken.Register(() =>
{
    // Best-effort: tell the host to stop. Do not await — caller wants to return now.
    _ = TrySendCancelFrameAsync(id);
    tcs.TrySetCanceled(cancellationToken);
});
```

The existing `timeoutCts.Token.Register(...)` callback (per-request timeout) gets the same treatment: send cancel frame, then set `TimeoutException`. Both registrations dispose in `finally`.

`TrySendCancelFrameAsync` is a small private helper:

```csharp
private async Task TrySendCancelFrameAsync(string requestId)
{
    if (_disposed || _stdin is null) return;
    var cancelFrameId = "cancel-" + Guid.NewGuid().ToString("N");
    var frame = new { id = cancelFrameId, method = "cancel", @params = new { requestId } };
    var json = JsonSerializer.Serialize(frame);
    try
    {
        // Use a short, independent CTS. We must not honor the just-cancelled token here.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
        try
        {
            await _stdin.WriteLineAsync(json.AsMemory(), cts.Token).ConfigureAwait(false);
            await _stdin.FlushAsync(cts.Token).ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "Failed to send cancel frame for request {Id}.", requestId);
    }
}
```

Cancel frame ids are intentionally **not** registered in `_pending`. The host's ack will hit the existing "OOP response for unknown request id" warning path; downgrade that path to debug for ids prefixed with `cancel-` to keep logs clean.

### 4.2 Read loop — drop responses for already-removed pending ids quietly

When the host writes the eventual `cancelled:true` response for an invoke that the .NET side already abandoned (`_pending.TryRemove` in `finally`), the read loop currently logs `"OOP response for unknown request id"`. Downgrade to `Debug` when the response carries `"cancelled":true` to avoid log noise.

### 4.3 `OutOfProcessSubprocessPool.InvokeAsync`

No structural change. The existing `catch (TimeoutException)` and `catch (InvalidOperationException) when (!host.IsRunning)` paths remain. We add **one** additional catch for caller cancellation:

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // Host has been signaled (cancel frame sent inside SendRequestAsync). Slot stays
    // healthy unless the host fails to recover; if it wedges, the next invoke will
    // time out and trigger the existing kill path. Do NOT mark the slot broken here.
    _logger.LogDebug(
        "Invoke '{CommandName}' on slot {Index} cancelled by caller.",
        commandName, slotIndex);
    throw;
}
```

This catch is purely for clean diagnostics; without it the OCE bubbles up unannotated, which is correct but harder to debug.

### 4.4 `OutOfProcessCommandExecutor.InvokeAsync` (Single mode)

No code change required. The cancel-frame propagation is entirely inside `OutOfProcessHost`. The Single executor inherits it for free.

---

## 5. Implementation plan — PowerShell side

### 5.1 `oop-host.ps1` (Single mode) — async invoke + active registry

The single-threaded dispatcher loop is the structural blocker. Refactor `Invoke-InvokeHandler` to be non-blocking:

```powershell
# Module-level registry, populated on invoke, cleared on completion.
$script:ActiveInvocations = [System.Collections.Concurrent.ConcurrentDictionary[string,powershell]]::new()
$script:ActiveRunspace = [runspacefactory]::CreateRunspace()
$script:ActiveRunspace.Open()

function Invoke-InvokeHandler {
    param([string]$Id, [object]$Params)

    $commandName = $Params.command
    if ([string]::IsNullOrWhiteSpace($commandName)) {
        Write-NdjsonResponse -Id $Id -ErrorObj @{ code = -1; message = 'Missing required parameter: command' }
        return
    }

    # ... build $splatParams, resolve switch parameters (existing logic) ...

    $ps = [powershell]::Create()
    $ps.Runspace = $script:ActiveRunspace
    [void]$ps.AddScript({
        param($Name, $Splat)
        $Error.Clear()
        $r = & $Name @Splat
        if ($null -eq $r) { return ,@($null, $Error.Count -gt 0) }
        $json = ConvertTo-SafeJson -InputObject $r -Depth 4
        ,@($json, $Error.Count -gt 0)
    })
    [void]$ps.AddArgument($commandName)
    [void]$ps.AddArgument($splatParams)

    [void]$script:ActiveInvocations.TryAdd($Id, $ps)

    $async = $ps.BeginInvoke()
    $callbackState = @{ Id = $Id; Ps = $ps; Async = $async }

    # Completion observer thread (or .NET timer) — see note below.
    [void][System.Threading.ThreadPool]::QueueUserWorkItem({
        param($state)
        try {
            $state.Async.AsyncWaitHandle.WaitOne()
            $output = $state.Ps.EndInvoke($state.Async)
            # ... build response (output JSON, hadErrors, cancelled, errors, warnings) ...
            Write-NdjsonResponse -Id $state.Id -Result $resultObj
        }
        catch [System.Management.Automation.PipelineStoppedException] {
            Write-NdjsonResponse -Id $state.Id -Result @{
                output = 'null'; hadErrors = $true; cancelled = $true
                errors = @('Pipeline stopped.'); warnings = @()
            }
        }
        catch {
            Write-NdjsonResponse -Id $state.Id -ErrorObj @{ code = -1; message = "$_" }
        }
        finally {
            [void]$script:ActiveInvocations.TryRemove($state.Id, [ref]([powershell]::null))
            try { $state.Ps.Dispose() } catch { }
        }
    }, $callbackState)
}

function Invoke-CancelHandler {
    param([string]$Id, [object]$Params)
    $requestId = "$($Params.requestId)"
    $found = $false
    if ($script:ActiveInvocations.ContainsKey($requestId)) {
        $ps = $null
        if ($script:ActiveInvocations.TryGetValue($requestId, [ref]$ps)) {
            try { [void]$ps.BeginStop($null, $null); $found = $true }
            catch { Write-Diag "BeginStop failed for $requestId : $_" }
        }
    }
    Write-NdjsonResponse -Id $Id -Result @{ cancelled = $found; requestId = $requestId }
}
```

**Note on the completion observer:** `BeginInvoke` accepts a `PSAsyncCallback` overload, but it executes back on the runspace thread which we want free for stop processing. A `ThreadPool.QueueUserWorkItem` that waits on `Async.AsyncWaitHandle` is a robust portable alternative; one OS thread per in-flight invoke in Single mode is acceptable (Single mode = max 1 in flight in normal operation; cancel allows brief overlap during stop unwind).

`Write-NdjsonResponse` already serializes via `[Console]::Out.WriteLine` — wrap stdout writes in a `[object]` lock (`$script:StdoutLock`) to prevent interleaving when both the callback thread and the dispatcher thread (e.g., for cancel ack) write concurrently. Pool host already has `PoolStdout.Lock`; mirror the pattern.

### 5.2 `oop-host-pool.ps1` (Pool mode) — track active dispatcher items

Two surgical changes inside `PoolDispatcher`:

1. **Track active items.** Add `private readonly ConcurrentDictionary<string, PoolWorkItem> _active = new();`. In `Submit`, do `_active.TryAdd(id, item)` *before* `_queue.Add(item)`. In `WorkerLoop`'s `finally`, do `_active.TryRemove(w.Id, out _)`.

2. **Expose `Cancel`.**

```csharp
public bool Cancel(string requestId)
{
    if (_active.TryGetValue(requestId, out var item))
    {
        try { item.Ps.BeginStop(null, null); return true; }
        catch { return false; }
    }
    return false;
}
```

In `ProcessOne`, detect a cancelled invoke via `w.Ps.InvocationStateInfo.State == PSInvocationState.Stopped` and emit `cancelled:true` in the response object (the existing JSON builder gets one extra field). This is one-line additive.

PS function:

```powershell
function Invoke-CancelHandler {
    param([string]$Id, [object]$Params)
    $requestId = "$($Params.requestId)"
    $found = $false
    if ($null -ne $script:Dispatcher) {
        $found = $script:Dispatcher.Cancel($requestId)
    }
    Write-NdjsonResponse -Id $Id -Result @{ cancelled = $found; requestId = $requestId }
}
```

Add `'cancel' { Invoke-CancelHandler -Id $id -Params $params }` to the main loop switch.

### 5.3 `ProcessPool` host scripts

Each `OutOfProcessHost` inside `OutOfProcessSubprocessPool` runs `oop-host.ps1` (Single mode). The Single-mode changes in §5.1 cover ProcessPool by default. No additional script work.

---

## 6. Test plan

Three integration tests, one per mode. Pattern follows `OutOfProcessHostConcurrencyTests.cs`:

### 6.1 `Single_LongRunningInvoke_TokenCancelStopsPipeline`

1. Start `OutOfProcessHost` directly with `oop-host.ps1`.
2. Issue `invoke` for `Start-Sleep -Seconds 60` with a `CancellationTokenSource`.
3. Cancel the CTS after 200 ms.
4. Assert the awaiter throws `OperationCanceledException` within 5 s (not 60 s).
5. Issue a follow-up `ping` with a fresh token; assert it succeeds within 5 s — proves the host is still healthy.

### 6.2 `Pool_LongRunningInvoke_TokenCancelStopsPipeline`

1. Start `OutOfProcessHost` directly with `oop-host-pool.ps1` (or via `OutOfProcessCommandExecutor` with `SubprocessHostMode.Pool`).
2. Issue *two* parallel invokes: one `Start-Sleep -Seconds 60`, one `Get-Date`.
3. Cancel only the first invoke's CTS.
4. Assert the first throws `OperationCanceledException` within 5 s.
5. Assert the second completes successfully (proves no head-of-line blocking; proves the pool is still serving).

### 6.3 `ProcessPool_LongRunningInvoke_TokenCancelStopsHost`

1. Construct an `OutOfProcessSubprocessPool` with `PoolSize = 2`.
2. Issue two parallel invokes (both `Start-Sleep -Seconds 60`).
3. Cancel both CTSs.
4. Assert both throw `OperationCanceledException` within 5 s.
5. Assert `pool.HealthyCount == 2` after a short settle (slots stay healthy; soft-cancel did not require kill).

For the wedged-pipeline path (cancel-times-out → kill), rely on the existing `Pool_PerRequestTimeout_KillsHostAndPoolRecovers` test which already covers the kill-and-recover behavior. This is intentional: §4.3 explicitly preserves that path; we are not regressing it.

---

## 7. Risks and open questions

| Risk | Mitigation |
|------|------------|
| Pipeline is in unmanaged code (P/Invoke, native cmdlet) and ignores `BeginStop`. | The .NET awaiter still returns OCE promptly. The host pipeline keeps running until it exits naturally. For `ProcessPool`, the next invoke on this slot will trigger the existing kill-on-timeout path. For `Single`/`Pool`, the per-request timeout (still in place) kills the host as a backstop — degraded but bounded. Documented in XML doc on `cancellationToken` parameter. |
| Race: `cancel` arrives between worker's `_active.TryRemove` and the response write. | Order matters. Worker writes response *before* `_active.TryRemove`. Late cancel finds nothing in `_active`, acks `cancelled:false`. Benign. |
| Single-mode async refactor introduces concurrent stdout writes (callback thread + dispatcher writing cancel ack). | Wrap `Write-NdjsonResponse` writes in `[Console]::Out` with a process-wide lock. Mirror Pool's `PoolStdout.Lock` pattern. |
| `OutOfProcessHost.SendRequestAsync` calling `_sendLock.WaitAsync` from the cancel-fire-and-forget path could deadlock if invoked synchronously from a continuation that holds the lock. | The cancel send runs via `Task.Run` (or fire-and-forget `_ = ...`), never on the cancel-token-callback thread synchronously. The 2-second CTS on the cancel send guarantees forward progress. |
| Cancel ack for already-completed invoke writes a noisy "OOP response for unknown request id" log. | Cancel ack frame ids are prefixed `cancel-`; the read loop downgrades unknown-id warnings to debug for that prefix. |
| In Single mode, the runspace is reused across invokes; a stopped pipeline may leave the runspace in a transient `Stopping` state when the next invoke arrives. | `[powershell]::Create().Runspace = $script:ActiveRunspace` does not require `Opened` state for the next pipeline as long as the previous `[powershell]` was disposed (which the completion callback does). If we observe issues in tests, switch to a per-invoke `[powershell]::Create()` without an explicit shared runspace (uses default), at the cost of cold runspace creation per invoke. |

---

## 8. Decision

**Adopt the design described above.** Specifically:

1. New wire method `cancel` with optional `cancelled` field on responses.
2. `.NET`-side: `OutOfProcessHost.SendRequestAsync` registers a cancellation callback that fires a cancel frame and trips the TCS with `OperationCanceledException`. Per-request timeout path also fires the cancel frame.
3. `Single` mode: refactor invoke handler to async `BeginInvoke` + active-invocation registry, with stdout serialized under a process-wide lock.
4. `Pool` mode: extend `PoolDispatcher` with an active-item registry and a `Cancel(requestId)` method that calls `BeginStop`.
5. `ProcessPool` mode: inherits cancel propagation from `OutOfProcessHost`; existing kill-on-timeout backstop preserved verbatim.
6. Tests: one per mode covering soft-cancel-then-recover.

**Why this design over alternatives:**

- *Side-channel control protocol (named pipe, signal):* rejected. Adds an OS-specific surface (named pipes differ between Windows and Linux; signals don't exist on Windows in a useful form), and the existing stdin channel is not actually blocked in `Pool` mode and can be unblocked in `Single` mode by simply running invokes asynchronously. The async refactor is cheaper than introducing a second IPC channel.
- *Always-kill-host on cancel:* rejected. Loses module/runspace state on every cancel, defeating the purpose of `Pool` mode. The kill path remains as a backstop, not the primary mechanism.
- *Cooperative-only (no kill):* rejected. Wedged pipelines (unmanaged code) need a backstop. The existing per-request kill in `ProcessPool` already provides it; the new design preserves it.
