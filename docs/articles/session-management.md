---
uid: session-management
title: Session Management
---

# Session Management

PoshMcp manages PowerShell runspaces and session state for each connection.

## Persistent State

Variables and functions persist across multiple calls within the same session.

```powershell
# Call 1: Set a variable
$MyData = @{
    Timestamp = Get-Date
    Records = Get-Process | Select-Object -First 5
}

# Call 2: Access the variable (same session)
$MyData.Timestamp
# Output: [date from Call 1]
```

## Per-User Isolation (HTTP Mode)

Each user maintains independent state:

```powershell
# User A's session
$Global:UserId = "user-a@company.com"
$Global:Data = @{ ... }

# User B's session (separate runspace)
$Global:UserId = "user-b@company.com"
$Global:Data = @{ ... }
# User B never sees User A's data
```

## Session Timeouts

Runspaces are cleaned up after inactivity (default: 30-60 minutes).

Configure timeout:

```bash
export POSHMCP_SESSION_TIMEOUT_MINUTES=120
```

## Startup Scripts

Run PowerShell code when a new session starts.

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
