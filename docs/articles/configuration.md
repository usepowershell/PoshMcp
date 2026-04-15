---
uid: configuration
title: Configuration Guide
---

# Configuration Guide

Configure PoshMcp to expose specific PowerShell commands, modules, and tools for your use case.

## Creating Configuration

Generate default configuration:

```bash
poshmcp create-config
```

This creates `appsettings.json` with defaults. Customize using CLI commands:

```bash
poshmcp update-config --add-function Get-Process
poshmcp update-config --add-module Az.Accounts
poshmcp update-config --add-import-module Az.Accounts
```

For developers building from source:

```bash
dotnet run --project PoshMcp.Server -- create-config
dotnet run --project PoshMcp.Server -- update-config --add-function Get-Process
```

## Configuration Options

### Exposing Commands

**Specific command names (whitelist):**

```bash
poshmcp update-config --add-function Get-Service
poshmcp update-config --add-function Restart-Service
poshmcp update-config --add-function Get-Process
```

**Include patterns (wildcard):**

```bash
poshmcp update-config --add-include-pattern "Get-*"
poshmcp update-config --add-include-pattern "Set-AzVM"
```

**Exclude patterns (blacklist):**

```bash
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Invoke-*"
```

### Module Management

**Install modules:**

```bash
poshmcp update-config --add-install-module Az.Accounts --minimum-version 2.0.0
poshmcp update-config --add-install-module Az.Resources --repository PSGallery
```

**Import modules:**

```bash
poshmcp update-config --add-import-module Az.Accounts
poshmcp update-config --add-import-module Microsoft.PowerShell.Management
```

**Add module paths:**

```bash
poshmcp update-config --add-module-path /mnt/shared-modules
poshmcp update-config --add-module-path ./custom-modules
```

**Module gallery settings:**

```bash
poshmcp update-config --trust-psgallery
poshmcp update-config --skip-publisher-check
poshmcp update-config --install-timeout-seconds 600
```

## appsettings.json Reference

Full configuration structure:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Process",
      "Get-Service"
    ],
    "IncludePatterns": [
      "Get-*"
    ],
    "ExcludePatterns": [
      "Remove-*",
      "*-Credential"
    ],
    "Modules": [
      "Az.Accounts"
    ],
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "MinimumVersion": "2.0.0",
          "Repository": "PSGallery",
          "Scope": "CurrentUser"
        }
      ],
      "ImportModules": [
        "Az.Accounts",
        "Microsoft.PowerShell.Management"
      ],
      "ModulePaths": [
        "/mnt/shared-modules"
      ],
      "TrustPSGallery": true,
      "SkipPublisherCheck": true,
      "InstallTimeoutSeconds": 600,
      "StartupScript": "$Global:Ready = $true",
      "StartupScriptPath": "./startup.ps1"
    },
    "Performance": {
      "EnableResultCaching": false,
      "UseDefaultDisplayProperties": true
    },
    "EnableDynamicReloadTools": false
  }
}
```

## Environment Variables

Override configuration via environment variables:

```bash
# Set transport mode
export POSHMCP_TRANSPORT=http

# Set log level (info, debug, trace, warning, error)
export POSHMCP_LOG_LEVEL=debug

# Use custom config file
export POSHMCP_CONFIGURATION=/config/appsettings.json

# Session timeout (minutes)
export POSHMCP_SESSION_TIMEOUT_MINUTES=120

# Container module pre-install
export POSHMCP_MODULES="Az.Accounts Az.Resources Az.Storage"
```

## Startup Scripts

Run PowerShell code when the server starts:

**Inline startup script:**

Edit `appsettings.json`:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:CompanyName = 'Acme'; Write-Host 'Ready!'"
    }
  }
}
```

**From file:**

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
# Company-specific initialization
$Global:CompanyName = 'Acme'
$Global:Environment = 'Production'

# Azure setup
Connect-AzAccount -Identity -ErrorAction Stop

# Custom functions
function Get-CompanyInfo {
    [PSCustomObject]@{
        Company = $Global:CompanyName
        Environment = $Global:Environment
    }
}

Write-Host "✓ Environment initialized" -ForegroundColor Green
```

## Performance Tuning

**Enable result caching:**

```bash
poshmcp update-config --enable-result-caching true
```

This caches command output for repeated queries (useful for read-only operations).

**Use display properties:**

Set `UseDefaultDisplayProperties: true` to leverage PowerShell's default display formatting (faster for large result sets).

**Pre-install modules in Docker:**

```bash
docker build \
  --build-arg MODULES="Az.Accounts Az.Resources" \
  -t poshmcp:fast .
```

## Security Configuration

### Command Filtering

Restrict dangerous commands:

```bash
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "Disable-*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Format-*"
poshmcp update-config --add-exclude-pattern "ConvertTo-SecureString"
```

### Authentication

PoshMcp supports both authentication modes in the same `Authentication` model:

- **Entra ID (OAuth 2.1 / JwtBearer):** Use for enterprise identity and token-based clients. See [Authentication Guide - Entra ID](authentication.md#entra-id-oauth-21).
- **API key (ApiKey):** Use for trusted internal clients and automation. Example:

```json
{
  "Authentication": {
    "Enabled": true,
    "DefaultScheme": "ApiKey",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": [],
      "RequiredRoles": ["reader"]
    },
    "Schemes": {
      "ApiKey": {
        "Type": "ApiKey",
        "HeaderName": "X-API-Key",
        "Keys": {
          "key-reader": {
            "Scopes": [],
            "Roles": ["reader"]
          },
          "key-ops": {
            "Scopes": [],
            "Roles": ["ops", "reader"]
          }
        }
      }
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Process", "Get-Service"],
    "Modules": [],
    "ExcludePatterns": [],
    "IncludePatterns": [],
    "FunctionOverrides": {
      "Get-Process": {
        "RequiredRoles": ["ops"]
      },
      "Get-Service": {
        "RequiredRoles": ["reader", "support"]
      }
    }
  }
}
```

Behavior:
- `Authentication.DefaultPolicy.RequiredRoles` and `RequiredScopes` apply to all tools by default.
- API key role and scope claims come from the matching `Schemes.ApiKey.Keys` entry.
- `PowerShellConfiguration.FunctionOverrides.<ToolName>.RequiredRoles` and `RequiredScopes` take precedence over `Authentication.DefaultPolicy` for that tool.
- `FunctionOverrides` matching checks exact tool names first (for example `get_process_name`), then normalized command-name candidates. Use command names (for example `Get-Process`) for stable configuration across generated parameter-set tool names.

For full Entra ID and API key setup guidance, see [Authentication Guide](authentication.md).

---

**Next:** [Transport Modes](transport-modes.md)
