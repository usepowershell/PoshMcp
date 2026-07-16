---
uid: session-management
title: Session Management
---

# Session Management

PoshMcp manages PowerShell runspaces and session state for each MCP session.

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

Each HTTP MCP session maintains independent state:

```powershell
# User A's session
$Global:UserId = "user-a@company.com"
$Global:Data = @{ ... }

# User B's session (separate runspace)
$Global:UserId = "user-b@company.com"
$Global:Data = @{ ... }
# User B never sees User A's data
```

## HTTP Session Lifecycle

In HTTP mode, a request with `Mcp-Session-Id` receives one clean initialized
runspace that is never shared with another session. Requests without that
header use a one-shot runspace and do not preserve state. Session runspaces
are released when the SDK session completes, the client sends `DELETE`, or the
runspace remains idle for its configured TTL. A release requested while a tool
is running waits for that invocation to finish.

The defaults are a 60-second MCP session idle timeout, 16 total owned
runspaces, a 300-second runspace idle TTL, a 30-second sweep interval, two
warm standbys, and a 15-second acquisition timeout. Capacity includes warm
standbys and one-shot requests. When capacity is full, a request waits up to
the acquisition timeout and then fails rather than receiving another
session's runspace.

Configure session behavior under `McpServer`:

```json
{
  "McpServer": {
    "IdleSessionTimeoutSeconds": 120,
    "SessionRunspaceCapacity": 24,
    "SessionRunspaceWarmStandbyCount": 2,
    "SessionRunspaceAcquisitionTimeoutSeconds": 15
  }
}
```

For the complete setting reference and operational sizing guidance, see
[Configuration](configuration.md#mcp-server-http-sessions). Dynamic tool
reload releases managed HTTP session runspaces, so clients must create a new
session and should not expect in-session PowerShell state to survive a reload.

## Startup Scripts

Run PowerShell code when PoshMcp creates a clean runspace. This includes warm
standbys before they are assigned to a session.

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
