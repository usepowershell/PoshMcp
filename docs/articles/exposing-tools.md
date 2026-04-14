---
uid: exposing-tools
title: Exposing PowerShell Tools
---

# Exposing PowerShell Tools

PoshMcp discovers PowerShell commands and transforms them into AI-consumable tools automatically.

## How It Works

1. **Discovery** — reads available PowerShell commands via `Get-Command`
2. **Inspection** — extracts metadata: name, synopsis, parameters, output type
3. **Schema generation** — creates JSON schema for AI consumption
4. **Registration** — exposes tool via MCP protocol
5. **Execution** — invokes the command and returns structured results

## Configuration Methods

### Specific Commands (Whitelist)

Expose only the commands you want:

```bash
poshmcp update-config --add-function Get-Service
poshmcp update-config --add-function Restart-Service
poshmcp update-config --add-function Get-Process
```

**Configuration:**

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Service",
      "Restart-Service",
      "Get-Process"
    ]
  }
}
```

### Include Patterns (Wildcard)

Expose groups of commands matching patterns:

```bash
poshmcp update-config --add-include-pattern "Get-*"
poshmcp update-config --add-include-pattern "Set-Service"
poshmcp update-config --add-include-pattern "Restart-*"
```

**Configuration:**

```json
{
  "PowerShellConfiguration": {
    "IncludePatterns": [
      "Get-*",
      "Set-Service",
      "Restart-*"
    ]
  }
}
```

### Exclude Patterns (Blacklist)

Block dangerous commands:

```bash
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Invoke-*"
```

**Configuration:**

```json
{
  "PowerShellConfiguration": {
    "ExcludePatterns": [
      "Remove-*",
      "*-Credential",
      "Format-*",
      "ConvertTo-SecureString"
    ]
  }
}
```

## Module Management

### Import Modules

Load modules at startup:

```bash
poshmcp update-config --add-import-module Az.Accounts
poshmcp update-config --add-import-module Microsoft.PowerShell.Management
```

### Install Modules

Install from PowerShell Gallery:

```bash
poshmcp update-config --add-install-module Az.Accounts --minimum-version 2.0.0
poshmcp update-config --add-install-module Az.Resources --repository PSGallery
```

### Module Paths

Load modules from custom directories:

```bash
poshmcp update-config --add-module-path /mnt/shared-modules
poshmcp update-config --add-module-path ./custom-modules
```

## Built-In Utility Tools

PoshMcp provides utility tools for working with command output:

- **`get-last-command-output`** — retrieve cached output from the previous command
- **`sort-last-command-output`** — sort results by property
- **`filter-last-command-output`** — filter using PowerShell expressions
- **`group-last-command-output`** — group results by property

**Example workflow:**

```
1. AI calls: Get-Service
   Result: ~50 services returned

2. AI calls: sort-last-command-output -Property Status
   Result: Sorted by Status

3. AI calls: filter-last-command-output -FilterExpression "$_.Status -eq 'Running'"
   Result: Only running services
```
