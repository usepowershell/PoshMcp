# Azure Bicep modularization architecture

## Overview

The PoshMcp Azure infrastructure uses a **modularized Bicep deployment** to properly handle resources at different Azure scopes. This architecture enables subscription-scoped role assignments while maintaining clean separation of concerns.

## Architecture

```
Deployment Scope Hierarchy:
├── main.bicep (subscription scope)
│   ├── Creates Resource Group
│   ├── Deploys resources.bicep module → (resource group scope)
│   └── Assigns RBAC roles at subscription level
│
└── resources.bicep (resource group scope)
    ├── Log Analytics Workspace
    ├── Application Insights
    ├── Container Apps Environment
    ├── Managed Identity
    └── Container App (PoshMcp)
```

## Why modularization?

### The problem we solved

Previously, `main.bicep` attempted to deploy resource group-scoped resources directly from subscription scope using the `scope:` property on individual resources. This violates Bicep's scope rules:

**Bicep Rule:** A resource's scope must match the Bicep file's `targetScope`. To deploy to a different scope, you **must use modules**.

### Errors fixed

1. **BCP139**: "A resource's scope must match the scope of the Bicep file"
2. **BCP265**: "`resourceGroup` is not a function. Did you mean `az.resourceGroup`?"
3. **BCP037**: "The property 'scope' is not allowed on objects of type..."

### Benefits of this design

✅ **Subscription-Scoped Role Assignments**: Managed Identity can receive permissions across the entire subscription  
✅ **Clean Separation**: Each Bicep file operates at a single, consistent scope  
✅ **Reusable Modules**: `resources.bicep` can be reused in different contexts  
✅ **Best Practices**: Follows official Bicep modularization patterns  
✅ **Maintainable**: Clear boundaries between subscription and resource group resources

## File responsibilities

### main.bicep (subscription scope)

**Location:** `infrastructure/azure/main.bicep`  
**Target Scope:** `subscription`

**Responsibilities:**
- Accept all deployment parameters
- Create the resource group
- Invoke `resources.bicep` module with resource group scope
- Assign RBAC roles to Managed Identity at subscription level
- Aggregate outputs from module

**Key Resources:**
```bicep
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01'
module resources 'resources.bicep'
resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01'
```

**Usage:**
```bash
az deployment sub create \
  --location eastus \
  --template-file main.bicep \
  --parameters @parameters.json
```

### resources.bicep (resource group scope)

**Location:** `infrastructure/azure/resources.bicep`  
**Target Scope:** `resourceGroup`

**Responsibilities:**
- Deploy all Azure resources within the resource group
- Configure Application Insights and Log Analytics
- Create and configure Container Apps Environment
- Deploy the PoshMcp Container App
- Create User-Assigned Managed Identity
- Return resource IDs and properties as outputs

**Key Resources:**
```bicep
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01'
resource appInsights 'Microsoft.Insights/components@2020-02-02'
resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2023-05-01'
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31'
resource containerApp 'Microsoft.App/containerApps@2023-05-01'
```

**Note:** This module is **invoked by main.bicep** - do not deploy it directly unless you're testing in isolation.

## Parameter flow

### Deployment command

```bash
az deployment sub create \
  --location eastus \
  --template-file infrastructure/azure/main.bicep \
  --parameters @infrastructure/azure/parameters.json
```

### Flow diagram

```
parameters.json
     ↓
main.bicep (subscription scope)
     ├── Uses: resourceGroupName, location, managedIdentityRole, tags
     ├── Creates: Resource Group
     └── Passes remaining params to module ↓
              resources.bicep (resource group scope)
                   ├── Uses: containerAppName, containerImage, etc.
                   └── Returns: outputs (managedIdentityPrincipalId, etc.)
     ↑
main.bicep receives module outputs
     └── Uses: managedIdentityPrincipalId for role assignment
```

### Parameter breakdown

**Consumed by main.bicep only:**
- `resourceGroupName` - Name of the resource group to create
- `managedIdentityRole` - Subscription-level role to assign (Reader/Contributor/Owner/None)

**Passed through to resources.bicep:**
- `containerAppName` - Name of the Container App
- `environmentName` - Name of Container Apps Environment
- `location` - Azure region
- `containerImage` - Container image to deploy
- `containerRegistryServer` - Registry server URL
- `containerRegistryUsername` - Registry username (optional)
- `containerRegistryPassword` - Registry password (optional)
- `minReplicas` - Minimum replica count
- `maxReplicas` - Maximum replica count
- `cpuCores` - CPU allocation per replica
- `memoryGi` - Memory allocation per replica
- `powerShellFunctions` - PowerShell functions to expose
- `enableDynamicReloadTools` - Enable hot reload
- `tags` - Resource tags

## Outputs

### From main.bicep (aggregated)

All outputs from `resources.bicep` are surfaced at the subscription level:

