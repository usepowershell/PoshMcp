---
uid: resources-and-prompts
title: Resources and Prompts Guide
---

# Resources and Prompts Guide

Expose contextual information and reusable prompt templates to AI agents through PoshMcp's MCP Resources and Prompts capabilities.

## Overview

### MCP Resources

**Resources** allow AI agents to read static or dynamic content without invoking PowerShell commands. Perfect for:

- **Configuration context**: Expose runbooks, deployment guides, or policies
- **Live system state**: Serve processes, services, or Azure resources on-demand
- **Audit trails**: Expose logs or event data for analysis
- **Infrastructure inventory**: Share topology or resource listings

Two sources:
- **File-based**: Read from disk (config files, markdown docs, JSON)
- **Command-based**: Execute PowerShell and return output (live processes, service status)

### MCP Prompts

**Prompts** are reusable templates with named arguments that AI agents can invoke. Perfect for:

- **Standardized workflows**: Define consistent runbook execution patterns
- **Dynamic context injection**: Pass runtime values (service names, resource IDs) into prompts
- **Compliance templates**: Encode organizational best practices
- **Multi-step guides**: Create templates that orchestrate complex investigations

Prompts inject arguments as PowerShell variables, enabling dynamic rendering.

---

## MCP Resources

### Configuration

Add resources to `appsettings.json`:

```json
{
  "McpResources": {
    "Resources": [
      {
        "Uri": "poshmcp://resources/deployment-guide",
        "Name": "Deployment Guide",
        "Description": "Standard deployment procedures for production",
        "MimeType": "text/markdown",
        "Source": "file",
        "Path": "docs/DEPLOYMENT.md"
      },
      {
        "Uri": "poshmcp://resources/running-processes",
        "Name": "Running Processes",
        "Description": "Live list of running processes",
        "MimeType": "application/json",
        "Source": "command",
        "Command": "Get-Process | Select-Object Name, Id, WorkingSet | ConvertTo-Json -Depth 2"
      }
    ]
  }
}
```

### Schema Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Uri` | string | Yes | Unique resource identifier, typically `poshmcp://resources/{id}` |
| `Name` | string | Yes | Human-readable display name |
| `Description` | string | Yes | What the resource contains (shown in `resources/list`) |
| `MimeType` | string | Yes | Content type: `text/plain`, `text/markdown`, `application/json`, etc. |
| `Source` | string | Yes | `"file"` or `"command"` |
| `Path` | string | If Source="file" | File path (relative to appsettings.json directory, or absolute) |
| `Command` | string | If Source="command" | PowerShell command to execute |

### Example: File-Based Resources

Expose documentation files for AI context:

```json
{
  "McpResources": {
    "Resources": [
      {
        "Uri": "poshmcp://resources/runbook-vm-restart",
        "Name": "VM Restart Runbook",
        "Description": "Standard procedure for restarting Azure VMs",
        "MimeType": "text/markdown",
        "Source": "file",
        "Path": "runbooks/restart-vm.md"
      },
      {
        "Uri": "poshmcp://resources/compliance-checklist",
        "Name": "Security Compliance Checklist",
        "Description": "Required checks before production deployment",
        "MimeType": "text/markdown",
        "Source": "file",
        "Path": "docs/compliance/checklist.md"
      }
    ]
  }
}
```

File paths are resolved relative to the directory containing `appsettings.json`. Use absolute paths for resources outside the config directory:

```json
{
  "Uri": "poshmcp://resources/external-guide",
  "Source": "file",
  "Path": "/shared/guides/architecture.md"
}
```

### Example: Command-Based Resources

Serve live system data:

