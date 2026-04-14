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

poshmcp update-config --add-install-module Az.Accounts --minimum-version 2.0.0
poshmcp update-config --add-install-module Az.Compute

poshmcp update-config --add-import-module Az.Accounts
poshmcp update-config --add-import-module Az.Compute

poshmcp update-config --trust-psgallery
poshmcp update-config --skip-publisher-check
```

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

Run PowerShell in a separate process for better module isolation:

```bash
export POSHMCP_RUNTIME_MODE=out-of-process
poshmcp serve --transport http
```

Requires PowerShell 7.x for out-of-process mode.

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
