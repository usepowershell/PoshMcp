---
name: "oop-wire-format-extension"
description: "Extend the PoshMcp OOP discover wire format (RemoteToolSchema + parallel payload) safely without breaking backward compatibility with older OOP hosts or C# servers. WHEN: adding new fields to OOP discover responses, threading per-command metadata across the JSON-RPC boundary, or adding new top-level optional payload objects to the discover response."
domain: "api-design"
confidence: "medium"
source: "earned"
captured-by: hermes
captured: 2026-05-15
---

# OOP wire-format extension

## When this applies

Any time you need to add data to the JSON crossing the JSON-RPC boundary between the C# server and the `pwsh` OOP subprocess (`oop-host.ps1` or `oop-host-pool.ps1`). Examples:
- New per-command metadata that follows the schema array.
- New top-level fields on the `discover` response.
- New per-tool attribution data that the doctor or a tool consumer needs.

## The contract

OOP wire format is **versioned by Newtonsoft default behavior, not by an explicit version field.** Backward compatibility relies on two Newtonsoft properties:

1. **Unknown JSON fields are silently ignored on deserialize.** Newer hosts emit fields older C# servers ignore.
2. **Missing JSON fields produce default values for the corresponding type** — `null` for nullable reference types, `0` for value types, empty for collections initialized in the constructor.

This means the safe extension pattern is **always additive, always nullable / defaulted, never required**. The moment you make a field non-nullable required (or introduce a new shape that older hosts don't emit at all without a fallback), you break one of the two compat directions.

## The 4-step recipe

### 1. Add nullable fields to `RemoteToolSchema` (per-command data)

> Code: see `REFERENCE.md` § "C# Field Addition"

- Make the field **nullable**.
- Use `NullValueHandling.Ignore` so newer hosts don't bloat older C# server's deserialization with empty fields they don't read.
- Older hosts that don't emit the field → null (Newtonsoft default).
- Older C# servers that don't read the field → ignored (Newtonsoft default).

### 2. Add a parallel top-level payload (request-scoped data)

For data that doesn't belong on every command (per-module probe results, per-pattern statistics, environment fingerprints), add a new top-level optional object after the schemas array.

> Code: see `REFERENCE.md` § "Parallel Top-Level Payload"

- Define a top-level POCO (`RemoteModuleImportsPayload`) with collections that initialize to empty in the constructor.
- Parse defensively in `OutOfProcessCommandExecutor` and `OutOfProcessSubprocessPool` — both must handle the field being absent (older host).

### 3. Emit from both OOP host scripts

`oop-host.ps1` (single-host) and `oop-host-pool.ps1` (runspace pool) **must both** be updated. The pool variant runs discovery inside a script block, so:

- Wrap the script-block return as `[pscustomobject]@{ Schemas = $schemas; ModuleImports = $payload }`.
- In the outer handler, unwrap with a defensive fallback (see `REFERENCE.md` § "OOP Host Pool Defensive Unwrap").

This way any alternate script-block invocation that returns the bare array still works.

### 4. Wire the C# consumer with backward-compat fallback

For data that flows OOP → C# but is consumed somewhere disconnected from the discover call site (e.g., `DoctorService` invoked separately from `McpToolSetupService`), use:

- `ICommandExecutor.LastModuleImports` default-implementation property returns null for non-OOP executors.
- AsyncLocal capture helper (`OopModuleImportsCapture`) with `Reset()` before discovery and `Set()` after, both **before the executor lease disposes**. CLI doctor is one-shot so cross-invocation leak is impossible.
- New consumer overload accepts `RemoteModuleImportsPayload?`. When non-null, skip the in-process fallback path entirely. When null, fall back to existing in-process behavior + emit a one-time `DoctorReport.Warnings` entry.

## Source-attribution priority

When deriving per-command source data on the host side, follow first-writer-wins enumeration order to encode priority deterministically. See `REFERENCE.md` § "Source-Attribution Priority" for the full loop.

No post-merge resolution needed — the enumeration order encodes the priority.

## Test coverage required

For any wire-format extension you should always add at least three tests:

1. **Round-trip test**: serialize → JSON → deserialize, assert all fields preserved.
2. **Older-host JSON test**: hand-craft a JSON payload **lacking** the new fields, deserialize, assert defaults are applied (`null`, empty collection, etc.). This proves SC-263-4-style backward compat.
3. **Integration test against real OOP host**: spawn the actual `pwsh` subprocess via `OutOfProcessCommandExecutor`, exercise discovery against a small config that triggers the new fields, assert the C# side captures the data.

## Pitfalls

1. **Don't add a required field.** Even if you "know" all hosts will be upgraded, somebody will pin an older OOP host on disk and the C# server will throw on deserialize.
2. **Don't break the bare-array contract on the pool variant** without a defensive fallback. If you switch to `PSCustomObject` always, an older script-block invocation that returns the bare array fails silently.
3. **Don't skip the integration test.** Unit tests against hand-built JSON catch the C# side; only an integration test against the real `oop-host.ps1` catches PowerShell-side serialization bugs (e.g., `Newtonsoft` PascalCase vs `ConvertTo-Json` camelCase mismatches).
4. **Don't forget the AsyncLocal Reset() call.** If you Set without Reset, a prior in-flow discovery's payload can leak into the next consumer invocation. CLI doctor is one-shot so the leak is bounded, but it's still a correctness bug.
5. **Don't try to refactor the executor signature instead.** It's tempting to change `DiscoverToolsForCliAsync` to return a tuple. Resist it — the AsyncLocal capture pattern is less invasive and the executor lease lifetime is short enough that the AsyncLocal trade-off is benign.

## Related

- Spec 011 / issue #268 / PR #271 (Phase 2 — first application of this pattern).
- Spec 010 / `IToolDescriptionSourceTracker` (analogous pattern for per-tool description source attribution; could be the model for a future per-tool import attribution tracker that consumes the new `RemoteToolSchema.Source*` fields).