```json
{
  "McpResources": {
    "Resources": [
      {
        "Uri": "poshmcp://resources/critical-services",
        "Name": "Critical Services Status",
        "Description": "Real-time status of critical Windows services",
        "MimeType": "application/json",
        "Source": "command",
        "Command": "Get-Service -ServiceName 'wuauserv', 'spooler', 'w32time' | Select-Object Name, Status, StartType | ConvertTo-Json"
      },
      {
        "Uri": "poshmcp://resources/azure-resource-inventory",
        "Name": "Azure Resource Inventory",
        "Description": "List of Azure resources in production subscription",
        "MimeType": "application/json",
        "Source": "command",
        "Command": "Get-AzResource -ResourceGroupName 'prod-*' | Select-Object Name, ResourceType, Location, Id | ConvertTo-Json -Depth 2"
      },
      {
        "Uri": "poshmcp://resources/disk-usage",
        "Name": "Disk Usage Report",
        "Description": "Free disk space across all drives",
        "MimeType": "application/json",
        "Source": "command",
        "Command": "Get-Volume | Where-Object DriveLetter | Select-Object DriveLetter, @{Label='SizeGB';Expression={$_.Size/1GB}}, @{Label='FreeGB';Expression={$_.SizeRemaining/1GB}} | ConvertTo-Json"
      }
    ]
  }
}
```

### MCP Methods

#### `resources/list`

Discover all configured resources.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "resources/list"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "resources": [
      {
        "uri": "poshmcp://resources/deployment-guide",
        "name": "Deployment Guide",
        "description": "Standard deployment procedures",
        "mimeType": "text/markdown"
      },
      {
        "uri": "poshmcp://resources/running-processes",
        "name": "Running Processes",
        "description": "Live list of running processes",
        "mimeType": "application/json"
      }
    ]
  }
}
```

#### `resources/read`

Read a specific resource by URI.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "resources/read",
  "params": {
    "uri": "poshmcp://resources/deployment-guide"
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "contents": [
      {
        "uri": "poshmcp://resources/deployment-guide",
        "mimeType": "text/markdown",
        "text": "# Deployment Guide\n\n1. Pre-check list...\n2. Execute migration...\n"
      }
    ]
  }
}
```

### Command-Based Resource Best Practices

**Performance:**
- Keep PowerShell commands fast; resources are executed on every `resources/read` call
- Use targeted filtering: `Get-Process -Name 'svchost'` instead of `Get-Process | Where-Object ...`
- Consider caching implications if resources are read frequently

**Error handling:**
- PowerShell errors in command-based resources return MCP error responses
- Use `-ErrorAction Stop` to fail fast on errors
- Test commands independently before adding to configuration

**Output formatting:**
- Use `ConvertTo-Json -Depth 2` for nested objects
- Use `Select-Object` to limit output size
- Use `Format-Table` or `ConvertTo-Csv` for large datasets

**Security:**
- Resources are visible to any MCP client that can connect
- Do not expose secrets, API keys, or sensitive data
- Consider resource URIs as part of your security boundary

---

## MCP Prompts

### Configuration

Add prompts to `appsettings.json`:

```json
{
  "McpPrompts": {
    "Prompts": [
      {
        "Name": "analyze-service",
        "Description": "Analyze Windows service status and health",
        "Arguments": [
          {
            "Name": "serviceName",
            "Description": "Name of the Windows service",
            "Required": true
          },
          {
            "Name": "detail",
            "Description": "Level of detail (basic, advanced)",
            "Required": false
          }
        ],
        "Command": "Get-Service -Name $serviceName | Format-List *"
      }
    ]
  }
}
```

### Schema Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Name` | string | Yes | Prompt identifier (must be unique) |
| `Description` | string | Yes | Purpose of the prompt (shown in `prompts/list`) |
| `Arguments` | array | No | Array of argument definitions |
| `Arguments[].Name` | string | Yes | Argument name (used as PowerShell variable: `$name`) |
| `Arguments[].Description` | string | Yes | What the argument is for |
| `Arguments[].Required` | boolean | No | Whether the argument must be provided (default: false) |
| `Command` | string | Yes | PowerShell command with `$variableName` placeholders |

### Example: Simple Prompts

Basic prompt with required argument:

```json
{
  "Name": "check-process",
  "Description": "Get details of a running process",
  "Arguments": [
    {
      "Name": "processName",
      "Description": "Process name to check",
      "Required": true
    }
  ],
  "Command": "Get-Process -Name $processName | Format-List *"
}
```

Prompt with optional arguments:

```json
{
  "Name": "list-services",
  "Description": "List Windows services with optional filtering",
  "Arguments": [
    {
      "Name": "pattern",
      "Description": "Filter services by name pattern (e.g., 'sql*')",
      "Required": false
    }
  ],
  "Command": "if ([string]::IsNullOrEmpty($pattern)) { Get-Service } else { Get-Service -Name $pattern -ErrorAction SilentlyContinue }"
}
```

