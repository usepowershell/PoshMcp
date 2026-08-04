---
uid: migration-v1-v2
title: Migrating from v1 to v2
---

# Migrating from v1 to v2

This guide covers the behavioral changes, configuration key renames, and
deployment steps for operators upgrading from PoshMcp v1 (MCP SDK 1.4.1,
protocol `2025-11-25`) to v2 (MCP SDK 2.0.0, default protocol `2026-07-28`).

## What changed

### Execution model

**v1** used a session-affine `SessionAwarePowerShellRunspace`: each HTTP MCP
session received a dedicated PowerShell runspace that persisted for the
lifetime of that MCP protocol session. Variables, functions, and location set
in one HTTP call were available in subsequent calls within the same session.

**v2** replaces that with a `StatelessRunspacePool`: every HTTP tool call
leases a clean, reset pooled worker, executes, and returns the worker after a
reset. **No PowerShell state persists between HTTP calls**, regardless of
`Mcp-Session-Id`. The session ID is now a protocol-only identifier for MCP
message correlation; it does not select or retain a worker.

> **stdio is unchanged.** Stdio mode still uses a single process-scoped
> `SingletonPowerShellRunspace`; variables and functions accumulate normally
> across calls for the lifetime of that connection.

### Protocol version

The default protocol changes from `2025-11-25` to `2026-07-28` (Stateless
mode). `2025-11-25` remains available for legacy clients via Stateful mode
(see [Stateful compatibility](#stateful-http-compatibility-opt-in) below).

### SDK packages

`ModelContextProtocol` and `ModelContextProtocol.AspNetCore` are now **2.0.0**.
The `ModelContextProtocol.Extensions.Tasks` package is available as a preview
extension (Tasks deferred — spike only); do not add it to the production server.

---

## Prerequisites

- .NET SDK 10 or later
- All existing `appsettings.json` files accessible for key migration
- Deployment pipeline that can drain before rollout (recommended)

---

## Behavior changes requiring attention

| Area | v1 behavior | v2 behavior | Action needed |
|------|-------------|-------------|---------------|
| HTTP PowerShell state | Persists within MCP session | **Reset before each call** | Audit tools/workflows that relied on cross-call state |
| `Mcp-Session-Id` | Selects a dedicated runspace | Protocol identifier only; no runspace binding | Remove any logic that assumed runspace affinity |
| Session-runspace count | `SessionRunspaceCapacity` (hard limit) | `RunspacePool:MaxPoolSize` (total workers) | Remap key; semantics similar |
| Warm standbys | `SessionRunspaceWarmStandbyCount` (one key) | `RunspacePool:MinPoolSize` + `RunspacePool:EagerWarmCount` (two keys) | Set both; defaults are 2 each |
| Idle TTL | `SessionRunspaceIdleTtlSeconds` (int seconds) | `RunspacePool:IdleTtl` (TimeSpan `hh:mm:ss`) | Convert unit |
| Sweep interval | `SessionRunspaceSweepIntervalSeconds` (int seconds) | `RunspacePool:SweepInterval` (TimeSpan `hh:mm:ss`) | Convert unit |
| Acquisition timeout | `SessionRunspaceAcquisitionTimeoutSeconds` (int seconds) | `RunspacePool:AcquisitionTimeout` (TimeSpan `hh:mm:ss`) | Convert unit |
| Idle session timeout | Applied to runspace idle | Applied to MCP session idle **in Stateful mode only** | No runspace effect in Stateless mode |
| Pool worker lifetime | No separate concept | `RunspacePool:IdleTtl` / `StopTimeout` / `ShutdownDrainTimeout` | New keys; defaults are safe |
| Transport mode key | Not configurable | `McpServer:HttpTransportMode` | Add key only if you need Stateful opt-in |
| Startup scripts | Once per session assignment | **Once per warm worker** at init/replenishment | Scripts must be idempotent; see [Startup Scripts Guide](startup-scripts.md) |

---

## Configuration key mapping

Deprecated keys still bind at runtime via per-key alias fallback, each
emitting **one deprecation warning** at startup. Migrate before the next major
version, when they will be removed.

### Before (v1) → After (v2)

```json
// BEFORE — v1 McpServer block
{
  "McpServer": {
    "IdleSessionTimeoutSeconds": 120,
    "SessionRunspaceCapacity": 24,
    "SessionRunspaceIdleTtlSeconds": 300,
    "SessionRunspaceSweepIntervalSeconds": 30,
    "SessionRunspaceWarmStandbyCount": 4,
    "SessionRunspaceAcquisitionTimeoutSeconds": 15
  }
}
```

```json
// AFTER — v2 McpServer block
{
  "McpServer": {
    "HttpTransportMode": "Stateless",
    "IdleSessionTimeoutSeconds": 120,
    "RunspacePool": {
      "MaxPoolSize": 24,
      "MinPoolSize": 4,
      "EagerWarmCount": 4,
      "IdleTtl": "00:05:00",
      "SweepInterval": "00:00:30",
      "AcquisitionTimeout": "00:00:15"
    }
  }
}
```

> **Note:** `IdleSessionTimeoutSeconds` has no effect in Stateless mode
> (the default). Keep it only if you are running Stateful mode for legacy
> client compatibility.

### Key reference

| v1 key (`McpServer:`) | v2 key (`McpServer:RunspacePool:`) | v2 type | Default | Notes |
|-----------------------|------------------------------------|---------|---------|-------|
| `SessionRunspaceCapacity` | `MaxPoolSize` | int | 16 | Total workers in pool |
| `SessionRunspaceWarmStandbyCount` | `MinPoolSize` | int | 2 | Replenishment floor *(dual-map — set both)* |
| `SessionRunspaceWarmStandbyCount` | `EagerWarmCount` | int | 2 | Workers pre-warmed at startup *(dual-map — set both)* |
| `SessionRunspaceIdleTtlSeconds` | `IdleTtl` | TimeSpan | `00:05:00` | `hh:mm:ss`; divide old value by 60 for minutes |
| `SessionRunspaceSweepIntervalSeconds` | `SweepInterval` | TimeSpan | `00:00:30` | |
| `SessionRunspaceAcquisitionTimeoutSeconds` | `AcquisitionTimeout` | TimeSpan | `00:00:15` | |
| *(none)* | `StopTimeout` | TimeSpan | `00:00:05` | Worker stop grace period |
| *(none)* | `ShutdownDrainTimeout` | TimeSpan | `00:00:30` | Graceful shutdown drain |
| *(none)* | `ReplenishCheckInterval` | TimeSpan | `00:00:05` | Pool replenishment poll interval |
| *(none)* | `McpServer:HttpTransportMode` | enum | `Stateless` | `Stateless` (default) or `Stateful` |

**Precedence:** v2 key > v1 deprecated alias > coded default. A warning is
emitted for each deprecated key present, even when a v2 key takes precedence.

### Environment variable form

Standard .NET double-underscore binding maps to the nested keys:

```bash
# v2 equivalents for the common v1 env var overrides
export McpServer__RunspacePool__MaxPoolSize=24
export McpServer__RunspacePool__MinPoolSize=4
export McpServer__RunspacePool__EagerWarmCount=4
export McpServer__RunspacePool__IdleTtl="00:05:00"
export McpServer__HttpTransportMode=Stateless

# Stateful-mode idle session timeout (opt-in only)
export McpServer__IdleSessionTimeoutSeconds=120
```

Do not use `POSHMCP_SESSION_TIMEOUT_MINUTES` — it is not wired in v2.

---

## Cross-call PowerShell state: alternatives

If your deployment relies on HTTP cross-call state you must adopt one of these
patterns for v2:

| Pattern | When to use | How |
|---------|-------------|-----|
| **stdio** | Single-user, local, or GitHub Copilot scenarios | `--transport stdio`; state persists for the connection lifetime |
| **Explicit request arguments** | Tool accepts all required context in each call | Pass all required parameters explicitly; design tools to be stateless |
| **Durable external state** | Shared data that outlives a call | Store in a database, cache, or file keyed by authenticated identity or app key; retrieve in the tool body |
| **Application-owned storage** | User-specific session data | Key on the authenticated user claim or API key; read at the start of each tool call |

**Never assume Stateful HTTP preserves PowerShell variables, location, or
loaded modules between calls.** Stateful mode adds `Mcp-Session-Id` protocol
continuity only; the worker is still reset before each tool call.

---

## Stateful HTTP compatibility (opt-in)

If clients require `Mcp-Session-Id` for MCP protocol continuity (for example,
`2025-11-25` clients that expect a session header), opt in via:

```json
{
  "McpServer": {
    "HttpTransportMode": "Stateful"
  }
}
```

Stateful mode enables:
- `Mcp-Session-Id` issuance and `IdleSessionTimeoutSeconds` enforcement
- Legacy `2025-11-25` protocol negotiation

Stateful mode does **not**:
- Assign a dedicated PowerShell runspace to an MCP session
- Preserve PowerShell variables, location, or modules between calls
- Provide any runspace state not also provided by Stateless mode

Stateful mode is a transitional option for client compatibility. Plan to move
clients to protocol `2026-07-28` Stateless mode.

---

## Deployment sequence

1. **Update packages** — verify `ModelContextProtocol` and
   `ModelContextProtocol.AspNetCore` are `2.0.0` in `PoshMcp.csproj`.
2. **Audit cross-call state usage** — search tool implementations for global
   variable reads/writes that assumed HTTP cross-call persistence.
3. **Update `appsettings.json`** — apply the key migration table above.
4. **Update startup scripts** — ensure scripts are idempotent; see
   [Startup Scripts Guide](startup-scripts.md).
5. **Deploy with a drain** — drain existing connections before the rollout to
   let v1 sessions close cleanly, then bring up v2.
6. **Verify with health probes**:

   ```bash
   curl https://poshmcp.example/health
   curl https://poshmcp.example/health/ready
   ```

   `/health/ready` passes only after the pool has warm workers and all
   registered checks pass. If the server does not reach ready, inspect logs for
   `Starting StatelessRunspacePool` and `Startup complete` events.

7. **Check for deprecation warnings** in startup logs — one warning per
   deprecated key present; eliminate them by migrating to v2 keys.

---

## Rollback

Restoring the v1 HTTP session-affine runspace model requires **reverting both
the code and packages** to a version prior to the `StatelessRunspacePool`
introduction. There is no runtime configuration switch that restores v1
behavior. Setting `HttpTransportMode: "Stateful"` does **not** restore the v1
dedicated-runspace model; it only enables `Mcp-Session-Id` session bookkeeping
on the same shared pool.

To roll back: revert to the pre-v2 release tag, restore the previous
`appsettings.json`, and redeploy.

---

## Verification

After migration, validate the following:

```bash
# Pool started and warmed up
grep "Starting StatelessRunspacePool" poshmcp.log
grep "Startup complete" poshmcp.log

# No deprecated-key warnings (once migrated)
grep "SessionRunspace" poshmcp.log  # should be empty after key migration

# Health checks pass
curl -s https://poshmcp.example/health | jq .status
curl -s https://poshmcp.example/health/ready | jq .status

# Tools return results (stateless tool call)
curl -s -X POST https://poshmcp.example/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  --data '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

---

## See also

- [Transport Modes](transport-modes.md) — Stateless vs Stateful HTTP and stdio
- [Session Management](session-management.md) — execution state model and pool lifecycle
- [Configuration Guide](configuration.md#mcp-server-http-sessions) — full key reference
- [Startup Scripts Guide](startup-scripts.md) — safe startup-script patterns
