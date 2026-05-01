# PoshMcp Entra ID Authentication Guide

**Secure your MCP server with Entra ID single sign-on and token-based access control.**

This guide walks you through configuring PoshMcp to authenticate and authorize incoming requests using Azure Entra ID (formerly Azure Active Directory), implementing OAuth 2.1 with automatic client discovery via RFC 8414. Whether you're deploying on-premises, in a hybrid environment, or in Azure, we cover two complementary authentication paths.

---

## Table of Contents

1. [Why Entra ID?](#why-entra-id)
2. [Choosing Your Authentication Path](#choosing-your-authentication-path)
3. [Path A: App Registration (General Purpose)](#path-a-app-registration-general-purpose)
4. [Path B: Managed Identity (Azure-Hosted Deployments)](#path-b-managed-identity-azure-hosted-deployments)
5. [Best Practice: Combining Both](#best-practice-combining-both)
6. [Token Validation & Security](#token-validation--security)
7. [Troubleshooting](#troubleshooting)

---

## Why Entra ID?

Use Entra ID authentication when:

- **Multi-user deployments** — HTTP mode with per-user isolation and audit trails
- **Enterprise environments** — integrate with existing Azure AD/Microsoft 365 tenants
- **Shared infrastructure** — prevent unauthorized access to sensitive PowerShell commands
- **Compliance requirements** — token-based auth with signing key validation
- **Automatic client discovery** — clients automatically fetch auth metadata without manual config

PoshMcp implements OAuth 2.1 (RFC 8414) so MCP clients can discover auth requirements automatically:

1. Client requests a resource → server responds `401 Unauthorized` + metadata URL
2. Client fetches OAuth metadata from the server
3. Client redirects user to Entra ID to authenticate
4. User logs in and grants consent
5. Client gets a token and makes authenticated requests
6. PoshMcp validates the token (signature, issuer, audience) and allows access

---

## Choosing Your Authentication Path

Two complementary approaches are available. Many production deployments use **both**.

### Quick Comparison

| Aspect | App Registration | Managed Identity | Best For |
|--------|------------------|------------------|----------|
| **Defines OAuth scopes?** | ✓ Yes (required) | ✗ No | OAuth API definition |
| **Provides server credentials?** | ✓ Yes (client secret) | ✓ Yes (automatic) | Server calling Azure APIs |
| **Setup complexity** | Medium (portal steps) | Low (enable on resource) | Ease of deployment |
| **Secret rotation burden?** | High (manual) | None (automatic) | Reduced ops overhead |
| **Works on-premises?** | ✓ Yes | ✗ No (requires Azure compute) | Non-Azure deployments |
| **Works in Azure Container Apps/AKS?** | ✓ Yes | ✓ Yes | Azure-native deployments |

### Decision Matrix

- **On-premises or hybrid (non-Azure VMs, containers)**: Use **App Registration only**
- **Azure Container Apps, AKS, or App Service**: Use **Managed Identity for server credentials** + **App Registration for OAuth scopes** (recommended)
- **Simple testing or single-client access**: Use **App Registration only**
- **Enterprise with audit/compliance requirements**: Use **both** for maximum flexibility and auditability

---

## Path A: App Registration (General Purpose)

Use this path to define OAuth scopes and issue access tokens for clients. Works anywhere (on-premises, hybrid, cloud).

### Prerequisites

**Before starting:**

- **Azure subscription** with Entra ID tenant (most Microsoft enterprise customers have this)
- **Global Administrator or Application Administrator role** in your Entra ID tenant (to create app registrations)
- **PoshMcp 1.1+** (includes JWT Bearer authentication support)
- **HTTP mode deployment** (Entra ID auth works best with HTTP; stdio mode is single-connection, so less useful for multi-user scenarios)
- **HTTPS URL for your PoshMcp server** (Entra ID requires token validation over HTTPS; HTTP is allowed only for local testing with `RequireHttpsMetadata: false`)

**Optional but recommended:**

- **Azure CLI** or **Microsoft Graph PowerShell** for app registration scripting
- **curl** or **Postman** for testing token endpoints
- **VS Code REST Client** for manual token acquisition

### Setup in Azure Portal

#### Step 1: Create the App Registration

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Active Directory** → **App registrations**
2. Click **New registration**
3. Fill in:
   - **Name:** `PoshMcp Server` (or your app name)
   - **Supported account types:** `Accounts in this organizational directory only` (single tenant) or `Accounts in any organizational directory` (multi-tenant)
   - **Redirect URI:** Leave blank for now; not needed for server-to-server auth
4. Click **Register**

You now have:
- **Application (client) ID** — copy this, you'll need it later
- **Directory (tenant) ID** — copy this, you'll need it later
- **Application ID URI** — auto-generated as `api://{client-id}`, customize if desired

#### Step 2: Expose API Scopes

Scopes define what permissions clients can request. Create at least one scope that clients will use:

**Before you add a scope, set the Application ID URI:**

1. In the app registration, go to **Expose an API**
2. Next to "Application ID URI," click **Set** (if not already set)
3. Accept the default `api://{client-id}` or enter a custom URI like `api://poshmcp-prod`
4. Click **Save**

**Now add the scope:**

5. Click **Add a scope**
6. Fill in all required fields:
   - **Scope name** (required): `access_as_server` — becomes part of the full URI: `api://poshmcp-prod/access_as_server`
   - **Who can consent?** (required): 
     - Select **"Admins only"** for server-to-server (M2M) scenarios where PoshMcp acts on its own behalf
     - Select **"Admins and users"** for delegated access where end-users grant consent
   - **Admin consent display name** (required): `Access PoshMcp Server` — shown on admin consent approval screens
   - **Admin consent description** (required): `Allows authenticated users to execute PowerShell commands via PoshMcp` — explain what the scope permits
   - **User consent display name** (optional): `Access PoshMcp Server`
   - **User consent description** (optional): `Allows you to execute PowerShell commands via PoshMcp`
   - **State**: Ensure **Enabled** is selected
7. Click **Add scope**

**Result**: The full scope URI is `api://poshmcp-prod/access_as_server` (used in `RequiredScopes` and `ScopesSupported`). When a token is issued, the scope appears in the `scp` claim as just `access_as_server`.

#### Step 2b: Authorize Client Applications (Required for VS Code and MCP Clients)

To allow VS Code and other MCP clients to authenticate with your PoshMcp server, you must authorize them in the **Authorized client applications** section:

1. In the app registration, go to **Expose an API**
2. Scroll down to **Authorized client applications** and click **Add a client application**
3. For VS Code MCP support, add the pre-registered VS Code client ID:
   - **Client ID**: `aebc6443-996d-45c2-90f0-388ff96faa56` (VS Code's fixed MCP client ID)
   - **Scopes**: Check the scopes you created (e.g., `access_as_server`)
4. Click **Add application**

**Why this step is critical:** VS Code uses a pre-registered client ID (`aebc6443-996d-45c2-90f0-388ff96faa56`) and does not support dynamic client registration (RFC 7591). Without this authorization, VS Code will fail with: *"Dynamic client registration not supported"*.

If you have other MCP clients, add their client IDs here as well.

#### Step 3: (Conditional) Grant Admin Consent for M2M

If you selected "Admins only" for the scope, you must grant admin consent:

1. Go to the *client app registration* (the one that calls PoshMcp, e.g., VS Code)
2. Go to **API permissions**
3. Click **Add a permission**
4. Select **My APIs** → choose your PoshMcp Server app
5. Select **Application permissions**
6. Check the `access_as_server` scope
7. Click **Add permissions**
8. Click **Grant admin consent for [tenant name]** (only admins can do this)

---

#### Step 4: Create Credentials for M2M (Server-to-Server)

#### Step 3: Create Credentials for M2M (Server-to-Server)

If PoshMcp needs to act on behalf of itself (not just validate user tokens), create a client secret:

1. Go to **Certificates & secrets**
2. Click **New client secret**
3. Set **Expires:** `24 months` or per your policy
4. Click **Add**
5. **Copy the secret value immediately** — you won't see it again
6. Store it securely in your server's environment or key vault (e.g., Azure Key Vault)

#### Step 4: API Permissions (Optional)

If PoshMcp needs to call Azure APIs (e.g., via PowerShell `Connect-AzAccount`), grant API permissions:

1. Go to **API permissions**
2. Click **Add a permission**
3. Select **Microsoft APIs** → **Microsoft Graph** (or the Azure service you need)
4. Choose **Delegated permissions** or **Application permissions** as needed
5. Search for and select permissions (e.g., `User.Read`, `AzureServiceManagement/user_impersonation`)
6. Click **Add permissions**
7. If you added **Application permissions**, click **Grant admin consent** (only admins can do this)

### Configuration

#### Edit `appsettings.json`

Authentication configuration goes in the `Authentication` section of your PoshMcp config file. Here's a complete example:

```json
{
  "Authentication": {
    "Enabled": true,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": ["api://poshmcp-prod/access_as_server"],
      "RequiredRoles": []
    },
    "Schemes": {
      "Bearer": {
        "Type": "JwtBearer",
        "Authority": "https://login.microsoftonline.com/{tenant-id}",
        "Audience": "api://poshmcp-prod",
        "RequireHttpsMetadata": true,
        "ValidIssuers": ["https://login.microsoftonline.com/{tenant-id}/v2.0"],
        "ClaimsMapping": {
          "ScopeClaim": "scp",
          "RoleClaim": "roles"
        }
      }
    },
    "ProtectedResource": {
      "Resource": "api://poshmcp-prod",
      "ResourceName": "PoshMcp Server",
      "AuthorizationServers": ["https://login.microsoftonline.com/{tenant-id}"],
      "ScopesSupported": ["api://poshmcp-prod/access_as_server"],
      "BearerMethodsSupported": ["header"]
    },
    "Cors": {
      "AllowedOrigins": ["http://localhost:3000"],
      "AllowCredentials": false
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Process", "Get-Service"]
  }
}
```

#### Replace Placeholders

Update these values with your Azure AD app info:

| Placeholder | Value | Example |
|-------------|-------|---------|
| `{tenant-id}` | Directory (tenant) ID from app registration | `12345678-1234-1234-1234-123456789012` |
| `Authority` | Entra ID token endpoint | `https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012` |
| `Audience` | App ID URI from "Expose an API" | `api://poshmcp-prod` |
| `ValidIssuers[0]` | Entra ID issuer URL | `https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0` |
| `Resource` | Same as Audience | `api://poshmcp-prod` |
| `AuthorizationServers[0]` | Same as Authority | `https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012` |

#### Production Configuration

For production deployments on Azure Container Apps or AKS:

**Use environment variables to store sensitive values:**

```bash
# Store in Azure Key Vault or Container Apps secrets
AUTHENTICATION__SCHEMES__BEARER__AUTHORITY=https://login.microsoftonline.com/{tenant-id}
AUTHENTICATION__SCHEMES__BEARER__AUDIENCE=api://poshmcp-prod
AUTHENTICATION__SCHEMES__BEARER__VALIDISSUERS__0=https://login.microsoftonline.com/{tenant-id}/v2.0
```

Then update `appsettings.json` to reference them:

```json
{
  "Authentication": {
    "Schemes": {
      "Bearer": {
        "Authority": "${AUTHENTICATION__SCHEMES__BEARER__AUTHORITY}",
        "Audience": "${AUTHENTICATION__SCHEMES__BEARER__AUDIENCE}"
      }
    }
  }
}
```

### Testing

Have these values ready:

- **Tenant ID**: `12345678-1234-1234-1234-123456789012`
- **Client ID**: `87654321-4321-4321-4321-210987654321`
- **Client Secret** (if M2M testing): `your-secret-value`
- **Scope**: `api://poshmcp-prod/access_as_server`
- **PoshMcp URL**: `https://poshmcp.example.com` or `https://localhost:8080`

#### Test 1: Check Protected Resource Metadata

Verify your server publishes auth metadata correctly:

```bash
curl https://poshmcp.example.com/.well-known/oauth-protected-resource
```

Expected response:

```json
{
  "resource": "api://poshmcp-prod",
  "resource_name": "PoshMcp Server",
  "authorization_servers": ["https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012"],
  "scopes_supported": ["api://poshmcp-prod/access_as_server"],
  "bearer_methods_supported": ["header"]
}
```

#### Test 2: Acquire a Token (M2M)

If you created a client secret, test server-to-server authentication:

```bash
curl -X POST \
  https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/oauth2/v2.0/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "client_id=87654321-4321-4321-4321-210987654321" \
  -d "client_secret=your-secret-value" \
  -d "scope=api://poshmcp-prod/.default" \
  -d "grant_type=client_credentials"
```

Response:

```json
{
  "token_type": "Bearer",
  "expires_in": 3600,
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6IjEyMzQ1Njc4OTBhYmNkZWYifQ.eyJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vMTIzNDU2Nzg4OTAxMi92Mi4wIiwiYXVkIjoiYXBpOi8vcG9zaC1tY3AtcHJvZCIsInN1YiI6IjEyMzQ1Njc4OTAiLCJpYXQiOjE3MTAwMDAwMDAsImV4cCI6MTcxMDAwMzYwMH0.example_signature"
}
```

#### Test 3: Make Authenticated Request

Use the token to call PoshMcp:

```bash
curl -H "Authorization: Bearer eyJhbGc..." \
  https://poshmcp.example.com/tools
```

Expected response: list of available tools (if token is valid).

Without the token or with an invalid token:

```bash
curl https://poshmcp.example.com/tools
```

Response:

```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.example.com/.well-known/oauth-protected-resource"
```

#### Test 4: Check Server Health

Verify authentication is configured correctly:

```bash
curl https://poshmcp.example.com/health
```

If authentication config is invalid, you'll see errors in the response or in server logs. Valid output indicates auth is running.

---

### VS Code MCP Integration

**Overview:** VS Code supports MCP servers that authenticate with Entra ID via OAuth 2.1 with RFC 9728 Protected Resource Metadata.

#### Why This Matters

VS Code uses a **pre-registered client ID** (`aebc6443-996d-45c2-90f0-388ff96faa56`) for MCP server connections. Microsoft Entra ID does **not** support RFC 7591 (Dynamic Client Registration), so this pre-registered ID must be explicitly authorized in your app registration. Without this step, VS Code will fail with: *"Dynamic client registration not supported"*.

#### How VS Code OAuth Flow Works

1. VS Code connects to your PoshMcp server
2. Server responds with `HTTP 401` + `WWW-Authenticate` header pointing to `/.well-known/oauth-protected-resource`
3. VS Code fetches the protected resource metadata, which includes:
   - Authorization server (Entra ID)
   - Required scopes
   - Bearer method
4. VS Code fetches OAuth metadata from Entra ID (`/.well-known/oauth-authorization-server`)
5. VS Code initiates **Authorization Code flow with PKCE** using its pre-registered client ID
6. User authenticates in browser and grants consent
7. VS Code receives a token and includes it in requests to PoshMcp: `Authorization: Bearer {token}`
8. PoshMcp validates the token signature, issuer, audience, and scopes

#### VS Code Configuration

In your VS Code `settings.json` or `.vscode/mcp.json`:

```json
{
  "servers": {
    "poshmcp": {
      "type": "http",
      "url": "https://your-mcp-server.example.com/mcp",
      "headers": {}
    }
  }
}
```

VS Code automatically handles the OAuth flow. No manual token configuration is needed.

#### Protected Resource Metadata Endpoint

Your MCP server must expose `/.well-known/oauth-protected-resource` (RFC 9728). PoshMcp does this automatically via the `ProtectedResource` configuration:

```json
{
  "Authentication": {
    "ProtectedResource": {
      "Resource": "api://poshmcp-prod",
      "ResourceName": "PoshMcp Server",
      "AuthorizationServers": ["https://login.microsoftonline.com/{tenant-id}"],
      "ScopesSupported": ["api://poshmcp-prod/access_as_server"],
      "BearerMethodsSupported": ["header"]
    }
  }
}
```

Test the endpoint:

```bash
curl https://poshmcp.example.com/.well-known/oauth-protected-resource
```

Expected response:

```json
{
  "resource": "api://poshmcp-prod",
  "resource_name": "PoshMcp Server",
  "authorization_servers": [
    "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012"
  ],
  "scopes_supported": [
    "api://poshmcp-prod/access_as_server"
  ],
  "bearer_methods_supported": ["header"]
}
```

#### VS Code MCP Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| "Dynamic client registration not supported" | VS Code's pre-registered client ID is not authorized in your app registration | Go to **Expose an API** → **Authorized client applications** and add `aebc6443-996d-45c2-90f0-388ff96faa56` |
| VS Code redirects to `https://{poshmcp-host}/authorize` instead of Entra ID | Server's 401 response is missing the RFC 9728 `resource_metadata` header (PoshMcp v0.9.3 or earlier) | Upgrade to PoshMcp v0.9.4 or later. The server now emits the correct `WWW-Authenticate: Bearer resource_metadata="..."` header required for VS Code to discover the PRM endpoint. |
| VS Code prompts auth against the app's own `/authorize` endpoint | PRM is misconfigured or missing (post-v0.9.4) | Verify `ProtectedResource` is configured and the endpoint returns the expected JSON (accessible at `https://{poshmcp-host}/.well-known/oauth-protected-resource`) |
| 401 after successful VS Code login | Scope mismatch or token validation failure | Verify the token scope matches `ScopesSupported` in PRM; decode the token at jwt.io to inspect claims |
| AADSTS650053 error | App needs admin consent | Grant admin consent in Azure Portal → App registrations → API permissions |

---

## Path B: Managed Identity (Azure-Hosted Deployments)

Use managed identity when PoshMcp runs on Azure-hosted compute (Container Apps, AKS, App Service, Azure VMs) and needs to call other Azure services with its own identity. Managed identity eliminates the need for secret management on the server side, but **does not replace the app registration** for OAuth API definition.

### Prerequisites

**Before starting:**

- **PoshMcp 1.1+** (includes JWT Bearer authentication support)
- **Azure-hosted compute resource** with managed identity support:
  - Azure Container Apps
  - Azure Kubernetes Service (AKS)
  - Azure App Service
  - Azure Virtual Machines
  - Azure Logic Apps
- **HTTP mode deployment** (same as app registration path)
- **HTTPS URL for your PoshMcp server** (same as app registration path)
- **App registration still required** (for OAuth scopes and client discovery)

### Infrastructure Setup

Enable managed identity on your Azure compute resource. Here are examples for common scenarios:

#### Azure Container Apps (Azure CLI)

```bash
# Create container app with managed identity
az containerapp create \
  --name poshmcp \
  --resource-group MyResourceGroup \
  --image myregistry.azurecr.io/poshmcp:latest \
  --system-assigned  # Enable system-assigned managed identity
```

Or, if creating via Bicep:

```bicep
param location string = resourceGroup().location
param containerAppName string = 'poshmcp'

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: managedEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
    }
    template: {
      containers: [
        {
          name: 'poshmcp'
          image: 'myregistry.azurecr.io/poshmcp:latest'
          env: [
            {
              name: 'AUTHENTICATION__ENABLED'
              value: 'true'
            }
            {
              name: 'AUTHENTICATION__SCHEMES__BEARER__AUTHORITY'
              value: 'https://login.microsoftonline.com/${tenant().tenantId}'
            }
            {
              name: 'AUTHENTICATION__SCHEMES__BEARER__AUDIENCE'
              value: 'api://poshmcp-prod'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
}

output principalId string = containerApp.identity.principalId
```

After deployment, note the **principal ID** — you'll use it to grant permissions.

#### Azure Kubernetes Service (AKS) with Workload Identity

AKS pods can use workload identity as an alternative to managed identity:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: poshmcp
  namespace: default
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: poshmcp
spec:
  replicas: 1
  selector:
    matchLabels:
      app: poshmcp
  template:
    metadata:
      labels:
        app: poshmcp
    spec:
      serviceAccountName: poshmcp
      containers:
      - name: poshmcp
        image: myregistry.azurecr.io/poshmcp:latest
        ports:
        - containerPort: 8080
        env:
        - name: AUTHENTICATION__ENABLED
          value: "true"
        - name: AUTHENTICATION__SCHEMES__BEARER__AUTHORITY
          value: "https://login.microsoftonline.com/$(TENANT_ID)"
        - name: AUTHENTICATION__SCHEMES__BEARER__AUDIENCE
          value: "api://poshmcp-prod"
```

For workload identity, link the Kubernetes ServiceAccount to an Entra ID app registration:

```bash
az aks workload-identity association create \
  --resource-group MyResourceGroup \
  --cluster-name MyAksCluster \
  --namespace default \
  --service-account-name poshmcp \
  --identity-resource-id /subscriptions/{subscription-id}/resourceGroups/{rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/poshmcp
```

### What Managed Identity Does NOT Replace

**App registration is still required** for:

- **OAuth scope definition** — clients need to know what scopes are available
- **Client discovery** — the `/.well-known/oauth-protected-resource` endpoint publishes the app registration's scopes
- **Token validation configuration** — PoshMcp still needs to know the `Audience` and `ValidIssuers` from your app registration

Managed identity **only** provides the server's own credentials for calling other Azure services.

### Using Managed Identity for Server-Side Azure Credentials

If PoshMcp needs to call Azure APIs (e.g., Key Vault, Storage, Azure CLI), use managed identity instead of storing secrets:

#### Example: PoshMcp Calling Azure Key Vault

**Without managed identity (old way, not recommended):**

```json
{
  "KeyVault": {
    "ClientId": "87654321-4321-4321-4321-210987654321",
    "ClientSecret": "your-secret-value",
    "TenantId": "12345678-1234-1234-1234-123456789012",
    "VaultUri": "https://mykeyvault.vault.azure.net"
  }
}
```

**With managed identity (recommended):**

```json
{
  "KeyVault": {
    "VaultUri": "https://mykeyvault.vault.azure.net",
    "UseManagedIdentity": true
  }
}
```

In PowerShell code, use the Azure SDK with managed identity:

```powershell
# Azure SDK automatically uses managed identity when running in Azure
$credential = New-Object Azure.Identity.DefaultAzureCredential
$client = New-Object Azure.Security.KeyVault.Secrets.SecretClient -ArgumentList @("https://mykeyvault.vault.azure.net", $credential)
$secret = $client.GetSecret("my-secret")
```

#### Grant Managed Identity Permissions

After enabling managed identity on your compute resource, grant it access to the resources it needs:

```bash
# Get the managed identity's principal ID
principalId="12345678-1234-1234-1234-123456789012"

# Grant access to Key Vault
az keyvault set-policy \
  --name mykeyvault \
  --object-id $principalId \
  --secret-permissions get list

# Or grant access to a storage account
az role assignment create \
  --assignee $principalId \
  --role "Storage Blob Data Reader" \
  --scope /subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/mystorageacct
```

### Configuration

The configuration is identical to app registration; the difference is where credentials come from:

```json
{
  "Authentication": {
    "Enabled": true,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": ["api://poshmcp-prod/access_as_server"],
      "RequiredRoles": []
    },
    "Schemes": {
      "Bearer": {
        "Type": "JwtBearer",
        "Authority": "https://login.microsoftonline.com/{tenant-id}",
        "Audience": "api://poshmcp-prod",
        "RequireHttpsMetadata": true,
        "ValidIssuers": ["https://login.microsoftonline.com/{tenant-id}/v2.0"],
        "ClaimsMapping": {
          "ScopeClaim": "scp",
          "RoleClaim": "roles"
        }
      }
    },
    "ProtectedResource": {
      "Resource": "api://poshmcp-prod",
      "ResourceName": "PoshMcp Server",
      "AuthorizationServers": ["https://login.microsoftonline.com/{tenant-id}"],
      "ScopesSupported": ["api://poshmcp-prod/access_as_server"],
      "BearerMethodsSupported": ["header"]
    },
    "Cors": {
      "AllowedOrigins": ["http://localhost:3000"],
      "AllowCredentials": false
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Process", "Get-Service"]
  }
}
```

**Key difference:** You don't need to store or rotate client secrets. The Azure compute platform provides credentials transparently.

### Testing

#### Test 1: Verify Managed Identity is Active

```bash
# From inside the container or VM, query the Azure Instance Metadata Service (IMDS)
curl http://169.254.169.254/metadata/identity/oauth2/token?api-version=2017-12-01&resource=https://management.azure.com \
  -H "Metadata:true" | jq .
```

You should see a token with claims pointing to your managed identity.

#### Test 2: Verify Key Vault Access

If PoshMcp accesses Key Vault via managed identity:

```powershell
# In a PoshMcp PowerShell script
$credential = New-Object Azure.Identity.DefaultAzureCredential
$client = New-Object Azure.Security.KeyVault.Secrets.SecretClient -ArgumentList @("https://mykeyvault.vault.azure.net", $credential)
$secret = $client.GetSecret("my-secret")
Write-Host "Retrieved secret: $($secret.Value)"
```

If this works, managed identity is properly configured.

#### Test 3: OAuth Flow (Same as App Registration Path)

Client authentication flow is identical. Clients still acquire tokens via app registration and send them to PoshMcp. See [Path A Testing](#testing) for details.

---

## Best Practice: Combining Both

**Recommended for production Azure deployments:** Use managed identity for server credentials + app registration for OAuth API definition.

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ MCP Client (e.g., Claude.dev, external app)                 │
│ - Acquires token via app registration                        │
│ - Sends token in Authorization header                        │
└──────────────────────┬──────────────────────────────────────┘
                       │ HTTP request + Bearer token
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ PoshMcp Server (Azure Container Apps / AKS)                │
│                                                              │
│ ┌──────────────────────────────────────────────────────┐   │
│ │ Token Validation (App Registration)                 │   │
│ │ - Validates client's token signature                │   │
│ │ - Checks audience: api://poshmcp-prod               │   │
│ │ - Checks scopes: api://poshmcp-prod/access_as_server│   │
│ └──────────────────────────────────────────────────────┘   │
│                       │                                      │
│                       ▼ (if token is valid)                 │
│ ┌──────────────────────────────────────────────────────┐   │
│ │ PowerShell Execution                                 │   │
│ │ - Runs command with client's user context           │   │
│ │ - If needs Azure services...                         │   │
│ │   └─> Uses managed identity for service credentials │   │
│ └──────────────────────────────────────────────────────┘   │
│                       │                                      │
│ ┌──────────────────────────────────────────────────────┐   │
│ │ Azure Service Calls (Managed Identity)               │   │
│ │ - Accesses Key Vault, Storage, etc.                 │   │
│ │ - Uses server's managed identity (no secrets!)       │   │
│ └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                       │
                       ▼
            Azure services (Key Vault, Storage, etc.)
```

### Setup Steps

1. **Create app registration** (see [Path A](#path-a-app-registration-general-purpose)) to define OAuth scopes
2. **Enable managed identity** on your Azure compute resource (see [Infrastructure Setup](#infrastructure-setup))
3. **Grant managed identity permissions** to Azure services it needs (see [Grant Managed Identity Permissions](#grant-managed-identity-permissions))
4. **Configure PoshMcp** with app registration details (same `appsettings.json` for both paths)
5. **Update PowerShell code** to use managed identity for Azure service calls (see [Using Managed Identity for Server-Side Azure Credentials](#using-managed-identity-for-server-side-azure-credentials))

### Benefits

- **No secret management for the server** — managed identity handles rotation automatically
- **Audit trail for server actions** — Azure logs show which managed identity accessed which service
- **OAuth still works** — clients still authenticate via app registration; the server's internal calls use managed identity
- **Least privilege** — the server has only the permissions it needs, via managed identity role assignments

---

## Token Validation & Security

### How Token Validation Works

PoshMcp's JWT validation pipeline is scheme-agnostic — it works the same way regardless of whether tokens come from app registration or managed identity. For each request with a token:

1. Extract token from `Authorization: Bearer {token}` header
2. Validate signature using Entra ID's public keys
3. Check issuer matches `ValidIssuers`
4. Check audience matches `Audience`
5. Check expiration
6. Extract scopes from `scp` claim
7. Verify required scopes are present (from `DefaultPolicy.RequiredScopes`)
8. Allow or deny the request

### Client Authentication Flow

#### Automatic Discovery (Recommended)

When authentication is enabled, MCP clients discover auth requirements automatically. Here's the flow:

##### 1. Client requests a resource

```http
GET /tools HTTP/1.1
Host: poshmcp.example.com
```

##### 2. Server responds with metadata URL

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer resource_metadata="https://poshmcp.example.com/.well-known/oauth-protected-resource"
```

##### 3. Client fetches protected resource metadata

```http
GET /.well-known/oauth-protected-resource HTTP/1.1
Host: poshmcp.example.com
```

Server responds:

```json
{
  "resource": "api://poshmcp-prod",
  "resource_name": "PoshMcp Server",
  "authorization_servers": [
    "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012"
  ],
  "scopes_supported": [
    "api://poshmcp-prod/access_as_server"
  ],
  "bearer_methods_supported": ["header"]
}
```

##### 4. Client fetches OAuth authorization server metadata

Client fetches RFC 8414 metadata from the authorization server:

```http
GET /.well-known/oauth-authorization-server HTTP/1.1
Host: login.microsoftonline.com
```

Entra ID responds:

```json
{
  "token_endpoint": "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/oauth2/v2.0/token",
  "authorization_endpoint": "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/oauth2/v2.0/authorize",
  "code_challenge_methods_supported": ["S256"],
  "scopes_supported": ["openid", "profile", "email"]
}
```

##### 5. Client runs OAuth 2.1 PKCE flow

- **Authorization Code flow**: Client redirects user to Entra ID login
- **PKCE (Proof Key for Code Exchange)**: Client generates code verifier and challenge for security
- **Token acquisition**: User authenticates and grants consent; client receives access token
- **Authenticated requests**: Client includes token in `Authorization: Bearer {token}` header

##### 6. PoshMcp validates the token

Validation happens automatically (see "How Token Validation Works" above).

### Security Best Practices

#### 1. Use HTTPS in Production

Always use HTTPS for the PoshMcp server URL in production. Token validation requires HTTPS, and Entra ID will reject HTTP requests.

```bash
# Production: Use HTTPS
poshmcp serve --transport http --port 443 --config ./appsettings.prod.json

# Or use a reverse proxy (nginx, API Gateway) to handle SSL/TLS
```

#### 2. Scope Design: Least Privilege

Define scopes that map to PowerShell tool groups, not "admin" or "full access":

```json
{
  "Authentication": {
    "DefaultPolicy": {
      "RequiredScopes": [
        "api://poshmcp-prod/read.diagnostics",
        "api://poshmcp-prod/execute.storage"
      ]
    }
  }
}
```

In Entra ID, create granular scopes:

| Scope | Tools Allowed | Example |
|-------|---------------|---------|
| `read.diagnostics` | `Get-Process`, `Get-Service`, `Get-Disk` | View-only monitoring |
| `execute.storage` | `New-StorageAccount`, `Set-StorageBlob` | Storage operations |
| `execute.compute` | `Start-VM`, `Stop-VM`, `Restart-VM` | VM management |

#### 3. Token Validation

PoshMcp validates tokens by:

- ✓ Checking signature (verifies token wasn't tampered with)
- ✓ Checking issuer (ensures token came from your Entra ID)
- ✓ Checking audience (ensures token is for your app)
- ✓ Checking expiration (token must be fresh)
- ✓ Checking scopes (user has required permissions)

The server fetches Entra ID's public keys automatically, so signatures are always validated correctly.

#### 4. Deny Dangerous Commands

Combine Entra ID auth with command allowlists to prevent misuse:

```json
{
  "Authentication": {
    "DefaultPolicy": {
      "RequiredScopes": ["api://poshmcp-prod/execute.diagnostics"]
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Process",
      "Get-Service",
      "Get-EventLog"
    ],
    "ExcludePatterns": [
      "Remove-*",
      "*-Credential",
      "Invoke-Expression"
    ]
  }
}
```

#### 5. Audit and Monitoring

Use PoshMcp's built-in correlation IDs to audit who ran what:

```bash
# Enable structured logging
export POSHMCP_LOG_LEVEL=Information

# Log includes:
# - Request headers (Authorization token hash, User-Agent)
# - User claims (from token)
# - Command executed
# - Correlation ID (trace requests across your system)
```

Example log output:

```
info: PoshMcp.Server[1001]
      User 'john@contoso.com' (oid=12345) executed Get-Process via API
      Correlation ID: 550e8400-e29b-41d4-a716-446655440000
      Scopes: api://poshmcp-prod/read.diagnostics
```

#### 6. Credentials in Environment

Never embed secrets in `appsettings.json`. Use Azure Key Vault or Container Apps secrets:

```bash
# Store in Key Vault
az keyvault secret set --vault-name MyKeyVault --name poshmcp-client-secret --value "..."

# Reference in Container Apps
az containerapp secret set --name poshmcp --secrets client-secret=keyvaultref:MyKeyVault/poshmcp-client-secret

# Use in deployment
AUTHENTICATION__SCHEMES__BEARER__AUTHORITY=...
```

#### 7. Regular Token Rotation

Entra ID automatically rotates its signing keys. PoshMcp fetches new keys transparently, so no action needed. But for client secrets (app registration path only):

- Rotate every 12–24 months (or per your policy)
- Create a new secret before deleting the old one (for zero-downtime rotation)
- Disable old secrets in Entra ID once clients have migrated

Managed identity has no secrets to rotate.

---

## Troubleshooting

### App Registration Issues

#### Issue: `401 Unauthorized` on every request

**Check:**

1. Is `Authentication.Enabled` set to `true` in `appsettings.json`?
2. Is the token in the request? Add the `Authorization: Bearer {token}` header.
3. Is the token expired? Check the token's `exp` claim:
   ```bash
   # Decode token at https://jwt.io (for testing only!)
   ```

#### Issue: Invalid token signature

**Check:**

1. Did you acquire the token from the correct Entra ID tenant? Token issuer must match `ValidIssuers`.
2. Is the server URL HTTPS? (Entra ID won't issue tokens for HTTP except localhost with `RequireHttpsMetadata: false`)
3. Are the token's `iss`, `aud`, and `scp` claims correct? Decode at jwt.io to inspect.

#### Issue: Insufficient permissions / scope mismatch

**Error in logs:**

```
The claim 'scp' does not contain any of the required values (the claim value was 'xxx').
```

**Fix:**

1. Verify the token includes the required scope. Decode the token and check the `scp` claim.
2. In Entra ID, verify the app registration has the scope defined in "Expose an API."
3. Verify the client requested the correct scope when acquiring the token (e.g., `scope=api://poshmcp-prod/access_as_server`).
4. In `appsettings.json`, check `DefaultPolicy.RequiredScopes` matches what you're testing.

#### Issue: `RequireHttpsMetadata` validation failure

**Error:**

```
IDX20108: The address must use HTTPS, not HTTP.
```

**Fix for production:** Use HTTPS and set `RequireHttpsMetadata: true`.

**For local testing only:**

```json
{
  "Authentication": {
    "Schemes": {
      "Bearer": {
        "RequireHttpsMetadata": false
      }
    }
  }
}
```

#### Issue: Server won't start due to auth config errors

**Check logs:**

```bash
poshmcp serve --transport http --port 8080 --log-level debug
```

Look for validation errors like:

```
Authentication.Schemes[Bearer].Authority is required for JwtBearer.
```

**Fix:** Ensure all required fields are populated in `appsettings.json`:
- `Authority` (Entra ID token endpoint)
- `Audience` (App ID URI)
- At least one item in `ValidIssuers`

### Managed Identity Issues

#### Issue: IMDS endpoint not found (`http://169.254.169.254/...`)

**Symptoms:** PoshMcp can't access Key Vault or other Azure services despite having managed identity enabled.

**Check:**

1. Is your compute resource running in Azure (Container Apps, AKS, App Service)?
2. Is managed identity enabled on the resource? Check:
   ```bash
   az containerapp identity show --name poshmcp --resource-group MyResourceGroup
   ```
3. Is there a proxy or network policy blocking access to IMDS (169.254.169.254)?

**Fix:**

- If using a proxy, configure it to allow IMDS access
- If using network policies, add a rule allowing `169.254.169.254:80`
- Verify managed identity is enabled (see [Infrastructure Setup](#infrastructure-setup))

#### Issue: Access denied when calling Azure services

**Symptoms:** PoshMcp can access IMDS but gets 403 Forbidden when calling Key Vault, Storage, etc.

**Check:**

1. Did you grant the managed identity permissions to that service? See [Grant Managed Identity Permissions](#grant-managed-identity-permissions).
2. Is the principal ID correct?
   ```bash
   # Get the managed identity's principal ID
   az containerapp identity show --name poshmcp --resource-group MyResourceGroup --query principalId
   ```
3. Is the role assignment correctly applied?
   ```bash
   az role assignment list --assignee {principal-id}
   ```

**Fix:** Grant the managed identity the appropriate role:

```bash
principalId="12345678-1234-1234-1234-123456789012"
az role assignment create \
  --assignee $principalId \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/mykeyvault
```

#### Issue: Token claims don't include expected identity

**Symptoms:** Managed identity is working, but token `sub` or `oid` claims don't match your managed identity's principal ID.

**Check:**

1. Decode the IMDS token and inspect claims:
   ```bash
   curl http://169.254.169.254/metadata/identity/oauth2/token?api-version=2017-12-01&resource=https://management.azure.com -H "Metadata:true" | jq '.access_token' | cut -d. -f2 | base64 -d | jq .
   ```
2. Does the `sub` or `oid` claim match the managed identity's principal ID?

**Fix:** Ensure managed identity is enabled on the correct resource. If you created a new resource, you may need to redeploy.

#### Issue: Can't use Azure SDK with managed identity in PoshMcp

**Symptoms:** PowerShell code using `New-Object Azure.Identity.DefaultAzureCredential` throws errors.

**Check:**

1. Is the Azure SDK installed in the PoshMcp container?
   ```bash
   pip list | grep azure
   # or
   Get-InstalledModule | grep Azure
   ```
2. Is the Azure SDK version recent (supports managed identity via IMDS)?

**Fix:** Update or install the Azure SDK:

```dockerfile
# In your Dockerfile
RUN pip install --upgrade azure-identity azure-keyvault-secrets
# or for PowerShell
RUN Install-Module -Name Az.Identity -Force
```

---

## Next Steps

1. **Decide on your path** — app registration only, managed identity + app registration, or both (see [Choosing Your Authentication Path](#choosing-your-authentication-path))
2. **Set up app registration** (see [Path A](#path-a-app-registration-general-purpose))
3. **If using Azure compute:** Enable managed identity and grant permissions (see [Path B](#path-b-managed-identity-azure-hosted-deployments))
4. **Configure PoshMcp** with `appsettings.json` (see [Configuration](#configuration))
5. **Test the auth flow** (see [Testing](#testing) in each path)
6. **Deploy to production** with HTTPS and secrets in Key Vault (see [Production Configuration](#production-configuration))
7. **Monitor and audit** using correlation IDs and structured logs (see [Audit and Monitoring](#5-audit-and-monitoring))

For questions or issues, check [Troubleshooting](#troubleshooting) or open an issue on [GitHub](https://github.com/microsoft/poshmcp).
