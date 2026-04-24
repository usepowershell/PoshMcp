---
uid: examples
title: Examples & Recipes
---

# Examples & Recipes

Real-world scenarios and practical recipes for PoshMcp.

For comprehensive examples and detailed recipes, see user-guide.md in the repository.

## Example 1: Service Health Check Tool

Expose service management commands:

```bash
poshmcp create-config
poshmcp update-config --add-command Get-Service
poshmcp update-config --add-command Restart-Service
poshmcp update-config --add-exclude-pattern "*-Dangerous*"
```

**Usage:** "Are the Windows Update and Print Spooler services running?"

## Example 2: Azure Resource Explorer

Expose Azure management commands:

```bash
poshmcp create-config
poshmcp update-config --add-command Get-AzResource
poshmcp update-config --add-command Get-AzResourceGroup
poshmcp update-config --add-command Get-AzVM

poshmcp update-config --add-module Az.Accounts
poshmcp update-config --add-module Az.Resources
```

**Startup script:**

```powershell
Connect-AzAccount -Identity -ErrorAction Stop
```

**Usage:** "How many VMs do we have in the East US region?"

## Example 3: Custom Utility Functions

Define company-specific tools via startup script:

```powershell
# startup.ps1
function Get-HealthCheck {
    $critical = @('wuauserv', 'spooler', 'w32time')
    $critical | ForEach-Object {
        Get-Service -Name $_ | Select-Object Name, Status, StartType
    }
}

function Get-SystemInfo {
    [PSCustomObject]@{
        ComputerName = $env:COMPUTERNAME
        OSVersion = [System.Environment]::OSVersion.VersionString
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        InstallTime = (Get-Item "C:\Windows\System32\kernel32.dll").CreationTime
    }
}
```

**Usage:** "Run a health check on critical services"

## Example 4: Multi-Module Setup

Expose Azure, storage, and compute commands:

```bash
poshmcp create-config

# Include specific commands
poshmcp update-config --add-include-pattern "Get-AzVM"
poshmcp update-config --add-include-pattern "Get-AzResource"
poshmcp update-config --add-include-pattern "Get-Service"
poshmcp update-config --add-include-pattern "Get-Process"

# Exclude dangerous patterns
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "Invoke-*"

# Add modules for discovery
poshmcp update-config --add-module Az.Accounts
poshmcp update-config --add-module Az.Compute
```

## Example 5: Container Deployment

Custom Dockerfile with pre-configured modules:

```dockerfile
FROM poshmcp:latest

COPY appsettings.json /app/appsettings.json
COPY startup.ps1 /app/startup.ps1

ENV POSHMCP_CONFIGURATION=/app/appsettings.json
ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["poshmcp", "serve", "--transport", "http"]
```

Build and run:

```bash
docker build -t myorg/poshmcp:production .
docker run -d -p 8080:8080 --name poshmcp-prod myorg/poshmcp:production
```

---

**See also:** [Getting Started](getting-started.md) | [Docker Deployment](docker.md) | user-guide.md in the repository
