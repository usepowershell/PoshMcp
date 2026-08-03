# Project Context

- **Owner:** {user name}
- **Project:** {project description}
- **Stack:** {languages, frameworks, tools}
- **Created:** {timestamp}

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-08-03T13:46:21-05:00 — Pool contract types (issue #344)

**Context:** Implemented `IRunspacePool`, `RunspaceWorker`, `RunspaceLease`, `RunspacePoolOptions`,
`RunspaceWorkerState`, `RunspacePoolStats` in `PoshMcp.Server/PowerShell/Pool/`.

**Key learnings:**

1. **Contract-only issues need no PowerShell resources in tests.** By making `RunspaceWorker`
   accept `IPowerShellRunspace` (the existing interface) rather than `IsolatedPowerShellRunspace`,
   all 75 state-machine and lease-disposal tests run in < 1 ms each using `Moq` mocks.
   Reserve real `IsolatedPowerShellRunspace` creation for integration/functional tests.

2. **Interlocked.CompareExchange on an int field is the right tool for state machine transitions.**
   Casting `RunspaceWorkerState` enum values to/from `int` and using `CAS` makes the state
   machine lock-free and thread-safe without monitors. The return value (`prev == current`) tells
   the caller whether they won the race.

3. **`TimeSpan.Zero` vs positive intervals need distinct validation rules.** `AcquisitionTimeout =
   TimeSpan.Zero` is a meaningful sentinel ("instant fail"). All scheduling intervals (`IdleTtl`,
   `SweepInterval`, etc.) must be positive. Mixing them under one rule would reject valid config.

4. **Exactly-once disposal via `Interlocked.Exchange(ref _disposed, 1)`** is the canonical pattern
   for idempotent `IDisposable` + `IAsyncDisposable` implementations. Set `_worker` to null in
   the same exchange to ensure `PowerShell` access after disposal throws `ObjectDisposedException`
   without a separate disposed flag.

5. **`LastLeaseCompletedAt` should only be set on `Resetting → Warm`, not on `Creating → Warm`.**
   This preserves the semantic of "worker has never been leased" (null) vs. "worker last completed
   a lease at T", which the idle-TTL sweep (#348) needs to correctly protect fresh workers.

6. **The decisions inbox path is `.squad/decisions/inbox/<filename>.md`.** The `.squad/decisions/`
   directory may not exist; create it with `-Force` before writing the inbox file.

**Approved defaults (do not change without new decisions.md entry):**
MinPoolSize=2, MaxPoolSize=16, EagerWarmCount=2, AcquisitionTimeout=15s, IdleTtl=300s,
SweepInterval=30s, StopTimeout=5s, ShutdownDrainTimeout=30s, ReplenishCheckInterval=5s.
