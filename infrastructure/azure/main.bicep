// Main Bicep template for deploying PoshMcp to Azure Container Apps
// This template orchestrates deployment at subscription scope:
// - Creates Resource Group
// - Deploys all RG-scoped resources via module (resources.bicep)
// - Assigns RBAC roles to Managed Identity at subscription scope
//
// Architecture:
// main.bicep (subscription) -> resources.bicep (resourceGroup) -> all Azure resources

targetScope = 'subscription'

@description('Name of the resource group')
param resourceGroupName string = 'rg-poshmcp'

@description('Azure region for all resources')
param location string = deployment().location

@description('Name of the Container App')
param containerAppName string = 'poshmcp'

@description('Name of the Container Apps Environment')
param environmentName string = 'poshmcp-env'

@description('Container image to deploy (format: registry/image:tag)')
param containerImage string

@description('Container registry server (e.g., myregistry.azurecr.io)')
param containerRegistryServer string

@description('Container registry username (empty for managed identity)')
param containerRegistryUsername string = ''

@description('Container registry password (empty for managed identity)')
@secure()
param containerRegistryPassword string = ''

@description('Minimum number of replicas')
@minValue(0)
@maxValue(30)
param minReplicas int = 1

@description('Maximum number of replicas')
@minValue(1)
@maxValue(30)
param maxReplicas int = 10

@description('CPU cores per replica (0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0)')
param cpuCores string = '0.5'

@description('Memory per replica in Gi (0.5, 1.0, 1.5, 2.0, 3.0, 3.5, 4.0)')
param memoryGi string = '1.0'

@description('PowerShell function names to expose (comma-separated)')
param powerShellFunctions string = 'Get-SomeData'

@description('Enable dynamic reload tools')
param enableDynamicReloadTools bool = true

@description('Azure role to assign to Managed Identity at subscription scope')
@allowed([
  'Reader'
  'Contributor'
  'Owner'
  'None'
])
param managedIdentityRole string = 'Reader'

@description('Tags to apply to all resources')
param tags object = {
  application: 'PoshMcp'
  environment: 'production'
}

// Create resource group at subscription scope
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// Deploy all resource group-scoped resources via module
module resources 'resources.bicep' = {
  scope: az.resourceGroup(rg.name)
  params: {
    containerAppName: containerAppName
    environmentName: environmentName
    location: location
    containerImage: containerImage
    containerRegistryServer: containerRegistryServer
    containerRegistryUsername: containerRegistryUsername
    containerRegistryPassword: containerRegistryPassword
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    cpuCores: cpuCores
    memoryGi: memoryGi
    powerShellFunctions: powerShellFunctions
    enableDynamicReloadTools: enableDynamicReloadTools
    tags: tags
  }
}

// Role Assignment for Managed Identity at Subscription Scope
// This is deployed at subscription scope to grant permissions across the subscription
var roleDefinitions = {
  Reader: 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
  Contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  Owner: '8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (managedIdentityRole != 'None') {
  name: guid(subscription().id, resourceGroupName, containerAppName, roleDefinitions[managedIdentityRole])
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roleDefinitions[managedIdentityRole]
    )
    principalId: resources.outputs.managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Outputs from subscription-level deployment
output resourceGroupName string = rg.name
output containerAppFQDN string = resources.outputs.containerAppFQDN
output containerAppUrl string = resources.outputs.containerAppUrl
output logAnalyticsWorkspaceId string = resources.outputs.logAnalyticsWorkspaceId
output appInsightsInstrumentationKey string = resources.outputs.appInsightsInstrumentationKey
output appInsightsConnectionString string = resources.outputs.appInsightsConnectionString
output managedIdentityClientId string = resources.outputs.managedIdentityClientId
output managedIdentityPrincipalId string = resources.outputs.managedIdentityPrincipalId
output roleAssignmentId string = managedIdentityRole != 'None' ? roleAssignment.id : ''
output managedIdentityRoleAssigned string = managedIdentityRole
