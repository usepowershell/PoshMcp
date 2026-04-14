---
uid: authentication
title: Entra ID Authentication Guide
---

# Entra ID Authentication Guide

Secure your PoshMcp server with Azure Entra ID OAuth 2.1 authentication and token-based access control.

This guide covers two complementary authentication paths. Most production deployments use both to achieve enterprise security with minimal credential management.

## Quick Comparison

| Aspect | App Registration | Managed Identity | Best For |
|--------|------------------|------------------|----------|
| **Defines OAuth scopes?** | ✓ Yes (required) | ✗ No | OAuth API definition |
| **Provides server credentials?** | ✓ Yes (client secret) | ✓ Yes (automatic) | Server calling Azure APIs |
| **Setup complexity** | Medium (portal steps) | Low (enable on resource) | Ease of deployment |
| **Secret rotation burden?** | High (manual) | None (automatic) | Reduced ops overhead |
| **Works on-premises?** | ✓ Yes | ✗ No (requires Azure compute) | Non-Azure deployments |
| **Works in Azure Container Apps/AKS?** | ✓ Yes | ✓ Yes | Azure-native deployments |

## Decision Matrix

- **On-premises or hybrid**: Use **App Registration only**
- **Azure Container Apps, AKS, or App Service**: Use **Managed Identity + App Registration** (recommended)
- **Simple testing**: Use **App Registration only**
- **Enterprise with compliance**: Use **both** for maximum flexibility

## Path A: App Registration

Use App Registration to define OAuth scopes and control client access. Works anywhere.

### Setup in Azure Portal

1. Go to **Azure Portal** → **Azure Active Directory** → **App registrations** → **New registration**
2. Name: `PoshMcp Server`
3. Click **Register**

**Save these values:**
- Application (client) ID
- Directory (tenant) ID

### Expose API Scopes

1. Go to **Expose an API**
2. Set **Application ID URI** to `api://poshmcp-prod` (or custom value)
3. Click **Add a scope**
4. Scope name: `access_as_server`
5. Fill in consent prompts and save

### Configure PoshMcp

Edit `appsettings.json`:

```json
{
  "Authentication": {
    "Enabled": true,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": ["api://poshmcp-prod/access_as_server"]
    },
    "Schemes": {
      "Bearer": {
        "Type": "JwtBearer",
        "Authority": "https://login.microsoftonline.com/{tenant-id}",
        "Audience": "api://poshmcp-prod",
        "RequireHttpsMetadata": true,
        "ValidIssuers": ["https://login.microsoftonline.com/{tenant-id}/v2.0"]
      }
    },
    "ProtectedResource": {
      "Resource": "api://poshmcp-prod",
      "ResourceName": "PoshMcp Server",
      "AuthorizationServers": ["https://login.microsoftonline.com/{tenant-id}"],
      "ScopesSupported": ["api://poshmcp-prod/access_as_server"],
      "BearerMethodsSupported": ["header"]
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Process", "Get-Service"]
  }
}
```

Replace `{tenant-id}` with your Directory (tenant) ID from the app registration.

## Path B: Managed Identity (Azure-Hosted Deployments)

Use managed identity when PoshMcp runs on Azure compute (Container Apps, AKS, App Service). Eliminates credential management on the server.

### Enable Managed Identity

**Azure Container Apps:**

```bash
az containerapp create \
  --name poshmcp \
  --resource-group MyResourceGroup \
  --image myregistry.azurecr.io/poshmcp:latest \
  --system-assigned
```

**Azure Kubernetes Service (AKS):**

Use workload identity for pods:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: poshmcp
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: poshmcp
spec:
  template:
    spec:
      serviceAccountName: poshmcp
      containers:
      - name: poshmcp
        image: myregistry.azurecr.io/poshmcp:latest
```

### Grant Permissions

Get the managed identity's principal ID and grant access to services:

```bash
principalId=$(az containerapp identity show --name poshmcp --resource-group MyResourceGroup --query principalId -o tsv)

# Grant Key Vault access
az keyvault set-policy \
  --name mykeyvault \
  --object-id $principalId \
  --secret-permissions get list

# Or grant storage access
az role assignment create \
  --assignee $principalId \
  --role "Storage Blob Data Reader" \
  --scope /subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/mystorageacct
