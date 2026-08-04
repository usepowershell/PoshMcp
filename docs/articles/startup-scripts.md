---
uid: startup-scripts
title: Startup Scripts Guide
---

# Startup Scripts Guide

Startup scripts let you pre-initialize each pooled runspace worker with
functions, modules, aliases, and constants. This guide covers the execution
model, safe patterns, anti-patterns, and how to test scripts.

## Execution model

A startup script is PowerShell code that runs once for **each pooled runspace
worker** at the time the worker is initialized (warm-up or replenishment).

Key facts:
- **Per-worker, not per-server.** The script runs each time a new worker is
  added to the pool, which happens at startup (up to `EagerWarmCount` workers)
  and whenever the pool replenishes below `MinPoolSize`.
- **Multiple workers run concurrently.** The pool may initialize several workers
  in parallel; scripts should not write to shared external state in a way that
  causes conflicts.
- **Runs before the worker enters the pool.** The worker is available for
  leasing only after the script completes without error. A script that exits
  with a terminating error causes the worker to be evicted and the pool to
  replenish.
- **Not per-session and not per-call.** The state set by the script (functions,
  variables, modules, aliases) exists in that worker until the worker is reset
  after a tool call, at which point the next lease gets a fresh reset worker.
  **Each tool call starts from a clean reset state**, not from accumulated
  script state.

> **stdio exception.** In stdio mode, there is one process-scoped
> `SingletonPowerShellRunspace`; the startup script runs once for that
> runspace and its effects persist for the lifetime of the connection.

---

## Configuration

Set the startup script in `appsettings.json`:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:CompanyName = 'Acme'",
      "StartupScriptPath": "./startup.ps1"
    }
  }
}
```

Or via environment variables:

```bash
export PowerShellConfiguration__Environment__StartupScriptPath=/app/startup.ps1
```

Both `StartupScript` (inline string) and `StartupScriptPath` (file) execute sequentially if both are configured (file first, then inline). Because both run for every created worker, external side effects will duplicate. We recommend configuring only one unless you specifically require composition (e.g., using a file for base setup and the inline script for overrides).

---

## Safe patterns

### Define functions and aliases

Defining functions and aliases is the primary use case for startup scripts.
Functions are not preserved across HTTP tool calls (the worker is reset), but
they are available within each call while the worker is warm:

```powershell
# startup.ps1 — define utility functions for all workers
function Get-HealthCheck {
    param([string[]]$ServiceNames = @('wuauserv', 'spooler'))
    $ServiceNames | ForEach-Object {
        $svc = Get-Service -Name $_ -ErrorAction SilentlyContinue
        [PSCustomObject]@{
            Service   = $_
            Status    = if ($svc) { $svc.Status } else { 'NotFound' }
        }
    }
}

Set-Alias -Name health -Value Get-HealthCheck
Write-Host "Startup complete: health function registered."
```

### Load modules idempotently

Import modules using `-ErrorAction SilentlyContinue` (or a `try/catch` for
required modules) so the script does not fail on a missing optional module:

```powershell
# startup.ps1 — idempotent module load
$required = @('Az.Accounts')
$optional = @('Az.Resources', 'Az.Storage')

foreach ($mod in $required) {
    Import-Module $mod -ErrorAction Stop
}
foreach ($mod in $optional) {
    if (Get-Module -ListAvailable -Name $mod) {
        Import-Module $mod -ErrorAction SilentlyContinue
    }
}
```

### Read environment configuration

Environment variables are safe to read; they are read-only from the script's
perspective and do not cause contention between parallel workers:

```powershell
# startup.ps1 — read environment values
$tenantId  = $env:AZURE_TENANT_ID
$companyNm = $env:COMPANY_NAME ?? 'Acme Corporation'

# Set script-local constants (available within this worker's lifetime)
Set-Variable -Name CompanyName -Value $companyNm -Option ReadOnly -Scope Global
```

### Connect to Azure with Managed Identity

Connecting to Azure in the startup script is safe if the connection is
per-worker (each worker connects independently) and the connection attempt
uses `-ErrorAction Stop` so failures evict the worker rather than silently
continuing with an unauthenticated context:

```powershell
# startup.ps1 — Azure Managed Identity setup
if ($env:AZURE_CLIENT_ID) {
    try {
        $null = Connect-AzAccount -Identity -AccountId $env:AZURE_CLIENT_ID -ErrorAction Stop
        Write-Host "Connected to Azure with Managed Identity $($env:AZURE_CLIENT_ID)"
    } catch {
        Write-Error "Azure connection failed: $_"
        throw  # fail the worker; pool will replenish
    }
} elseif (Test-Path env:AZURE_TENANT_ID) {
    try {
        $null = Connect-AzAccount -Identity -ErrorAction Stop
        Write-Host "Connected to Azure with system-assigned Managed Identity"
    } catch {
        Write-Error "Azure connection failed: $_"
        throw
    }
}
```

### Set PowerShell preferences

Setting `$ErrorActionPreference`, `$ProgressPreference`, and similar
preference variables is safe and idempotent:

```powershell
$ErrorActionPreference   = 'Continue'
$ProgressPreference      = 'SilentlyContinue'
$VerbosePreference       = 'SilentlyContinue'
$InformationPreference   = 'Continue'
```

---

## Anti-patterns

### Writing to shared external state

Avoid writes that conflict when multiple workers initialize concurrently:

```powershell
# ❌ Anti-pattern: concurrent workers may conflict on the lock file
Set-Content "/tmp/poshmcp.lock" (Get-Date)

