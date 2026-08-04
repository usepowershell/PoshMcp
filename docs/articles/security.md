---
uid: security
title: Security Best Practices
---

# Security Best Practices

Follow these practices to secure your PoshMcp deployment.

## Isolated Runspaces

HTTP requests are served from a **reset-before-reuse runspace pool**: no
cross-request variable or function persistence, and each lease starts clean.
Persistent per-connection state exists only in **stdio** mode (single
process-scoped runspace).

- **Stdio mode** (single connection): One runspace per client connection
- **HTTP mode** (multi-user): pooled runspaces with reset-before-reuse; per-call cleanup via the reset protocol. For per-tenant process isolation use OutOfProcess `ProcessPool`.

## Command Filtering

Restrict dangerous commands via exclude patterns:

```bash
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "Disable-*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Format-*"
```

**Configuration:**

```json
{
  "PowerShellConfiguration": {
    "ExcludePatterns": [
      "Remove-*",
      "Disable-*",
      "*-Credential",
      "Format-*",
      "ConvertTo-SecureString"
    ]
  }
}
```

## Azure Managed Identity

When deployed to Azure (Container Apps, AKS, etc.), PoshMcp automatically uses Azure Managed Identity for secure resource access—no credentials needed.

```powershell
# Automatically uses the managed identity
Connect-AzAccount -Identity
Get-AzResource
```

## Authentication (Optional)

For HTTP deployments, choose one authentication mode:

- **Entra ID (OAuth 2.1):** Best for enterprise identity, external clients, and centralized access governance. See [Entra ID setup](authentication.md#entra-id-oauth-21).
- **API key:** Best for trusted internal callers and simple service automation. See [API key setup](authentication.md#api-key-authentication).

Example API key configuration:

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
    "CommandOverrides": {
      "Get-Process": {
        "RequiredRoles": ["ops"]
      }
    }
  }
}
```

Per-tool `CommandOverrides` authorization requirements override `Authentication.DefaultPolicy` for that tool.
`CommandOverrides` matching checks exact tool names first (for example `get_process_name`), then normalized command-name candidates (for example `Get-Process`). Use command-name keys for durable configuration across generated parameter-set tool names.

Clients must provide the API key:

```bash
curl -H "X-API-Key: your-secret-key" http://localhost:8080/tools
```

## Identity Separation (HTTP Mode)

In HTTP mode, each call gets isolated execution via the reset-before-reuse
pool:

- Reset-before-reuse pooled runspace per **call** (not per user/session); optional per-tenant **process** isolation via `SubprocessHostMode=ProcessPool`
- Variables don't bleed between calls (each worker starts clean)
- Automatic worker **reset on return** to the pool and idle reclamation past `RunspacePool:IdleTtl`; MCP `IdleSessionTimeoutSeconds` cleanup applies only in Stateful mode
- Audit trail via correlation IDs

## Deployment Security

### Use HTTPS in Production

```bash
poshmcp serve --transport http --port 443
```

Or use a reverse proxy (nginx, API Gateway) for SSL/TLS termination.

### Store Secrets in Key Vault

Never embed secrets in `appsettings.json`. Use Azure Key Vault:

```bash
az keyvault secret set --vault-name MyKeyVault --name poshmcp-secret --value "..."
```

### Enable Audit Logging

```bash
export POSHMCP_LOG_LEVEL=Information
```

Logs include:
- User principal name
- Command executed
- Correlation ID for tracing

---

**Next:** [Authentication Guide](authentication.md) | [Docker Deployment](docker.md)
