# Azure Container Apps Deployment - Quick Reference

## Prerequisites Checklist

- [ ] Azure CLI installed (`az --version`)
- [ ] Docker installed and running (`docker ps`)
- [ ] Logged into Azure (`az login`)
- [ ] Correct subscription selected (`az account show`)
- [ ] Azure Container Registry name chosen

## Quick Deploy

### Bash (Linux/macOS/WSL)

```bash
export REGISTRY_NAME="myregistry"
export RESOURCE_GROUP="poshmcp-rg"
export LOCATION="eastus"

cd infrastructure/azure
chmod +x validate.sh deploy.sh
./validate.sh
./deploy.sh
```

### PowerShell (Windows/cross-platform)

```powershell
cd infrastructure/azure
.\validate.ps1
.\deploy.ps1 -RegistryName "myregistry" -ResourceGroup "poshmcp-rg" -Location "eastus"
```

## Common Scenarios

### Development Environment

Parameters for dev/testing (scale-to-zero, lower resources):

```json
{
  "minReplicas": { "value": 0 },
  "maxReplicas": { "value": 3 },
  "cpuCores": { "value": "0.25" },
  "memoryGi": { "value": "0.5" }
}
```

### Production Environment

Parameters for production (always-on, higher resources):

```json
{
  "minReplicas": { "value": 2 },
  "maxReplicas": { "value": 20 },
  "cpuCores": { "value": "1.0" },
  "memoryGi": { "value": "2.0" }
}
```

### High-Traffic Environment

Parameters for heavy workloads:

```json
{
  "minReplicas": { "value": 3 },
  "maxReplicas": { "value": 30 },
  "cpuCores": { "value": "2.0" },
  "memoryGi": { "value": "4.0" }
}
```

## Post-Deployment Verification

```bash
# Get app URL
APP_URL=$(az containerapp show --name poshmcp --resource-group poshmcp-rg --query "properties.configuration.ingress.fqdn" -o tsv)

# Test health endpoint
curl https://$APP_URL/health | jq
curl https://$APP_URL/health/ready

# View logs
az containerapp logs show --name poshmcp --resource-group poshmcp-rg --follow

# Check metrics
az monitor metrics list --resource $(az containerapp show --name poshmcp --resource-group poshmcp-rg --query id -o tsv) --metric Requests
```

## Update Deployment

### Deploy new image version

```bash
# Build and tag new version
docker build -t $REGISTRY_NAME.azurecr.io/poshmcp:v2.0 .
az acr login --name $REGISTRY_NAME
docker push $REGISTRY_NAME.azurecr.io/poshmcp:v2.0

# Update container app
az containerapp update \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --image $REGISTRY_NAME.azurecr.io/poshmcp:v2.0
```

### Rollback to previous version

```bash
# List revisions
az containerapp revision list --name poshmcp --resource-group poshmcp-rg -o table

# Activate previous revision
az containerapp revision activate \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --revision <revision-name>
```

## Scaling Operations

### Manual scaling

```bash
# Scale to specific replica count
az containerapp update \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --min-replicas 2 \
  --max-replicas 15
```

### Disable autoscaling

```bash
# Set min = max for fixed replica count
az containerapp update \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --min-replicas 3 \
  --max-replicas 3
```

## Monitoring Queries

### Log Analytics (Kusto)

```kusto
// Recent errors
ContainerAppConsoleLogs_CL
| where ContainerAppName_s == "poshmcp"
| where Log_s contains "error" or Log_s contains "exception"
| where TimeGenerated > ago(1h)
| order by TimeGenerated desc

// Health check status
ContainerAppSystemLogs_CL
| where ContainerAppName_s == "poshmcp"
| where Log_s contains "health"
| where TimeGenerated > ago(15m)
| order by TimeGenerated desc

// Request latency (P95)
ContainerAppConsoleLogs_CL
| where ContainerAppName_s == "poshmcp"
| where Log_s contains "request"
| summarize percentile(todouble(duration_ms), 95) by bin(TimeGenerated, 5m)
```

