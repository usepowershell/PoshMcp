# PoshMcp SDK v2 Upgrade — Pre-Release Notes

> **Release status.** All release-blocking gates have cleared. The SDK v2 warm-call/throughput
> gate ([#380](https://github.com/usepowershell/PoshMcp/issues/380)) is **GREEN** in enforce
> mode (`enforceGate=true`) on `d5d715c`
> (run [31126540107](https://github.com/usepowershell/PoshMcp/actions/runs/31126540107)).
> Sustained-load soak non-handle gates passed; [#349](https://github.com/usepowershell/PoshMcp/issues/349)
> and [#385](https://github.com/usepowershell/PoshMcp/issues/385) are closed.
>
> **Known follow-up:** A residual Windows FullMix handle-floor slope was observed on hosted
> runners during soak. This was assessed as non-blocking for migration and is tracked on
> [#396](https://github.com/usepowershell/PoshMcp/issues/396). It does not affect stateless
> HTTP correctness, pool isolation, or the v2 migration path.
>
> Final release verification is tracked on [#360](https://github.com/usepowershell/PoshMcp/issues/360).

## Summary

This release upgrades PoshMcp to **ModelContextProtocol 2.0.0** and
**ModelContextProtocol.AspNetCore 2.0.0**, switches the HTTP transport default to **Stateless**,
replaces per-session runspace affinity with a shared warm worker pool, and adds health, readiness,
and metrics instrumentation for the pool.

The primary user-visible impact is that **HTTP tool calls no longer preserve PowerShell variables,
modules, or working directory between calls**. This is a breaking change for deployments that
relied on cross-call state. See [Breaking Changes](#breaking-changes) and the
[migration guide](../articles/migration-v1-v2.md).

## What's Changed

### SDK and protocol versions

- `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` updated from **1.4.1** to
  **2.0.0**.
- SDK v2 introduces the `server/discover` capability, which uses the **`2026-07-28`** MCP
  protocol spec. Standard `initialize` negotiates **`2025-11-25`**; **`2024-11-05`** remains
  supported as a compatibility fallback. The `2026-07-28` date does not apply to ordinary
  `initialize` handshakes.

### HTTP transport: Stateless default

HTTP transport now defaults to `Stateless` mode (`McpServer:HttpTransportMode = Stateless`).

Each tool call:
1. Acquires a clean worker from the shared `StatelessRunspacePool`.
2. Runs the PowerShell command in an isolated, reset execution environment.
3. Returns the worker to the pool for reuse.

`Mcp-Session-Id` controls the MCP protocol/session lifecycle (SSE stream lifetime, event
sequencing) but is **not** used to route tool calls to a specific PowerShell worker. No per-session
or per-user runspace affinity exists.

### Explicit Stateful HTTP (compatibility mode)

Setting `McpServer:HttpTransportMode = Stateful` retains MCP protocol/session semantics for
clients that depend on `Mcp-Session-Id` continuity. **This mode does not preserve PowerShell
execution state.** Stateful and Stateless modes both use the same shared `StatelessRunspacePool`
and provide the same per-call isolation.

`IdleSessionTimeoutSeconds` applies only in Stateful mode.

### Shared warm runspace pool

The `StatelessRunspacePool` maintains a configurable set of pre-warmed, reset-ready workers.
Configure it under `McpServer:RunspacePool`:

| Key | Default | Description |
|-----|---------|-------------|
| `MaxPoolSize` | `16` | Maximum concurrent workers |
| `MinPoolSize` | `2` | Minimum workers kept warm |
| `EagerWarmCount` | `2` | Workers pre-created at startup |
| `IdleTtl` | `00:05:00` | Maximum idle time before eviction |
| `SweepInterval` | `00:00:30` | Interval between idle sweeps |
| `AcquisitionTimeout` | `00:00:15` | Maximum wait for a free worker |
| `StopTimeout` | `00:00:05` | Time to wait for graceful worker stop |
| `ShutdownDrainTimeout` | `00:00:30` | Time to drain in-flight leases on shutdown |
| `ReplenishCheckInterval` | `00:00:05` | Interval between replenishment checks |

### Startup scripts: once per warm worker

Startup scripts run once per warm worker during pool initialisation, not once per process or per
tool call. Workers may be created concurrently during eager warm-up or pool replenishment. Scripts
must be **idempotent, deterministic, and thread-safe**. See the
[startup-script guide](../articles/startup-scripts.md).

### Health, readiness, and metrics

- `/health` — includes `runspace_pool` health check.
- `/health/ready` — tagged `ready`; reflects pool readiness after eager warm-up.
- Metrics — pool depth, lease latency, reset duration, and eviction counts.

### Stdio mode unchanged

Stdio transport is unchanged: it uses a single process-scoped runspace (`SingletonPowerShellRunspace`),
retaining full PowerShell state across all calls within the process lifetime.

### Configuration key migration

`SessionRunspace*` keys from v0.18.0 are translated at startup with a deprecation warning. Migrate
to the current `McpServer:RunspacePool:*` keys to silence warnings:

| v0.18.0 key (deprecated) | v2 key | Notes |
|---------------------------|--------|-------|
| `SessionRunspaceCapacity` | `McpServer:RunspacePool:MaxPoolSize` | int |
| `SessionRunspaceWarmStandbyCount` | `McpServer:RunspacePool:MinPoolSize` **and** `EagerWarmCount` | dual-mapped |
| `SessionRunspaceIdleTtlSeconds` | `McpServer:RunspacePool:IdleTtl` | int seconds → TimeSpan |
| `SessionRunspaceSweepIntervalSeconds` | `McpServer:RunspacePool:SweepInterval` | int seconds → TimeSpan |
| `SessionRunspaceAcquisitionTimeoutSeconds` | `McpServer:RunspacePool:AcquisitionTimeout` | int seconds → TimeSpan |

Legacy keys will be removed in a future major version. The public types
`SessionAwarePowerShellRunspace` and `SessionRunspaceOptions` are deprecated compatibility
surfaces retained until the next major version.

`FunctionNames` is also deprecated; use `CommandNames`.

## Breaking Changes

### HTTP cross-call PowerShell state removed

Stateless HTTP no longer retains PowerShell variables, modules, or working directory between tool
calls. Each call starts with a clean, reset worker.

**Affected workflows:**

- Scripts that `$env:`, `Set-Location`, or `Import-Module` on one call and rely on those side
  effects in a subsequent call.
- Any implicit assumption that the same PowerShell session handles consecutive requests.

**Alternatives:**

- Pass all required state as explicit tool arguments on every call.
- Use **stdio transport** for single-client scenarios that need process-scoped state.
- Store durable cross-call state in an external system (database, file, Key Vault) keyed by
  authenticated identity or application key.
- Use `McpServer:HttpTransportMode = Stateful` for MCP protocol session continuity *only* — this
  does not preserve PowerShell execution state.

### Rollback

Restoring per-session runspace affinity requires reverting to the previous code and package
versions. There is no configuration flag to re-enable the v0.18.0 per-session runspace behaviour
in v2.

## Upgrade Notes

1. **Review startup scripts.** Ensure they are idempotent and do not assume a per-session or
   per-process execution context.
2. **Migrate configuration keys.** Replace `SessionRunspace*` keys with `McpServer:RunspacePool:*`
   equivalents. See the [migration guide](../articles/migration-v1-v2.md) for a complete
   before/after mapping.
3. **Test cross-call state assumptions.** Audit tool sequences that rely on PowerShell side effects
   persisting across calls. Refactor to pass state explicitly or use an alternative from the list
   above.
4. **Update clients if needed.** Clients using standard `initialize` negotiate `2025-11-25`
   (or `2024-11-05` as a compatibility fallback) — this is unchanged from prior releases. The
   `2026-07-28` protocol spec applies to the SDK v2 `server/discover` capability; clients that
   do not use `server/discover` continue to work without changes.
5. **Verify health endpoints.** The `/health/ready` endpoint reflects pool readiness after the
   eager warm-up phase completes. Allow the pool to reach ready state before routing traffic in
   production.

## Tasks Extension

The MCP Tasks extension is **deferred** and is not included in this release.

## Release Gate Status

| Issue | Description | Status |
|-------|-------------|--------|
| [#380](https://github.com/usepowershell/PoshMcp/issues/380) | SDK v2 warm-call/throughput gate (`enforceGate=true`, `d5d715c`, run [31126540107](https://github.com/usepowershell/PoshMcp/actions/runs/31126540107)) | ✅ CLOSED — GREEN |
| [#349](https://github.com/usepowershell/PoshMcp/issues/349) | Sustained-load soak non-handle gates | ✅ CLOSED |
| [#385](https://github.com/usepowershell/PoshMcp/issues/385) | Handle-floor investigation | ✅ CLOSED — superseded by #396 |
| [#396](https://github.com/usepowershell/PoshMcp/issues/396) | Residual Windows FullMix handle-floor slope (ops follow-up) | 🔵 OPEN — **not a migration blocker**; deferred post-release |

## See Also

- [Migration guide: v1 → v2](../articles/migration-v1-v2.md)
- [Startup-script guide](../articles/startup-scripts.md)
- [Transport modes reference](../articles/transport-modes.md)
- [Session management](../articles/session-management.md)
- [Configuration reference](../articles/configuration.md)
- [Security and identity](../articles/security.md)