### Example: Complex Prompts

Multi-argument prompt for incident investigation:

```json
{
  "McpPrompts": {
    "Prompts": [
      {
        "Name": "investigate-incident",
        "Description": "Investigate a specific incident event",
        "Arguments": [
          {
            "Name": "eventId",
            "Description": "Event ID to investigate",
            "Required": true
          },
          {
            "Name": "logName",
            "Description": "Event log name (System, Application, Security)",
            "Required": false
          },
          {
            "Name": "hours",
            "Description": "Look back this many hours (default: 24)",
            "Required": false
          }
        ],
        "Command": "$log = if ([string]::IsNullOrEmpty($logName)) { 'System' } else { $logName }\n$lookback = if ([string]::IsNullOrEmpty($hours)) { 24 } else { [int]$hours }\n$startTime = (Get-Date).AddHours(-$lookback)\nGet-EventLog -LogName $log -EventId $eventId -After $startTime | Select-Object TimeGenerated, EventId, Source, Message | Format-Table -AutoSize"
      }
    ]
  }
}
```

### MCP Methods

#### `prompts/list`

Discover all configured prompts.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "prompts/list"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "prompts": [
      {
        "name": "analyze-service",
        "description": "Analyze Windows service status and health",
        "arguments": [
          {
            "name": "serviceName",
            "description": "Name of the Windows service",
            "required": true
          }
        ]
      }
    ]
  }
}
```

#### `prompts/get`

Retrieve a prompt with arguments rendered.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "prompts/get",
  "params": {
    "name": "analyze-service",
    "arguments": {
      "serviceName": "svchost"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "messages": [
      {
        "role": "user",
        "content": {
          "type": "text",
          "text": "Analyze Windows service status and health\n\nService name: svchost\n\n---\n\n[PowerShell output from: Get-Service -Name svchost | Format-List *]"
        }
      }
    ]
  }
}
```

### Prompt Best Practices

**Argument handling:**
- Use meaningful names: `serviceName`, `resourceId`, `environment` (not `arg1`, `arg2`)
- Provide clear descriptions for required arguments
- Use optional arguments for filtering or configuration
- Validate arguments in PowerShell (check if service exists, resource is valid)

**Command design:**
- Keep prompts focused: one investigation, analysis, or action per prompt
- Use readable variable substitution: `$serviceName`, not `$svc`
- Handle missing optional arguments gracefully with `if`/`else` logic
- Return structured output: use `Select-Object` or `ConvertTo-Json`

