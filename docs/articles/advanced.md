---
uid: advanced
title: Advanced Configuration
---

# Advanced Configuration

Advanced topics for power users and enterprise deployments.

## Custom Startup Scripts

Define PowerShell functions and utilities available to all sessions:

```powershell
# startup.ps1
function Get-HealthCheck {
    $critical = @('wuauserv', 'spooler', 'w32time')
    $critical | ForEach-Object {
        $service = Get-Service -Name $_
        [PSCustomObject]@{
            Service = $service.Name
            Status = $service.Status
            StartType = $service.StartType
        }
    }
}

Write-Host "✓ Custom utilities loaded" -ForegroundColor Green
```

## Multi-Module Configuration

Expose tools from multiple modules with module-specific settings:

```bash
poshmcp update-config --add-include-pattern "Get-AzVM"
poshmcp update-config --add-include-pattern "Get-AzResource"
poshmcp update-config --add-include-pattern "Get-Service"

poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "Invoke-*"

poshmcp update-config --add-module Az.Accounts
poshmcp update-config --add-module Az.Compute
```

## Azure Scaffold Workflow

For Azure-first deployments, use the scaffold workflow documented in [Azure Integration](azure-integration.md) to generate `infra/azure` assets with `poshmcp scaffold` and then run `deploy.ps1` from the generated folder.

## Performance Tuning

### Enable Result Caching

Cache command output for repeated queries:

```bash
poshmcp update-config --enable-result-caching true
```

### Use Default Display Properties

Leverage PowerShell's default formatting for faster output:

```bash
# In appsettings.json
{
  "PowerShellConfiguration": {
    "Performance": {
      "UseDefaultDisplayProperties": true
    }
  }
}
```

### Pre-install Modules in Docker

```bash
docker build \
  --build-arg MODULES="Az.Accounts Az.Resources Az.Storage" \
  -t poshmcp:optimized .
```

## Out-of-Process PowerShell

Run PowerShell in a separate `pwsh` subprocess so that heavy or conflicting modules (Az, Microsoft.Graph) cannot crash or destabilize the MCP server. Requires PowerShell 7.x on `PATH`.

### Enable Out-of-Process Mode

Set `RuntimeMode` in `appsettings.json`:

```json
{
  "PowerShellConfiguration": {
    "RuntimeMode": "OutOfProcess"
  }
}
```

Or via environment variable:

```bash
export POSHMCP_RUNTIME_MODE=OutOfProcess
poshmcp serve --transport http
```

The environment variable takes precedence over the config file value. Valid values: `InProcess`, `OutOfProcess` (kebab-case `in-process` / `out-of-process` is also accepted by the env var and CLI). Unrecognized values cause the server to fail startup with `InvalidOperationException` (`Unsupported runtime mode '<value>'. Supported runtime modes: in-process, out-of-process.`).

### Subprocess Host Modes

When `RuntimeMode` is `OutOfProcess`, `SubprocessHostMode` selects the subprocess topology:

| `SubprocessHostMode` | Topology | When to use |
|----------------------|----------|-------------|
| `Pool` (default) | One subprocess, runspace pool inside it | Default since 2026-05-06. Best concurrent warm-invoke throughput; recommended for typical MCP workloads. |
| `ProcessPool` | N independent single-runspace subprocess hosts, leased per request | Trust-boundary or tail-latency-sensitive workloads. Crashes are reconciled per slot without disturbing other hosts. |
| `Single` | One subprocess, one runspace, serialized requests | Backward-compatible legacy mode. Useful for bisecting regressions or when only cold-start latency matters. |

The default flip from `Single` to `Pool` was driven by benchmark study — `Pool` posted ~4.86× warm-invoke throughput at concurrency 10 versus `Single`. See [`specs/004-out-of-process-execution/benchmark-findings.md`](https://github.com/usepowershell/PoshMcp/blob/main/specs/004-out-of-process-execution/benchmark-findings.md) for full data.

### Pool Sizing

```json
{
  "PowerShellConfiguration": {
    "RuntimeMode": "OutOfProcess",
    "SubprocessHostMode": "Pool",
    "SubprocessRunspacePoolSize": 0
  }
}
```

- `SubprocessRunspacePoolSize` (Pool mode): runspaces in the pool. `0` (default) auto-sizes to `min(ProcessorCount, 8)`. Ignored in `Single` and `ProcessPool` modes.
- `SubprocessPoolSize` (ProcessPool mode): independent subprocess hosts to launch. Default `4`.
- `SubprocessMinHealthyForStartup` (ProcessPool mode): minimum healthy hosts required for the pool to start. Clamped to `[1, SubprocessPoolSize]`. Default `1`.

ProcessPool example for tenant isolation:

```json
{
  "PowerShellConfiguration": {
    "RuntimeMode": "OutOfProcess",
    "SubprocessHostMode": "ProcessPool",
    "SubprocessPoolSize": 4,
    "SubprocessMinHealthyForStartup": 2
  }
}
```

### Cancellation

The MCP request `CancellationToken` propagates into the subprocess channel for all three host modes via a `cancel` control frame sent by `OutOfProcessHost.SendRequestAsync`. The host attempts a cooperative `BeginStop` on the in-flight pipeline; the .NET awaiter completes with `OperationCanceledException` immediately without waiting for the host to acknowledge. Per-mode behavior:

- `Pool`: `PoolDispatcher` looks up the active `[powershell]` instance by request id and calls `BeginStop` on it. The runspace is returned to the pool when the pipeline unwinds, so pool capacity recovers without restart. Pool capacity under stuck-but-cancelled invokes is `N - in_flight_uncancelled`.
- `ProcessPool`: each leased host runs the Single-mode script and inherits the same soft-cancel via the inherited `OutOfProcessHost` cancel frame. If the host honors `BeginStop`, the slot stays healthy and is returned to the pool; other hosts are unaffected. The existing per-request kill-on-timeout path in `OutOfProcessSubprocessPool` remains as a backstop for wedged hosts (e.g., a cmdlet stuck in unmanaged code) that do not honor `BeginStop` within the per-request timeout.
- `Single`: `SingleDispatcher` runs the invoke on a background dispatcher thread and calls `BeginStop` on the matching `[powershell]` instance when the cancel frame arrives. The host stays healthy for follow-up requests; the per-request timeout serves as the backstop and recycles the host only if `BeginStop` does not unwind the pipeline in time.

### Diagnostics

`poshmcp doctor` reports the resolved host mode, effective pool sizes, host-script path, and any clamp warnings under Runtime Settings.

## Dynamic Tool Reloading

Automatically reload tool definitions when configuration changes (experimental):

```bash
# In appsettings.json
{
  "PowerShellConfiguration": {
    "EnableDynamicReloadTools": true
  }
}
```

**Note:** This can be slow; use sparingly.

---

**See also:** [Configuration Guide](configuration.md) | [Security](security.md)
