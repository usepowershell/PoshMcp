# Example deployment commands for PoshMcp Azure Container Apps
# Copy these commands and customize for your environment

# ===================================
# BASH DEPLOYMENT EXAMPLE
# ===================================

# Set your environment
export REGISTRY_NAME="mycontainerreg"           # Your ACR name (without .azurecr.io)
export RESOURCE_GROUP="poshmcp-production"      # Resource group name
export LOCATION="eastus"                        # Azure region
export AZURE_SUBSCRIPTION="My Azure Subscription"     # Subscription name (optional)

# Change to infrastructure directory
cd infrastructure/azure

# Run validation
chmod +x validate.sh deploy.sh
./validate.sh

# Deploy to Azure
./deploy.sh

# ===================================
# POWERSHELL DEPLOYMENT EXAMPLE
# ===================================

# Set your environment
$env:REGISTRY_NAME = "mycontainerreg"
$env:RESOURCE_GROUP = "poshmcp-production"
$env:LOCATION = "eastus"
$env:AZURE_SUBSCRIPTION = "My Azure Subscription"

# Change to infrastructure directory
Set-Location infrastructure/azure

# Run validation
.\validate.ps1

# Deploy to Azure
.\deploy.ps1 -RegistryName $env:REGISTRY_NAME -ResourceGroup $env:RESOURCE_GROUP -Location $env:LOCATION

# ===================================
# CUSTOM PARAMETERS DEPLOYMENT
# ===================================

# Create a local parameters file from template
cp parameters.local.json.template parameters.local.json

# Edit parameters.local.json with your values:
# - containerImage: your-registry.azurecr.io/poshmcp:latest
# - containerRegistryServer: your-registry.azurecr.io
# - CPU, memory, replica counts as needed

# Deploy with local parameters (Bash)
export REGISTRY_NAME="myregistry"
export RESOURCE_GROUP="poshmcp-dev"
./deploy.sh
# The script will merge parameters.local.json if it exists

# ===================================
# POST-DEPLOYMENT VERIFICATION
# ===================================

# Get the application URL
az containerapp show \
  --name poshmcp \
  --resource-group poshmcp-production \
  --query "properties.configuration.ingress.fqdn" \
  -o tsv

# Test health endpoints
curl https://poshmcp.RANDOM-ID.eastus.azurecontainerapps.io/health | jq
curl https://poshmcp.RANDOM-ID.eastus.azurecontainerapps.io/health/ready

# Stream logs
az containerapp logs show \
  --name poshmcp \
  --resource-group poshmcp-production \
  --follow

# ===================================
# DEVELOPMENT-SPECIFIC SETTINGS
# ===================================

# For development with scale-to-zero:
# Edit parameters.json or parameters.local.json:
{
  "minReplicas": { "value": 0 },      # Scale to zero when idle
  "maxReplicas": { "value": 3 },      # Lower ceiling for cost
  "cpuCores": { "value": "0.25" },    # Minimal CPU
  "memoryGi": { "value": "0.5" }      # Minimal memory
}

# ===================================
# PRODUCTION-SPECIFIC SETTINGS
# ===================================

# For production with high availability:
{
  "minReplicas": { "value": 2 },      # Always 2+ instances
  "maxReplicas": { "value": 20 },     # Higher ceiling for traffic spikes
  "cpuCores": { "value": "1.0" },     # More CPU for performance
  "memoryGi": { "value": "2.0" }      # More memory for PowerShell
}

# ===================================
# UPDATE EXISTING DEPLOYMENT
# ===================================

# Update with new image version
REGISTRY_NAME="myregistry"
IMAGE_TAG="v2.0.0"

docker build -t ${REGISTRY_NAME}.azurecr.io/poshmcp:${IMAGE_TAG} .
az acr login --name $REGISTRY_NAME
docker push ${REGISTRY_NAME}.azurecr.io/poshmcp:${IMAGE_TAG}

az containerapp update \
  --name poshmcp \
  --resource-group poshmcp-production \
  --image ${REGISTRY_NAME}.azurecr.io/poshmcp:${IMAGE_TAG}

# ===================================
# ROLLBACK TO PREVIOUS VERSION
# ===================================

# List all revisions
az containerapp revision list \
  --name poshmcp \
  --resource-group poshmcp-production \
  --output table

# Activate previous revision
az containerapp revision activate \
  --name poshmcp \
  --resource-group poshmcp-production \
  --revision poshmcp--<revision-id>

# ===================================
# MONITORING AND DEBUGGING
# ===================================

# View Application Insights
az monitor app-insights component show \
  --app poshmcp-insights \
  --resource-group poshmcp-production

# Query logs with Log Analytics
az monitor log-analytics query \
  --workspace $(az containerapp env show --name poshmcp-env --resource-group poshmcp-production --query "properties.appLogsConfiguration.logAnalyticsConfiguration.customerId" -o tsv) \
  --analytics-query "ContainerAppConsoleLogs_CL | where ContainerAppName_s == 'poshmcp' | where TimeGenerated > ago(1h) | order by TimeGenerated desc | take 100"

# ===================================
# CLEANUP
# ===================================

# Delete just the container app
az containerapp delete --name poshmcp --resource-group poshmcp-production --yes

# Delete entire resource group (including ACR, logs, etc.)
az group delete --name poshmcp-production --yes

# ===================================
# CI/CD GITHUB ACTIONS EXAMPLE
# ===================================

# .github/workflows/deploy-azure.yml
name: Deploy to Azure Container Apps

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      
      - name: Deploy
        run: |
          export REGISTRY_NAME=${{ secrets.REGISTRY_NAME }}
          export RESOURCE_GROUP=${{ secrets.RESOURCE_GROUP }}
          cd infrastructure/azure
          ./deploy.sh
        env:
          IMAGE_TAG: ${{ github.sha }}

# Required GitHub secrets:
# - AZURE_CREDENTIALS: Service principal JSON
# - REGISTRY_NAME: ACR name
# - RESOURCE_GROUP: Resource group name

# ===================================
# TROUBLESHOOTING COMMANDS
# ===================================

# Check Container Apps provider registration
az provider show --namespace Microsoft.App --query registrationState

# Register if needed
az provider register --namespace Microsoft.App

# Check replica status
az containerapp replica list \
  --name poshmcp \
  --resource-group poshmcp-production \
  --output table

# Get specific replica logs
az containerapp logs show \
  --name poshmcp \
  --resource-group poshmcp-production \
  --replica <replica-name> \
  --tail 100

# Check environment issues
az containerapp env show \
  --name poshmcp-env \
  --resource-group poshmcp-production

# Validate Bicep template locally
az bicep build --file main.bicep

# Test deployment (what-if mode)
az deployment group what-if \
  --name poshmcp-whatif \
  --resource-group poshmcp-production \
  --template-file main.bicep \
  --parameters @parameters.json