# ✓ Use environment variables, per-worker in-process state, or idempotent
# remote writes (e.g., Azure Blob with upsert/conditional semantics) instead.
```

### Assuming a specific execution count

Do not write code that behaves differently based on how many times the script
has run in the process. Workers are replenished dynamically; the script may run
more or fewer times than you expect:

```powershell
# ❌ Anti-pattern: assumes exactly N runs
$global:WorkerCount++  # meaningless — resets with each worker

# ✓ Each worker is independent; design each initialization as if it is the first.
```

### Cross-call mutable global state (HTTP)

Setting mutable globals that tool code then reads across calls is safe only
in **stdio** mode. In HTTP mode, the worker is reset after each call:

```powershell
# ❌ Anti-pattern for HTTP: assumes state set in Call 1 is visible in Call 2
$Global:UserId = "user@example.com"  # reset before the next HTTP call

# ✓ Pass user identity explicitly via tool parameters or derive from
# authentication context within each tool call.
```

### External side effects that should happen once per process

Startup scripts may run multiple times per process (once per worker). Use a
process-level `[System.Threading.Interlocked]` gate or a one-time flag only
if the side effect is idempotent on re-run:

```powershell
# ❌ Anti-pattern: registers event handler multiple times
Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action { ... }

# ✓ Check if already registered, or use -SupportEvent and a flag, or avoid
# engine events in startup scripts entirely.
```

### Blocking or long-running initializations without timeout

Startup scripts that block indefinitely prevent the worker from entering the
pool. Use `-Timeout` or `Invoke-Command` with a timeout where possible:

```powershell
# ❌ Anti-pattern: no timeout; blocks worker if DNS/network is slow
$result = Invoke-RestMethod "https://internal-config-service/api/config"

# ✓ Wrap in a timeout or use -TimeoutSec
try {
    $result = Invoke-RestMethod "https://internal-config-service/api/config" `
        -TimeoutSec 10 -ErrorAction Stop
} catch {
    Write-Warning "Could not load remote config: $_. Using defaults."
}
```

---

## Failure behavior

If a startup script throws a terminating error (`throw`, `Write-Error -EA Stop`,
or an unhandled exception), the worker initialization fails:
- The worker is **evicted** without entering the pool.
- A warning is logged: `Worker startup failed; evicting without entering pool.`
- The pool replenishes to replace the failed worker (up to `MaxPoolSize`).

For required resources (like Azure authentication), **let failures terminate the
worker** so the pool does not silently serve unauthenticated requests:

```powershell
# Required connection: throw on failure so the worker is evicted
try {
    $null = Connect-AzAccount -Identity -ErrorAction Stop
} catch {
    throw "Required Azure authentication failed: $_"
}
```

For optional resources, catch and log the exception so the worker enters the
pool in a degraded-but-functional state:

```powershell
# Optional enrichment: warn but continue
try {
    Import-Module Az.Resources -ErrorAction Stop
} catch {
    Write-Warning "Az.Resources not available: optional enrichment disabled."
}
```

---

## Observability

Startup script execution is visible in logs:

| Log event | Level | When |
|-----------|-------|------|
| `Starting StatelessRunspacePool: min={Min}, max={Max}, eager={Eager}.` | Information | Pool start |
| `Startup complete: {Warm}/{Eager} workers initialized.` | Information | After eager warm-up |
| `Worker startup failed; evicting without entering pool.` | Warning | Script threw |
| `Worker {CreatedAt} initialized and enqueued. Total={Total}.` | Debug | Worker ready |

Set `Logging:LogLevel:Default` to `Debug` to see individual worker initialization events.

For pool health at runtime:

```bash
curl https://poshmcp.example/health
curl https://poshmcp.example/health/ready
```

`/health/ready` passes only when the pool reaches its minimum capacity (`WarmWorkers + LeasedWorkers >= MinPoolSize`). Workers that are still resetting or being created do not count. At startup, the common case is that all minimum capacity is warm. If the endpoint remains unhealthy, check logs for startup-script failure warnings.

---

## Testing startup scripts locally

Validate your script before deploying:

```powershell
# 1. Syntax check
$null = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path "startup.ps1").Path, [ref]$null, [ref]$errors
)
if ($errors.Count -gt 0) { $errors; throw "Parse errors" }

# 2. Dry-run in a fresh runspace (simulates per-worker init)
$rs = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
$rs.Open()
$ps = [System.Management.Automation.PowerShell]::Create()
$ps.Runspace = $rs
$null = $ps.AddScript((Get-Content "startup.ps1" -Raw))
$ps.Invoke()
if ($ps.HadErrors) { $ps.Streams.Error | ForEach-Object { Write-Error $_ }; throw "Script errors" }
$rs.Close()
Write-Host "Startup script validated successfully."
```

Run the server with a low `EagerWarmCount` in a local Docker container and
inspect `poshmcp doctor` output to confirm the pool is warm:

```bash
docker run --rm -p 8080:8080 \
  -e POSHMCP_TRANSPORT=http \
  -e McpServer__RunspacePool__EagerWarmCount=1 \
  -v ./startup.ps1:/app/startup.ps1 \
  -e PowerShellConfiguration__Environment__StartupScriptPath=/app/startup.ps1 \
  poshmcp:latest

# In another terminal, after the server starts:
curl -s http://localhost:8080/health/ready | jq .status
```

---

## See also

- [Session Management](session-management.md) — execution state model and pool lifecycle
- [Configuration Guide](configuration.md#startup-scripts) — configuration keys
- [Environment Customization](environment.md) — environment variable setup
- [Advanced Configuration](advanced.md) — dynamic tool reload and multi-module setup
- [Migration Guide](migration-v1-v2.md) — startup-script changes from v1 to v2
