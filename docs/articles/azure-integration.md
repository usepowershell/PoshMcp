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

## Scaffold Azure Deployment Assets

Use the `poshmcp scaffold` command to generate Azure deployment assets into your project before running deployment scripts.

```bash
poshmcp scaffold --project-path ./my-poshmcp-project
```

By default, files are generated under `infra/azure` in the target project.

### Generated Files

The scaffold workflow creates the Azure deployment folder and writes these key artifacts:

- `infra/azure/deploy.ps1`
- `infra/azure/validate.ps1`
- `infra/azure/main.bicep`
- `infra/azure/resources.bicep`
- `infra/azure/parameters.json`
- `infra/azure/deploy.appsettings.json.template`
- `infra/azure/parameters.local.json.template`

Use `--force` to overwrite existing scaffolded files, and `--format json` when you want machine-readable output for automation.

### Scaffold Then Deploy

Typical end-to-end flow:

```bash
# 1) Scaffold deployment assets into your project
poshmcp scaffold --project-path ./my-poshmcp-project

# 2) Move into generated Azure deployment folder
cd ./my-poshmcp-project/infra/azure

# 3) (Optional) validate
pwsh ./validate.ps1

# 4) Deploy
pwsh ./deploy.ps1 -RegistryName myregistry
```

If your environment uses appsettings-based deployment inputs, create a deployment settings file from the template and pass it to `deploy.ps1`:

```bash
cd ./my-poshmcp-project/infra/azure
cp deploy.appsettings.json.template deploy.appsettings.json
pwsh ./deploy.ps1 -AppSettingsFile ./deploy.appsettings.json -RegistryName myregistry
```

## Server Configuration with `-ServerAppSettingsFile`

The generated `deploy.ps1` script includes support for the `-ServerAppSettingsFile` parameter, which lets you configure the deployed MCP server identically to your local development setup. When you provide an appsettings.json file, `deploy.ps1` automatically translates the MCP server configuration into container environment variables.

### How It Works

Your local PoshMcp server reads configuration from `appsettings.json` (PowerShell functions, modules, logging, runtime mode, etc.). When deploying to Azure Container Apps:

1. **Pass your server config:** Use `-ServerAppSettingsFile` to point `deploy.ps1` at your MCP server's `appsettings.json`
2. **Automatic translation:** `deploy.ps1` extracts `PowerShellConfiguration` and other server settings
3. **Container environment:** Settings are applied as container environment variables on deploy
4. **Identical runtime:** The deployed container runs with the same configuration as your local server

### What Gets Translated

These settings from your server's `appsettings.json` are automatically converted to container environment variables:

- **PowerShellConfiguration.CommandNames** — Exposed PowerShell functions
- **PowerShellConfiguration.Modules** — Preinstalled modules
- **PowerShellConfiguration.RuntimeMode** — Execution mode (sync/async)
- **Logging settings** — Log levels and sinks
- **Authentication settings** — Identity provider configuration
- **Health check configuration** — Diagnostics and monitoring

### Basic Example: Deploy with Server Configuration

Copy your local MCP server's `appsettings.json` to your deployment folder, then pass it to `deploy.ps1`:

```bash
cd ./my-poshmcp-project/infra/azure

# Copy your server config file
cp ../PoshMcp.Server/appsettings.json ./server-config.json

# Deploy with server configuration
pwsh ./deploy.ps1 `
  -ServerAppSettingsFile ./server-config.json `
  -RegistryName myregistry `
  -EnvironmentName prod
```

The container will start with the same functions, modules, and logging configuration as your local server.

### Environment-Specific Configuration

For different environments (dev, staging, prod), maintain separate appsettings files and pass the appropriate one during deployment:

```bash
# Deploy to staging with staging config
pwsh ./deploy.ps1 `
  -ServerAppSettingsFile ./appsettings.staging.json `
  -RegistryName myregistry

# Deploy to production with production config
pwsh ./deploy.ps1 `
  -ServerAppSettingsFile ./appsettings.prod.json `
  -RegistryName myregistry
```

### Integration with Scaffold Workflow

The `-ServerAppSettingsFile` parameter works alongside the scaffolded deployment template. After running `poshmcp scaffold`, you can customize your server configuration and deploy with it in one step:

```bash
# 1) Scaffold deployment assets
poshmcp scaffold --project-path ./my-poshmcp-project

# 2) Customize server config (optional)
# Edit ./my-poshmcp-project/PoshMcp.Server/appsettings.json as needed

# 3) Deploy with your server configuration
cd ./my-poshmcp-project/infra/azure
pwsh ./deploy.ps1 `
  -ServerAppSettingsFile ../PoshMcp.Server/appsettings.json `
  -RegistryName myregistry
```

**See also:** [Configuration](configuration.md) — detailed appsettings.json reference | [Advanced Configuration](advanced.md) — environment-specific deployments

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

**See also:** infrastructure/azure/README.md in the repository for full guide | [Advanced Configuration](advanced.md)
