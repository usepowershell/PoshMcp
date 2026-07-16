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
poshmcp update-config --add-command Get-Process
poshmcp update-config --add-module Az.Accounts
```

For developers building from source:

```bash
dotnet run --project PoshMcp.Server -- create-config
dotnet run --project PoshMcp.Server -- update-config --add-command Get-Process
```

## Configuration Options

### Exposing Commands

**Specific command names (whitelist):**

```bash
poshmcp update-config --add-command Get-Service
poshmcp update-config --add-command Restart-Service
poshmcp update-config --add-command Get-Process
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

**Add modules to discovery list:**

```bash
poshmcp update-config --add-module Az.Accounts
poshmcp update-config --add-module Az.Resources
```

Install/import behavior and module search paths are configured in `appsettings.json` under `PowerShellConfiguration.Environment`.

### Noun-Derived Resources

Enable noun-derived resources when you want PoshMcp to expose parameterless `Get-*` nouns through `resources/list` and `resources/read`, and to append MCP resource links to successful tool results for the same noun.

Edit `appsettings.json`:

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-NounResourceFixture",
      "Assert-NounResourceFixture"
    ],
    "EnableNounResources": true,
    "NounResourceOverrides": {
      "noun_resource_fixture": {
        "ResourceName": "fixture_override",
        "Uri": "poshmcp://resources/fixture_override",
        "DisableResourceLinkBlock": false
      }
    }
  }
}
```

Behavior:

- `EnableNounResources` defaults to `false`.
- A noun is resourceable only when the discovered command set contains a matching `Get-{Noun}` command and that command has at least one parameter set with no required user parameters.
- The default resource name is the noun converted to snake_case, and the default URI is `poshmcp://resources/{resource_name}`.
- `NounResourceOverrides` is keyed by the default derived resource name.
- `Disabled: true` suppresses the derived resource and skips tool-result resource links for that noun.
- `DisableResourceLinkBlock: true` keeps the resource available in `resources/list` and `resources/read`, but skips the appended `application/json+mcp-resource-link` block on tool results.
- `PowerShellConfiguration.CommandOverrides.<Command>.AssociatedResourceUri` (or legacy `FunctionOverrides.<Command>.AssociatedResourceUri`) overrides the implicit noun-derived link target for that command when the URI resolves to an exposed MCP resource.
- `CommandOverrides` still take precedence over legacy `FunctionOverrides` when both define `AssociatedResourceUri` for the same command.
- Explicit `AssociatedResourceUri` links are appended with `relationship: "context"` and are not suppressed by `DisableResourceLinkBlock`; that flag only suppresses the implicit noun-derived fallback.
- If `AssociatedResourceUri` is absent or does not resolve for a discovered command override during tool setup, PoshMcp falls back to the existing implicit noun-derived link behavior when available. In that case, PoshMcp logs a warning and continues; it does not perform generic configuration-load validation for `AssociatedResourceUri` values.

Example:

```json
{
  "PowerShellConfiguration": {
    "CommandOverrides": {
      "Invoke-Deployment": {
        "AssociatedResourceUri": "poshmcp://resources/deployment-guide"
      }
    }
  }
}
```

