# Azure Container Apps deployment for PoshMcp

This directory contains the infrastructure-as-code templates and deployment scripts for running PoshMcp on Azure Container Apps.

## Overview

PoshMcp is deployed to Azure Container Apps in web/HTTP mode, exposing PowerShell functions as MCP (Model Context Protocol) tools via HTTP endpoints. This deployment includes:

- **Azure Container Apps**: Serverless container hosting with autoscaling
- **Log Analytics Workspace**: Centralized logging and monitoring
- **Application Insights**: APM and distributed tracing
- **Managed Identity**: Secure access to Azure resources
- **Health Checks**: Kubernetes-style liveness and readiness probes

## Prerequisites

Before deploying, ensure you have:

1. **Azure CLI** (v2.50.0+): [Installation Guide](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
2. **Docker**: [Installation Guide](https://docs.docker.com/get-docker/)
3. **Azure Subscription**: With appropriate permissions to create resources
4. **Azure Container Registry**: Or permissions to create one

### Authentication

Login to Azure CLI:

```bash
az login
```

Set your subscription (if you have multiple):

```bash
az account set --subscription "Your Subscription Name"
```

## Quick Start

### Using Bash

```bash
# Set required environment variables
export REGISTRY_NAME="myregistry"  # Your Azure Container Registry name (without .azurecr.io)
export RESOURCE_GROUP="poshmcp-rg"
export LOCATION="eastus"

# Optional: Specify Azure tenant (for multi-tenant scenarios)
export AZURE_TENANT_ID="00000000-0000-0000-0000-000000000000"

# Run deployment
cd infrastructure/azure
chmod +x deploy.sh
./deploy.sh
```

### Using PowerShell

```powershell
# Set required parameters and run deployment
cd infrastructure/azure
./deploy.ps1 -RegistryName "myregistry" -ResourceGroup "poshmcp-rg" -Location "eastus"

# Optional: Specify Azure tenant (for multi-tenant scenarios)
./deploy.ps1 -RegistryName "myregistry" -TenantId "00000000-0000-0000-0000-000000000000"
```

## Multi-tenant support

The deployment scripts support working with multiple Azure tenants, which is useful when:
- You have subscriptions in different Azure Active Directory tenants
- You need to deploy to a specific tenant in a multi-tenant organization
- You work across customer environments

### Specifying a tenant

Use the `-TenantId` parameter (PowerShell) or `AZURE_TENANT_ID` environment variable (Bash):

**PowerShell:**
```powershell
./deploy.ps1 -RegistryName "myregistry" -TenantId "11111111-2222-3333-4444-555555555555"
```

**Bash:**
```bash
export AZURE_TENANT_ID="11111111-2222-3333-4444-555555555555"
./deploy.sh
```

### Tenant Validation

The deployment script automatically:
1. **Validates tenant access**: Checks if you're logged into the specified tenant
2. **Switches tenants**: Performs `az login --tenant` if needed
3. **Validates subscription**: Ensures the target subscription belongs to the correct tenant
4. **Shows tenant info**: Displays current tenant ID in deployment output

### Multi-tenant scenarios

#### Scenario 1: Explicit tenant selection

When you know the tenant ID:
```powershell
./deploy.ps1 -RegistryName "myregistry" `
             -TenantId "contoso-tenant-id" `
             -Subscription "contoso-subscription"
```

#### Scenario 2: Using current tenant

If you don't specify a tenant, the deployment uses your currently logged-in tenant:
```powershell
az login
./deploy.ps1 -RegistryName "myregistry"
```

#### Scenario 3: CI/CD with managed identity

In Azure DevOps or GitHub Actions, the tenant is typically implicit through the service connection:
```bash
# Service principal automatically authenticated to correct tenant
./deploy.sh
```

#### Scenario 4: Cross-tenant deployment

If you need to switch between tenants:
```powershell
# Deploy to customer A
./deploy.ps1 -RegistryName "customerA-registry" -TenantId "customer-a-tenant-id"

# Deploy to customer B
./deploy.ps1 -RegistryName "customerB-registry" -TenantId "customer-b-tenant-id"
```

### Error handling

The scripts provide clear error messages for tenant-related issues:

- **Tenant access denied**: "Failed to login to tenant {id}. Verify you have access to this tenant."
- **Tenant mismatch**: "Subscription belongs to tenant {X}, but currently logged into tenant {Y}."
- **Invalid tenant**: Azure CLI will return a detailed error if the tenant ID is invalid or inaccessible

## Configuration

### Parameters File

Edit `parameters.json` to customize your deployment:

```json
{
  "containerAppName": "poshmcp",           // Container App name
  "environmentName": "poshmcp-env",        // Container Apps Environment
  "location": "eastus",                    // Azure region
  "minReplicas": 1,                        // Minimum instances
  "maxReplicas": 10,                       // Maximum instances (autoscaling)
  "cpuCores": "0.5",                       // CPU cores per instance
  "memoryGi": "1.0",                       // Memory in GB per instance
  "powerShellFunctions": "Get-SomeData",   // Comma-separated function names
  "enableDynamicReloadTools": true         // Enable runtime configuration reload
}
```

### Resource Sizing

PoshMcp runs PowerShell workloads which can be memory-intensive:

| Workload Type | CPU Cores | Memory | Min Replicas | Max Replicas |
|---------------|-----------|--------|--------------|--------------|
| Development   | 0.25      | 0.5Gi  | 0-1          | 3            |
| Production    | 0.5       | 1.0Gi  | 1            | 10           |
| Heavy Load    | 1.0       | 2.0Gi  | 2            | 20           |

### Environment Variables

The deployment automatically configures these environment variables:

- `ASPNETCORE_ENVIRONMENT`: Production
- `ASPNETCORE_URLS`: http://+:8080
- `POSHMCP_MODE`: web
- `PowerShellConfiguration__FunctionNames__0`: Your configured functions
- `PowerShellConfiguration__EnableDynamicReloadTools`: true/false
- `APPLICATIONINSIGHTS_CONNECTION_STRING`: Auto-configured
- `AZURE_CLIENT_ID`: Managed Identity client ID

## Deployment architecture

```
┌─────────────────────────────────────────────────────┐
│                Azure Container Apps                  │
│  ┌────────────────────────────────────────────┐    │
│  │           PoshMcp Container App             │    │
│  │  - Ingress: HTTPS (port 8080)              │    │
│  │  - Health: /health, /health/ready          │    │
│  │  - Autoscaling: 1-10 replicas              │    │
│  │  - Identity: User-assigned managed          │    │
│  └──────────┬─────────────────────────────────┘    │
│             │                                        │
│             ├─> Application Insights (APM)          │
│             └─> Log Analytics (Logs & Metrics)      │
└─────────────────────────────────────────────────────┘
         │
         └──> Azure Container Registry (poshmcp:tag)
```

## Health checks

PoshMcp exposes health check endpoints used by Azure Container Apps:

### Endpoints

- **`/health`**: Detailed health check with JSON response
  - PowerShell runspace health
  - Assembly generation capability
  - Configuration validation
  - Execution duration metrics

- **`/health/ready`**: Simple readiness probe (HTTP 200 OK / 503 Unhealthy)
  - Used for Kubernetes-style health probes
  - Fast response time (<500ms typical)

### Probe configuration

```bicep
probes: [
  {
    type: 'Liveness'
    httpGet: { path: '/health/ready', port: 8080 }
    initialDelaySeconds: 10
    periodSeconds: 30
    failureThreshold: 3
  },
  {
    type: 'Readiness'
    httpGet: { path: '/health/ready', port: 8080 }
    initialDelaySeconds: 5
    periodSeconds: 10
    failureThreshold: 3
  },
  {
    type: 'Startup'
    httpGet: { path: '/health', port: 8080 }
    initialDelaySeconds: 0
    periodSeconds: 5
    failureThreshold: 30  // Allow up to 150s for startup
  }
]
```

## Monitoring and observability

### Application Insights

Integrated APM provides:
- Request tracking with correlation IDs
- OpenTelemetry metrics
- Distributed tracing
- Custom telemetry from PoshMcp

Access in Azure Portal:
```
Resource Group > poshmcp-insights > Application Insights
```

### Log Analytics

Container logs are automatically forwarded to Log Analytics workspace:

```kusto
// Query container logs
ContainerAppConsoleLogs_CL
| where ContainerAppName_s == "poshmcp"
| where TimeGenerated > ago(1h)
| order by TimeGenerated desc

// Query health check metrics
ContainerAppSystemLogs_CL
| where ContainerAppName_s == "poshmcp"
| where Log_s contains "health"
| order by TimeGenerated desc
```

### Metrics

View metrics in Azure Portal:
```
Container App > Monitoring > Metrics
```

Key metrics to monitor:
- **Requests**: HTTP request rate and latency
- **CPU Usage**: Container CPU consumption
- **Memory**: Memory usage per replica
- **Replica Count**: Current number of instances
- **Health Check Success Rate**: Probe success/failure

## Security

### Managed Identity

The deployment creates a user-assigned managed identity that:
- Authenticates to Azure services without credentials
- Can be granted RBAC roles on Azure resources
- Automatically rotates credentials

Grant permissions to the identity:

```bash
# Get the identity's principal ID
PRINCIPAL_ID=$(az containerapp show \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --query "identity.userAssignedIdentities.*.principalId" \
  -o tsv)

# Grant Key Vault access (example)
az keyvault set-policy \
  --name your-keyvault \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list
```

### Network security

- **Ingress**: HTTPS only (automatic TLS termination)
- **Internal Communication**: Secured within Container Apps Environment
- **No Public IPs**: Fully managed platform security

### Secrets management

Container registry credentials and Application Insights connection strings are stored as Container Apps secrets (encrypted at rest).

For additional secrets, use Azure Key Vault with managed identity:

```csharp
// Example: Access Key Vault from PoshMcp
var client = new SecretClient(
    new Uri("https://your-keyvault.vault.azure.net/"),
    new DefaultAzureCredential());
```

## Scaling

### Autoscaling configuration

The deployment configures HTTP-based autoscaling:

```bicep
scale: {
  minReplicas: 1
  maxReplicas: 10
  rules: [
    {
      name: 'http-scaling'
      http: {
        metadata: {
          concurrentRequests: '50'  // Scale when >50 requests per replica
        }
      }
    }
  ]
}
```

### Scale to zero

For non-production environments, you can enable scale-to-zero:

```bash
az containerapp update \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --min-replicas 0
```

**Note**: First cold start may take 10-30 seconds.

## Troubleshooting

### View Container Logs

```bash
# Stream live logs
az containerapp logs show \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --follow

# View recent logs
az containerapp logs show \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --tail 100
```

### Check Revision Status

```bash
az containerapp revision list \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --output table
```

### Verify Health Checks

```bash
# Get the app URL
APP_URL=$(az containerapp show \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --query "properties.configuration.ingress.fqdn" \
  -o tsv)

# Test health endpoint
curl https://$APP_URL/health | jq
curl https://$APP_URL/health/ready
```

### Common issues

#### Container fails to start

**Symptoms**: Replicas stuck in "Provisioning"

**Causes**:
- Image pull failure (check registry credentials)
- Application crash on startup
- Health check configuration mismatch

**Resolution**:
```bash
# Check container logs
az containerapp logs show --name poshmcp --resource-group poshmcp-rg --tail 50

# Verify image exists in registry
az acr repository show --name myregistry --image poshmcp:latest
```

#### Health checks failing

**Symptoms**: Container restarts frequently

**Causes**:
- Application not listening on port 8080
- Health endpoints returning errors
- Probe timeouts too short

**Resolution**:
```bash
# Test health endpoint locally
kubectl run test --image=curlimages/curl -it --rm -- \
  curl http://poshmcp.internal:8080/health

# Increase probe timeout in main.bicep
timeoutSeconds: 10  # Increase from 3 to 10
```

#### Out of Memory

**Symptoms**: Container killed with exit code 137

**Resolution**: Increase memory allocation in parameters.json:
```json
{
  "memoryGi": { "value": "2.0" }  // Increase from 1.0 to 2.0
}
```

## Cost optimization

### Pricing factors

Container Apps billing is based on:
- **vCPU seconds**: CPU cores × running time
- **Memory GB-seconds**: Memory × running time
- **Requests**: Number of HTTP requests (free tier available)

### Optimization tips

1. **Right-size resources**: Start with 0.5 vCPU / 1GB and monitor
2. **Enable scale-to-zero**: For non-prod environments
3. **Use consumption plan**: Pay only for actual usage
4. **Set appropriate min replicas**: Balance availability vs cost

Example monthly costs (approximate):
- Dev (0.25 vCPU, 0.5GB, scale to zero): $5-15/month
- Prod (0.5 vCPU, 1GB, 1 min replica): $30-50/month
- High availability (1 vCPU, 2GB, 2 min replicas): $120-180/month

## Updates and rollbacks

### Deploy new version

```bash
# Build and push new image
docker build -t myregistry.azurecr.io/poshmcp:v2.0 .
docker push myregistry.azurecr.io/poshmcp:v2.0

# Update container app
az containerapp update \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --image myregistry.azurecr.io/poshmcp:v2.0
```

### Rollback to previous revision

```bash
# List revisions
az containerapp revision list \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --output table

# Activate previous revision
az containerapp revision activate \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --revision poshmcp--<previous-revision-name>
```

## CI/CD integration

### GitHub Actions example

```yaml
name: Deploy to Azure Container Apps

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Login to Azure
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Build and deploy
        run: |
          export REGISTRY_NAME=${{ secrets.ACR_NAME }}
          export RESOURCE_GROUP=poshmcp-rg
          ./infrastructure/azure/deploy.sh
```

### Azure DevOps example

```yaml
trigger:
  branches:
    include:
      - main

pool:
  vmImage: 'ubuntu-latest'

variables:
  registryName: 'myregistry'
  resourceGroup: 'poshmcp-rg'

steps:
  - task: AzureCLI@2
    inputs:
      azureSubscription: 'Azure-Subscription'
      scriptType: 'bash'
      scriptLocation: 'scriptPath'
      scriptPath: 'infrastructure/azure/deploy.sh'
    env:
      REGISTRY_NAME: $(registryName)
      RESOURCE_GROUP: $(resourceGroup)
```

## See also

- [INDEX.md](INDEX.md) — File navigation guide for this directory
- [QUICKSTART.md](QUICKSTART.md) — Quick-reference commands for common operations
- [ARCHITECTURE.md](ARCHITECTURE.md) — Infrastructure design and component details
- [CHECKLIST.md](CHECKLIST.md) — Step-by-step deployment verification checklist
- [MODULARIZATION.md](MODULARIZATION.md) — Bicep module architecture and scope handling
- [PoshMcp main README](../../README.md) — Project overview
- [Azure Container Apps documentation](https://learn.microsoft.com/en-us/azure/container-apps/)
- [Bicep documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [Application Insights for ASP.NET Core](https://learn.microsoft.com/en-us/azure/azure-monitor/app/asp-net-core)

For Azure-specific issues, consult [Azure Support](https://azure.microsoft.com/support/).
