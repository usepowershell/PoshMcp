---
uid: session-management
title: Session Management
---

# Session Management

PoshMcp manages PowerShell runspaces for each MCP request or connection.

## Execution State Model

**HTTP (default — Stateless):** each tool call leases a clean, pooled runspace
from the shared `StatelessRunspacePool`, executes, and returns the worker to the
pool after a reset. PowerShell variables, functions, and location do **not**
persist between HTTP calls; every call starts from a clean state.

**Stdio:** uses a single process-scoped `SingletonPowerShellRunspace`. The
runspace persists for the lifetime of the connection, so variables and functions
**do** accumulate across calls within that session.

**Stateful HTTP (opt-in):** adds MCP protocol session bookkeeping
(`Mcp-Session-Id`, idle timeout). PowerShell execution is still served from the
shared pool with reset-before-reuse — there is **no** dedicated per-session
runspace, and no cross-call variable persistence.

> **stdio example — persistent state:**
>
> ```powershell
> # Call 1: Set a variable (stdio session only)
> $MyData = @{ Timestamp = Get-Date; Records = Get-Process | Select-Object -First 5 }
>
> # Call 2: Access the variable (same stdio connection)
> $MyData.Timestamp
> # Output: [date from Call 1]
> ```
>
> Over **HTTP**, `$MyData` is **not** available in Call 2 — the worker is reset
> before reuse and starts clean.

## Per-Call Isolation (HTTP Mode)

HTTP requests are served from a **reset-before-reuse runspace pool**; there is
no per-user or per-session affinity. Each call receives a clean worker and
returns it after use, preventing cross-request state bleed:

```powershell
# HTTP — isolation is per-call, not per-user
# User A's call: sets $Global:UserId; the worker is reset before the next call
$Global:UserId = "user-a@company.com"

# User B's next call gets a separate, reset worker (no $Global:UserId from User A)
```

For **process-level tenant isolation** (separate `pwsh` subprocess per tenant),
use OutOfProcess `SubprocessHostMode=ProcessPool`.

## Runspace Pool Lifecycle

Each HTTP request leases a clean, reset pooled worker for the duration of that
call and returns it after use. Workers **are** reused across requests after
reset; there is no `Mcp-Session-Id`→runspace binding. The pool is shared by
both Stateless (default) and Stateful HTTP modes.

The defaults are: `MaxPoolSize` 16, `MinPoolSize` 2, `EagerWarmCount` 2
(workers pre-warmed at startup), `AcquisitionTimeout` 15 seconds, `IdleTtl`
5 minutes, `SweepInterval` 30 seconds. When the pool is exhausted, a request
waits up to `AcquisitionTimeout` before failing.

Configure the pool under `McpServer:RunspacePool`:

```json
{
  "McpServer": {
    "HttpTransportMode": "Stateless",
    "IdleSessionTimeoutSeconds": 120,
    "RunspacePool": {
      "MaxPoolSize": 24,
      "MinPoolSize": 2,
      "EagerWarmCount": 2,
      "AcquisitionTimeout": "00:00:15",
      "IdleTtl": "00:05:00",
      "SweepInterval": "00:00:30"
    }
  }
}
```

`EagerWarmCount` pre-warms workers **within** `MaxPoolSize` (they count toward
the max). `MinPoolSize` is the floor the pool replenishes to after workers are
evicted. `IdleSessionTimeoutSeconds` applies **only in Stateful mode** and
governs MCP session idle expiry — not individual worker lifetime.

For the complete setting reference and sizing guidance, see
[Configuration](configuration.md#mcp-server-http-sessions). Dynamic tool
reload drains and replenishes the **runspace pool**; because HTTP is stateless,
there is no session-scoped state to lose — in-flight leases complete before
workers cycle.

## Startup Scripts

Run PowerShell code for each **pooled runspace worker** at warm-up and
replenishment (governed by `EagerWarmCount`/`ReplenishCheckInterval`). Startup
scripts do **not** run once at server start — they run per worker, each time a
new worker is initialized into the pool.

Edit `appsettings.json`:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:SessionStartTime = Get-Date"
    }
  }
}
```

Or load from a file:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScriptPath": "./startup.ps1"
    }
  }
}
```

Example `startup.ps1`:

```powershell
$Global:CompanyName = 'Acme'
$Global:Environment = 'Production'
Connect-AzAccount -Identity -ErrorAction Stop
Write-Host "✓ Environment initialized" -ForegroundColor Green
```
