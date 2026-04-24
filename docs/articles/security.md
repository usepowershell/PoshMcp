---
uid: security
title: Security Best Practices
---

# Security Best Practices

Follow these practices to secure your PoshMcp deployment.

## Isolated Runspaces

Each session gets its own PowerShell runspace. Variables and functions persist within a session but are isolated from other sessions.

- **Stdio mode** (single connection): One runspace per client connection
- **HTTP mode** (multi-user): Separate runspace per user session with automatic cleanup

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

In HTTP mode, each user gets isolated execution:

- Separate runspace per user/session
- Variables don't bleed between users
- Automatic cleanup on session timeout
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