**Error handling:**
- Prompt PowerShell errors return MCP error responses
- Use `-ErrorAction SilentlyContinue` for non-critical failures
- Return meaningful error messages (don't let PowerShell errors propagate raw)

**Security:**
- Prompts execute in the shared PowerShell runspace with full command access
- Do not expose prompts for destructive commands (Remove-*, Reset-*, etc.)
- Validate user-supplied arguments before interpolating into dangerous commands
- Consider RBAC: filter by user identity if exposed via HTTP

---

## Configuration Validation

Use `poshmcp doctor` to validate your resources and prompts configuration:

```bash
poshmcp doctor
```

Doctor checks include:

**Resources:**
- Duplicate URIs
- File paths exist and are readable
- PowerShell command syntax is valid
- MimeType values are recognized

**Prompts:**
- Duplicate names
- Required arguments are documented
- PowerShell command syntax is valid
- Variable references in commands match argument names

**Output:**
```
Validation Results
==================

Resources: 3 configured
  ✓ poshmcp://resources/deployment-guide — file OK
  ✓ poshmcp://resources/running-processes — command OK
  ✓ poshmcp://resources/audit-log — file OK

Prompts: 2 configured
  ✓ analyze-service — 1 required arg
  ✓ investigate-incident — 3 args

No issues found.
```

---

## Common Patterns

### Pattern 1: Expose Configuration Files

Share runbooks or policies as grounded context:

```json
{
  "Uri": "poshmcp://resources/production-runbook",
  "Name": "Production Runbook",
  "Description": "Runbook for production deployments",
  "MimeType": "text/markdown",
  "Source": "file",
  "Path": "docs/production-runbook.md"
}
```

### Pattern 2: Live System State

Serve on-demand snapshots of system health:

```json
{
  "Uri": "poshmcp://resources/health-status",
  "Name": "System Health Status",
  "Description": "Current system health metrics",
  "MimeType": "application/json",
  "Source": "command",
  "Command": "$cpu = (Get-Counter '\\Processor(_Total)\\% Processor Time' -SampleInterval 1 -MaxSamples 1).CounterSamples[0].CookedValue\n$mem = Get-CimInstance Win32_OperatingSystem | Select-Object @{Label='MemUsedGB';Expression={($_.TotalVisibleMemorySize-$_.FreePhysicalMemory)/1MB}}, @{Label='MemTotalGB';Expression={$_.TotalVisibleMemorySize/1MB}}\n@{ cpu = $cpu; memory = $mem } | ConvertTo-Json"
}
```

### Pattern 3: Parameterized Analysis

Reusable templates for common investigations:

```json
{
  "Name": "analyze-disk",
  "Description": "Analyze disk usage for a specific drive",
  "Arguments": [
    {
      "Name": "drive",
      "Description": "Drive letter (C, D, etc.)",
      "Required": true
    }
  ],
  "Command": "$disk = Get-Volume -DriveLetter $drive -ErrorAction Stop\n$dir = \"$drive`:\"\n$top = Get-ChildItem -Path $dir -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum\n@{\n  disk = $disk | Select-Object DriveLetter, Size, SizeRemaining\n  topDir = $top | Select-Object Count, Sum\n} | ConvertTo-Json"
}
```

### Pattern 4: Multi-Step Workflows

Prompts that orchestrate complex investigations:

```json
{
  "Name": "audit-permissions",
  "Description": "Audit permissions for a resource",
  "Arguments": [
    {
      "Name": "resourceId",
      "Description": "Azure resource ID",
      "Required": true
    }
  ],
  "Command": "$resource = Get-AzResource -ResourceId $resourceId\n$assignments = Get-AzRoleAssignment -Scope $resourceId | Select-Object RoleDefinitionName, DisplayName, Scope\n$perms = @{\n  resource = $resource | Select-Object Name, ResourceType, Location\n  assignments = $assignments\n}\n$perms | ConvertTo-Json -Depth 2"
}
```

---

## Troubleshooting

### Resource read fails with "file not found"

**Symptom:** `resources/read` returns error "Path does not exist"

**Solution:** 
- Check that file path is relative to `appsettings.json` directory, or use an absolute path
- Verify file permissions (PoshMcp process must be able to read the file)

```json
"Path": "docs/guide.md"  // Relative to appsettings.json
"Path": "/etc/poshmc/config.json"  // Absolute path
```

### Command-based resource returns PowerShell errors

**Symptom:** `resources/read` returns error with PowerShell exception

**Solution:**
- Test command independently: `pwsh -Command 'Your-Command'`
- Add error handling: `Get-Service -Name $name -ErrorAction Stop`
- Check that modules are loaded or imported in `appsettings.json`

### Prompt arguments not being substituted

**Symptom:** Prompt output shows `$variableName` literally instead of the argument value

**Solution:**
- Verify argument name matches variable in command: `Arguments[].Name` = `ServiceName` → `$ServiceName` in command
- Use double quotes in PowerShell: `"$variable"`, not `'$variable'`
- Check `prompts/get` request includes correct argument names in `params.arguments`

### Doctor reports duplicate URIs

**Symptom:** `poshmcp doctor` output: "Duplicate URI: poshmcp://resources/my-resource"

**Solution:**
- Each resource must have a unique URI
- Search `appsettings.json` for duplicate URIs: `"Uri": "poshmcp://resources/..."`
- Rename conflicting resources

---

## See Also

- [Configuration Guide](configuration.md) — Full appsettings.json schema
- [Release Notes 0.6.0](../release-notes/0.6.0.md) — What's new in this release
- [MCP Specification](https://modelcontextprotocol.io) — Official MCP protocol documentation
- [Getting Started](getting-started.md) — Installation and first steps
