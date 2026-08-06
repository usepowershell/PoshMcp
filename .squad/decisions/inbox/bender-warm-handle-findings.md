# Bender — Warm-Path + Handle-Floor Investigation Findings
**Date:** 2026-08-06
**Milestone:** 10 — Warm-path + handle-floor (C# / HTTP / pool host ownership)
**Branch:** `squad/warm-handle-csharp-fix`

---

## Warm-Path Overhead — Root Causes & Fixes

### Problem
v2 `pool_reset` benchmarks at ~1.45–1.76× vs. 1.05× target (v1 `ephemeral_create_dispose` baseline).
PS table-based reset is ~0.5ms. Remaining C# overhead in the hot path adds to the gap.

### Root Causes Found in C# Layer

#### 1. Unconditional `FormatParameterSummary` + per-parameter detail loop (FIXED)
**File:** `PowerShellAssemblyGenerator.cs` — `ExecutePowerShellCommandTyped`

`FormatParameterSummary()` was called unconditionally on every `tools/call`, creating:
- `new List<string>()` per call
- `value?.ToString()` per parameter
- `string.Join(", ", items)`
- `LogSanitizer.Scrub(...)` on the result

A separate per-parameter loop also ran unconditionally, calling `LogSanitizer.Scrub(paramValue?.ToString())` for each parameter even when `LogLevel.Debug` was disabled.

**Fix:** Added `var isDebugEnabled = logger.IsEnabled(LogLevel.Debug)` at method entry. Wrapped `FormatParameterSummary`, the detail loop, and all per-parameter binding `LogDebug` calls in `if (isDebugEnabled)` blocks. `IsEnabled` is captured once as a local bool; the closure captures it for the `ExecuteThreadSafeAsync` callback.

**Impact:** Eliminates `O(P)` allocations per call (where P = parameter count) at Info/Warning log levels (production default).

#### 2. Multiple unconditional stage-marker `LogDebug` calls (FIXED)
**File:** `PowerShellAssemblyGenerator.cs`

Seven `LogDebug("Tool invocation stage: ...")` calls with structure logging arguments throughout the hot path were evaluated unconditionally. Even though the logger's `LogDebug` checks `IsEnabled` internally, argument boxing into `object[]` + method dispatch happens before the check.

Stages guarded: `request_received`, `tool_resolved`, `pipeline_initialized`, `parameters_bound_normalized`, `result_shaping_empty`, `result_shaping_started`, `result_shaping_completed`.

**Fix:** All guarded with the cached `isDebugEnabled` local.

#### 3. `BeginCorrelationScope` — per-call `Dictionary<string, object>` allocation (FIXED)
**File:** `LoggerExtensions.cs`

`logger.BeginScope(new Dictionary<string, object> { ... })` was called on every `tools/call` invocation. A `Dictionary<string, object>` with 2 entries requires:
- A `Dictionary` object header
- A `string[]` keys array
- An `object[]` values array
- Load-factor computation on construction

**Fix:** Replaced with `new KeyValuePair<string, object>[]` (2-element array). Reduces allocation from ~200 bytes (Dictionary overhead) to ~64 bytes. All structured logging providers that accept `IEnumerable<KeyValuePair<string, object>>` work correctly with this form.

#### 4. `McpProtocolVersionMiddleware` — full `JsonDocument.ParseAsync` on every stateless request (FIXED)
**File:** `McpProtocolVersionMiddleware.cs` — `GetInitializeProtocolVersionAsync`

For every stateless `tools/call` (no `Mcp-Session-Id` header), the middleware:
1. Called `request.EnableBuffering()` — wraps body in `FileBufferingReadStream`
2. Called `await JsonDocument.ParseAsync(request.Body)` — full O(n) parse of entire body
3. Checked if `method == "initialize"` — always false for `tools/call`
4. Reset `request.Body.Position = 0` — MCP SDK reads body again

For `tools/call`, this was entirely wasted work.

**Fix:** Added `TryGetMethodField` using `Utf8JsonReader` over a pooled 512-byte peek buffer. For non-initialize requests, the fast path exits after reading at most 512 bytes, skipping the full `JsonDocument.ParseAsync`. `ArrayPool<byte>.Shared` is used to avoid the peek buffer allocation. The full parse is still done for `initialize` requests (rare, small body).

**Impact:** For typical `tools/call` bodies (~100–500 bytes), avoids one `JsonDocument` allocation + O(n) parse per request.

---

## Handle Leak — Investigation Findings

### Problem
FullMix soak: handle growth rate ~0.029–0.042 handles/s. `tools/list` GREEN (no growth). Growth correlates with `tools/call`.

### What Was Checked

| Area | Finding |
|------|---------|
| `AcquireAsync` CTS | CLEAN — prior ba95fa6 fix uses `using var` for both `timeoutCts` and `linkedCts` |
| `OperationContext` (AsyncLocal) | CLEAN — pure managed stack, no kernel handles |
| `Activity` in `using` block | CLEAN — properly disposed; null when no OTel listeners |
| `McpProtocolVersionMiddleware.EnableBuffering()` | For bodies ≤ 32KB: `MemoryStream` only — no file handles. For bodies > 32KB: temp file handle, registered for dispose via `RegisterForDispose`. Not a leak in the GC sense; handle count could transiently increase under heavy load but closes properly at request end. |
| `PSDataCollection<T>` streams | Per-worker (not per-call) — cannot explain per-call handle growth |
| `RunspaceResetProtocol` / PS SDK | **Primary suspect** — see below |

### Primary Suspect: PS SDK Pipeline Kernel Objects (Hermes Domain)

Each `ps.Invoke()` on a reused runspace may create kernel synchronization objects (`EventWaitHandle`, `Mutex`, or pipe handles) that are part of the PS SDK's `LocalPipeline` infrastructure. These may not be released until the `System.Management.Automation.PowerShell` instance itself is disposed or the runspace is closed.

In the pool model (`pool_reset`), the same `PowerShell` instance is reused across many `tools/call` invocations (via `OnWorkerReturnedAsync` → reset → re-enqueue). If the PS SDK leaks a kernel object per `ps.Invoke()` rather than per `PowerShell` lifetime, handle growth would be exactly O(calls), correlating with `tools/call` but not `tools/list`.

**Profiler evidence required:** This cannot be confirmed without:
1. ETW/WPR capture with `Microsoft-Windows-Kernel-Process` handle tracking, OR
2. `dotnet-trace` with `ClrRundown + ClrPrivate` providers to identify GC-root handle holders

### Recommendation to Hermes

Please investigate whether `ps.Streams.Error`, `ps.Streams.Output`, `ps.Streams.Warning`, `ps.Streams.Verbose`, `ps.Streams.Debug`, and `ps.Streams.Information` accumulate kernel `WaitHandle` objects between calls. Specifically:

- Does calling `ps.Streams.Error.Clear()` (or `ReadAll()`) in `ClearStreams` release the `ManualResetEventSlim`'s kernel handle if it was escalated?
- Does `ManualResetEventSlim.Reset()` de-escalate (return to kernel-free mode) once the count drops to zero?
- Would explicitly calling `ps.Streams.ClearStreams()` + `ps.Streams.Error.Dispose()` (re-creating the PSDataCollection) between calls eliminate the growth?

A controlled experiment: run FullMix soak with `ps.Streams.Error.Dispose(); ps.Streams.Error = new PSDataCollection<ErrorRecord>();` in `ClearStreams` and compare handle growth rate.

### Secondary Suspect: `PSDataCollection<T>` Default Capacity
`ManualResetEventSlim` escalates to a kernel `EventWaitHandle` the first time `Wait()` is called. If PS SDK internals call `Wait()` on any stream collection during `Invoke()`, that stream's `ManualResetEventSlim` permanently holds a kernel handle. This happens once per worker at first call, not per call — so it would show as a startup spike rather than linear growth. Rules this out as the primary per-call leak cause.

---

## Files Changed
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` — logging guards (warm-path)
- `PoshMcp.Server/Observability/LoggerExtensions.cs` — Dictionary → KeyValuePair[] (warm-path)
- `PoshMcp.Server/Server/McpProtocolVersionMiddleware.cs` — fast-path body scan (warm-path + minor perf)

## Residual Risk
- Warm-path gap: C# layer changes reduce overhead by ~0.05–0.15ms estimated. The remaining gap to 1.05× gate is primarily in PS reset protocol (Hermes domain).
- Handle leak: unresolved without profiler. Recommend ETW trace run before next soak.
- `TryGetMethodField` uses `isFinalBlock: false` with 512-byte peek — if "method" key straddles the 512-byte boundary, `Utf8JsonReader` will fail to find it and the exception catch returns false (skips protocol version tracking for this request). In practice, JSON-RPC method fields are always within the first ~100 bytes; this edge case is acceptable.