Use `poshmcp doctor` to verify the effective noun-resource surface. When the feature is enabled, doctor reports registered resources, conflicts, and suppressed nouns in the `Noun Resources` section.

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
    "EnableDynamicReloadTools": false,
    "RuntimeMode": "InProcess",
    "SubprocessHostMode": "Pool",
    "SubprocessRunspacePoolSize": 0
  },
  "McpServer": {
    "IdleSessionTimeoutSeconds": 60,
    "SessionRunspaceCapacity": 16,
    "SessionRunspaceIdleTtlSeconds": 300,
    "SessionRunspaceSweepIntervalSeconds": 30,
    "SessionRunspaceWarmStandbyCount": 2,
    "SessionRunspaceAcquisitionTimeoutSeconds": 15,
    "EnableLegacySse": false
  }
}
```

### Runtime Mode

`PowerShellConfiguration.RuntimeMode` selects whether commands execute in-process (default) or in a separate `pwsh` subprocess:

- `InProcess` (default) — embedded PowerShell SDK; lowest overhead.
- `OutOfProcess` — commands run in a managed `pwsh` subprocess for module isolation. Recommended when loading heavy modules such as `Az.*` or `Microsoft.Graph.*`.

When `RuntimeMode` is `OutOfProcess`, `SubprocessHostMode` selects the subprocess topology (`Pool` is the default since 2026-05-06):

| `SubprocessHostMode` | When to use |
|----------------------|-------------|
| `Pool` (default) | Best concurrent throughput; recommended for typical MCP workloads. |
| `ProcessPool` | Trust-boundary or tail-latency-sensitive workloads. |
| `Single` | Backward-compatible serialized mode; useful for bisecting regressions. |

See [Advanced Configuration → Out-of-Process PowerShell](advanced.md#out-of-process-powershell) for sizing options (`SubprocessRunspacePoolSize`, `SubprocessPoolSize`, `SubprocessMinHealthyForStartup`), the cancellation contract, and the benchmark study driving the default.

### MCP Server HTTP Sessions

`McpServer` applies to HTTP transport. Each request with an
`Mcp-Session-Id` is assigned one clean, initialized PowerShell runspace for
the lifetime of that MCP session; it is not reassigned to another session.
Requests without that header use a one-shot runspace and do not retain
PowerShell state.

| Setting | Default | Effect |
|---------|--------:|--------|
| `IdleSessionTimeoutSeconds` | 60 | Closes an inactive MCP session. |
| `SessionRunspaceCapacity` | 16 | Maximum runspaces concurrently assigned to sessions or leased for one-shot requests. |
| `SessionRunspaceIdleTtlSeconds` | 300 | Retains an inactive session runspace for this many seconds before release. |
| `SessionRunspaceSweepIntervalSeconds` | 30 | Interval for checking idle session runspaces. |
| `SessionRunspaceWarmStandbyCount` | 2 | Clean initialized runspaces kept ready for new sessions. |
| `SessionRunspaceAcquisitionTimeoutSeconds` | 15 | Maximum time to wait for runspace capacity before the request fails. |
| `EnableLegacySse` | `false` | Enables deprecated HTTP-with-SSE endpoints for legacy clients. |

Warm standbys are initialized separately and do not count against
`SessionRunspaceCapacity`; the manager can therefore hold more initialized
runspaces than the capacity setting. Account for both settings when sizing
memory for module and startup-script initialization. Capacity limits only
runspaces assigned to sessions or leased for one-shot work. When that limit is
exhausted, a new session or one-shot request waits only for
`SessionRunspaceAcquisitionTimeoutSeconds` before failing; it is not assigned
another session's state.

The server releases a session runspace when the SDK session completes, the
client explicitly sends `DELETE`, or the runspace reaches its idle TTL. A
release requested during an active invocation completes after that invocation
unwinds. Dynamic tool reload also releases all managed HTTP session runspaces,
so clients must initialize a new session and should not rely on session state
surviving a reload.

For Streamable HTTP negotiation, endpoint paths, and origin validation, see
[Transport Modes](transport-modes.md#http-mode).

## Environment Variables

Override configuration via environment variables:

```bash
# Set transport mode
export POSHMCP_TRANSPORT=http

# Set log level (info, debug, trace, warning, error)
export POSHMCP_LOG_LEVEL=debug

# Use custom config file
export POSHMCP_CONFIGURATION=/config/appsettings.json

# Override an appsettings key with the standard .NET environment-variable form
export McpServer__IdleSessionTimeoutSeconds=120

# Container module pre-install
export POSHMCP_MODULES="Az.Accounts Az.Resources Az.Storage"
```

You can also provide full appsettings-style configuration purely through environment variables (no physical `appsettings.json` required):

```bash
# Expose Get-Process and Get-Service without mounting a config file
export PowerShellConfiguration__CommandNames__0=Get-Process
export PowerShellConfiguration__CommandNames__1=Get-Service
export PowerShellConfiguration__EnableDynamicReloadTools=true
```

PoshMcp resolves this as an environment-only configuration source.

## Configuration Source In Doctor Output

`poshmcp doctor` reports both the configuration path and configuration mode under Runtime Settings.

- `configurationPath` indicates the resolved file path, or `(environment-only configuration)` when no file is used.
- `configurationMode` indicates either `file-backed` or `environment-only`.
- Both entries include a `source` value (for example `cwd`, `user`, `env`, `cli`).

Example text output snippet:

```text
── Runtime Settings ──────────────────────────
  configuration: (environment-only configuration) (env)
  config-mode  : environment-only                (env)
```

Example JSON output snippet (`poshmcp doctor --format json`):

```json
{
  "runtimeSettings": {
    "configurationPath": {
      "value": "(environment-only configuration)",
      "source": "env"
    },
    "configurationMode": {
      "value": "environment-only",
      "source": "env"
    }
  }
}
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
    "CommandOverrides": {
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
- `PowerShellConfiguration.CommandOverrides.<ToolName>.RequiredRoles` and `RequiredScopes` take precedence over `Authentication.DefaultPolicy` for that tool.
- `CommandOverrides` matching checks exact tool names first (for example `get_process_name`), then normalized command-name candidates. Use command names (for example `Get-Process`) for stable configuration across generated parameter-set tool names.

For full Entra ID and API key setup guidance, see [Authentication Guide](authentication.md).

---

**Next:** [Transport Modes](transport-modes.md)