```

### Configuration

Use the same `appsettings.json` format as App Registration. The difference is credentials come from the Azure platform automatically.

## Testing Authentication

### Check Protected Resource Metadata

```bash
curl https://poshmcp.example.com/.well-known/oauth-protected-resource
```

Expected response:

```json
{
  "resource": "api://poshmcp-prod",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://login.microsoftonline.com/{tenant-id}"],
  "scopes_supported": ["api://poshmcp-prod/access_as_server"],
  "bearer_methods_supported": ["header"]
}
```

### Acquire a Token

```bash
curl -X POST \
  https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token \
  -d "client_id={client-id}" \
  -d "client_secret={client-secret}" \
  -d "scope=api://poshmcp-prod/.default" \
  -d "grant_type=client_credentials"
```

### Make Authenticated Request

```bash
curl -H "Authorization: Bearer {token}" \
  https://poshmcp.example.com/tools
```

## Security Best Practices

### 1. Use HTTPS

Always use HTTPS for production. Entra ID requires it.

### 2. Design Scopes for Least Privilege

Create granular scopes instead of "admin" or "full access":

```json
{
  "scopes_supported": [
    "api://poshmcp-prod/read.diagnostics",
    "api://poshmcp-prod/execute.storage",
    "api://poshmcp-prod/execute.compute"
  ]
}
```

### 3. Combine with Command Allowlists

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Process",
      "Get-Service"
    ],
    "ExcludePatterns": [
      "Remove-*",
      "*-Credential",
      "Invoke-Expression"
    ]
  }
}
```

### 4. Store Secrets in Key Vault

Never embed secrets in `appsettings.json`:

```bash
az keyvault secret set --vault-name MyKeyVault --name poshmcp-client-secret --value "..."
```

Reference in Container Apps:

```bash
az containerapp secret set --name poshmcp --secrets client-secret=keyvaultref:MyKeyVault/poshmcp-client-secret
```

### 5. Enable Audit Logging

PoshMcp logs user identity and command execution:

```bash
export POSHMCP_LOG_LEVEL=Information
```

Logs include:
- User principal name (from token)
- Command executed
- Correlation ID (trace requests across systems)

## Troubleshooting

### 401 Unauthorized on every request

**Check:**
1. Is authentication enabled in `appsettings.json`?
2. Are you sending `Authorization: Bearer {token}` header?
3. Is the token expired? Check the `exp` claim.

### Invalid token signature

**Check:**
1. Did you acquire the token from the correct Entra ID tenant?
2. Is the server using HTTPS? (Entra ID requires it for production)
3. Do the token's `iss`, `aud`, and `scp` claims match your configuration?

Decode the token at [jwt.io](https://jwt.io) to inspect claims.

### Insufficient permissions / scope mismatch

**Error:** `The claim 'scp' does not contain any of the required values`

**Fix:**
1. Verify the token includes the required scope (decode and check `scp` claim)
2. In Entra ID, verify the scope is defined in "Expose an API"
3. Verify the client requested the correct scope when acquiring the token
4. Check `DefaultPolicy.RequiredScopes` in `appsettings.json`

### Managed Identity: IMDS endpoint not found

**Symptoms:** Can't access Azure services from PoshMcp despite managed identity being enabled.

**Fix:**
1. Verify managed identity is enabled on your compute resource
2. Check for network policies or proxies blocking access to `169.254.169.254`
3. Allow IMDS (Instance Metadata Service) access:
   ```bash
   # Inside container, test IMDS
   curl -H "Metadata:true" http://169.254.169.254/metadata/identity/oauth2/token?api-version=2017-12-01&resource=https://management.azure.com
   ```

### Managed Identity: Access denied to Azure service

**Fix:** Grant the managed identity the appropriate role:

```bash
principalId=$(az containerapp identity show --name poshmcp --resource-group MyResourceGroup --query principalId -o tsv)

az role assignment create \
  --assignee $principalId \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/mykeyvault
```

---

**Next:** [Docker Deployment](docker.md) | [Advanced Configuration](advanced.md)

For the complete authentication guide with step-by-step portal screenshots and advanced scenarios, see the Entra ID Authentication Guide in the repository docs folder.