```bicep
output resourceGroupName string
output containerAppFQDN string
output containerAppUrl string
output logAnalyticsWorkspaceId string
output appInsightsInstrumentationKey string
output appInsightsConnectionString string
output managedIdentityClientId string
output managedIdentityPrincipalId string
output roleAssignmentId string
output managedIdentityRoleAssigned string
```

### From resources.bicep (module)

```bicep
output containerAppFQDN string
output containerAppUrl string
output logAnalyticsWorkspaceId string
output appInsightsInstrumentationKey string
output appInsightsConnectionString string
output managedIdentityClientId string
output managedIdentityPrincipalId string
```

## Migration path

### From old architecture

**Before (Broken):**
```bicep
// main.bicep
targetScope = 'subscription'

resource logAnalytics '...' = {
  scope: resourceGroup(resourceGroupName)  // ❌ Invalid
  // ...
}
```

**After (Working):**
```bicep
// main.bicep
targetScope = 'subscription'

module resources 'resources.bicep' = {
  scope: az.resourceGroup(rg.name)  // ✅ Correct
  params: { /* ... */ }
}

// resources.bicep
targetScope = 'resourceGroup'

resource logAnalytics '...' = {
  // Deployed at resource group scope (matches targetScope)
  // ...
}
```

### Migration steps

If you have an **existing deployment** using the old broken structure:

1. **Export existing resource configuration** (if needed):
   ```bash
   az group export --name rg-poshmcp --output json > existing-resources.json
   ```

2. **Update your deployment scripts** to use subscription-scoped deployment:
   ```bash
   # Old (would fail)
   az deployment group create --resource-group rg-poshmcp ...
   
   # New (correct)
   az deployment sub create --location eastus --template-file main.bicep ...
   ```

3. **Deploy the new modularized structure**:
   - Bicep will **update existing resources** in place (no downtime)
   - Role assignment is **idempotent** (safe to rerun)

4. **Verify outputs after deployment**:
   ```bash
   az deployment sub show \
     --name <deployment-name> \
     --query properties.outputs
   ```

### No breaking changes

✅ **Same parameters** - `parameters.json` format unchanged  
✅ **Same resources** - All resources remain identical  
✅ **Same outputs** - Output names preserved for automation  
✅ **Zero downtime** - Bicep updates resources in place

## Best practices applied

Based on official [Bicep best practices](https://learn.microsoft.com/azure/azure-resource-manager/bicep/best-practices):

1. ✅ **No `name` property on module** - Not required in modern Bicep
2. ✅ **Module scope uses `az.resourceGroup()`** - Correct function syntax
3. ✅ **Symbolic references** - Using module outputs instead of `resourceId()` functions
4. ✅ **Deterministic role assignment name** - Using `guid()` with static parameters
5. ✅ **Safe dereference** - Using `.?` operator where appropriate
6. ✅ **Secure parameters** - `@secure()` decorator on sensitive parameters

## Troubleshooting

### "BCP120: This expression requires a value that can be calculated at the start"

**Problem:** Role assignment GUID uses runtime values  
**Solution:** Use only deployment-time values in `guid()`:
```bicep
name: guid(subscription().id, resourceGroupName, containerAppName, roleDefinitionId)
```

### "BCP139: A resource's scope must match the scope of the Bicep file"

**Problem:** Trying to deploy cross-scope resources directly  
**Solution:** Use a module with explicit scope:
```bicep
module resources 'resources.bicep' = {
  scope: az.resourceGroup(rg.name)
}
```

### "BCP265: The name 'resourceGroup' is not a function"

**Problem:** Old Bicep function syntax  
**Solution:** Use the `az` namespace:
```bicep
scope: az.resourceGroup(rg.name)  // Correct
scope: resourceGroup(rg.name)     // Old/wrong
```

## Testing

### Validate Bicep syntax

```bash
# Check main.bicep
az bicep build --file infrastructure/azure/main.bicep

# Check resources.bicep
az bicep build --file infrastructure/azure/resources.bicep
```

### Dry run (what-if)

```bash
az deployment sub what-if \
  --location eastus \
  --template-file infrastructure/azure/main.bicep \
  --parameters @infrastructure/azure/parameters.json
```

### Deploy

```bash
# Full deployment
./infrastructure/azure/deploy.sh

# Or directly:
az deployment sub create \
  --location eastus \
  --template-file infrastructure/azure/main.bicep \
  --parameters @infrastructure/azure/parameters.json
```

## See also

- [ARCHITECTURE.md](./ARCHITECTURE.md) — Overall Azure infrastructure design
- [README.md](./README.md) — Full deployment guide
- [QUICKSTART.md](./QUICKSTART.md) — Quick-reference deployment commands
- [BICEP-REFACTOR-SUMMARY.md](./BICEP-REFACTOR-SUMMARY.md) — Historical record of the modularization refactor
- [Bicep modules — Microsoft Learn](https://learn.microsoft.com/azure/azure-resource-manager/bicep/modules)
- [Deployment scopes — Microsoft Learn](https://learn.microsoft.com/azure/azure-resource-manager/bicep/deploy-to-subscription)