## Troubleshooting Commands

```bash
# Check replica status
az containerapp replica list --name poshmcp --resource-group poshmcp-rg -o table

# View specific revision
az containerapp revision show --name poshmcp --resource-group poshmcp-rg --revision <revision-name>

# Check environment status
az containerapp env show --name poshmcp-env --resource-group poshmcp-rg

# View secrets (names only, not values)
az containerapp secret list --name poshmcp --resource-group poshmcp-rg

# Test from within environment (exec into container)
az containerapp exec --name poshmcp --resource-group poshmcp-rg
```

## Cost Management

```bash
# View current month costs
az consumption usage list \
  --start-date $(date -u -d "1 month ago" '+%Y-%m-%d') \
  --end-date $(date -u '+%Y-%m-%d') \
  | jq '.[] | select(.instanceName | contains("poshmcp"))'

# Set budget alert (via Azure Portal)
# Budgets > Create > Set threshold for container apps resource group
```

## Environment Variables

Update PowerShell functions at runtime (if EnableDynamicReloadTools is true):

```bash
# Via MCP tools (requires MCP client)
# Call: poshmcp_reload_configuration_from_file

# Or redeploy with new environment variables
az containerapp update \
  --name poshmcp \
  --resource-group poshmcp-rg \
  --set-env-vars "PowerShellConfiguration__FunctionNames__0=Get-Process" "PowerShellConfiguration__FunctionNames__1=Get-Service"
```

## Cleanup

```bash
# Delete container app only
az containerapp delete --name poshmcp --resource-group poshmcp-rg --yes

# Delete entire resource group (all resources)
az group delete --name poshmcp-rg --yes
```

## Region Selection

Recommended regions with Container Apps generally available:
- `eastus` (US East - default, good latency for East Coast)
- `westus2` (US West - good latency for West Coast)
- `centralus` (US Central - geographic center)
- `westeurope` (Europe - Netherlands)
- `northeurope` (Europe - Ireland)
- `australiaeast` (Asia Pacific - Sydney)
- `japaneast` (Asia Pacific - Tokyo)
- `uksouth` (UK - London)

Check availability:
```bash
az provider show --namespace Microsoft.App --query "resourceTypes[?resourceType=='managedEnvironments'].locations"
```

## CI/CD Variables

For automated deployments, set these as pipeline secrets/variables:

```yaml
# Required
AZURE_CREDENTIALS       # Service principal JSON
REGISTRY_NAME          # ACR name (without .azurecr.io)
RESOURCE_GROUP         # Resource group name

# Optional (with defaults)
CONTAINER_APP_NAME     # Default: poshmcp
LOCATION              # Default: eastus
IMAGE_TAG             # Default: timestamp or git SHA
```

## Health Check URLs

After deployment, monitor these endpoints:

- **Detailed health:** `https://[app-url]/health`
- **Readiness probe:** `https://[app-url]/health/ready`
- **MCP tools list:** `https://[app-url]/mcp/v1/tools` (if HTTP transport is configured)

## Support Resources

- **Azure Docs:** https://learn.microsoft.com/azure/container-apps/
- **Bicep Docs:** https://learn.microsoft.com/azure/azure-resource-manager/bicep/
- **PoshMcp README:** ../../README.md
- **Full Deployment Guide:** README.md
- **Azure Support:** https://azure.microsoft.com/support/

## Quick Tips

💡 **First deployment:** Run `validate.sh` or `validate.ps1` first to catch issues early  
💡 **Cost optimization:** Use scale-to-zero (minReplicas: 0) for development  
💡 **Performance:** Allocate 1GB+ memory for PowerShell workloads  
💡 **Monitoring:** Enable Application Insights for correlation ID tracking  
💡 **Security:** Use managed identity instead of registry username/password  
💡 **Debugging:** Use `az containerapp logs show --follow` for live log streaming
