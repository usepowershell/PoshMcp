---
uid: azure-integration
title: Azure Integration
---

# Azure Integration

Deploy PoshMcp to Azure Container Apps with Managed Identity, monitoring, and scaling.

For the complete Azure deployment guide, see infrastructure/azure/README.md in the repository.

## Quick Deploy

```bash
cd infrastructure/azure
./deploy.sh
```

## What This Sets Up

- **Azure Container Apps** — managed container hosting
- **Managed Identity** — automatic credentials for Azure services
- **Log Analytics** — centralized logging and monitoring
- **Application Insights** — performance monitoring
- **Auto-scaling** — scales based on CPU and memory usage

## Manual Deployment

Create a container app with managed identity:

```bash
az containerapp create \
  --name poshmcp \
  --resource-group MyResourceGroup \
  --image myregistry.azurecr.io/poshmcp:latest \
  --system-assigned \
  --ingress external \
  --target-port 8080 \
  --environment MyContainerEnv
```

Grant permissions:

```bash
principalId=$(az containerapp identity show \
  --name poshmcp \
  --resource-group MyResourceGroup \
  --query principalId -o tsv)

az role assignment create \
  --assignee $principalId \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/mykeyvault
```

## Bicep Deployment

Use Bicep for infrastructure-as-code:

```bicep
param location string = resourceGroup().location

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'poshmcp'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
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
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
}
```

---

**See also:** infrastructure/azure/README.md in the repository for full guide
