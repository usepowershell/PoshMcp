// Resource group-scoped resources module
// This module contains all resources that are deployed within the resource group

targetScope = 'resourceGroup'

@description('Name of the Container App')
param containerAppName string

@description('Name of the Container Apps Environment')
param environmentName string

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Container image to deploy (e.g., myregistry.azurecr.io/poshmcp:latest)')
param containerImage string

@description('Container registry server (e.g., myregistry.azurecr.io)')
param containerRegistryServer string = ''

@description('Container registry username (leave empty for managed identity auth)')
param containerRegistryUsername string = ''

@description('Container registry password (leave empty for managed identity auth)')
@secure()
param containerRegistryPassword string = ''

@description('Minimum number of container replicas')
@minValue(0)
@maxValue(30)
param minReplicas int = 1

@description('Maximum number of container replicas')
@minValue(1)
@maxValue(30)
param maxReplicas int = 10

@description('CPU cores allocated to each container instance')
param cpuCores string = '0.5'

@description('Memory in GB allocated to each container instance')
param memoryGi string = '1.0'

@description('PowerShell functions to expose as MCP tools (comma-separated)' )
param powerShellFunctions string = 'Get-Process,Get-Service'

@description('Enable dynamic reload tools for hot configuration updates')
param enableDynamicReloadTools bool = true

@description('Resource tags for cost tracking and organization')
param tags object = {}

var acrPullRoleDefinitionId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var containerRegistryName = !empty(containerRegistryServer) ? split(containerRegistryServer, '.')[0] : 'unused'
var normalizedPowerShellFunctions = [for functionName in split(powerShellFunctions, ','): trim(functionName)]
var powerShellCommandEnvVars = [for (commandName, index) in normalizedPowerShellFunctions: {
  name: 'PowerShellConfiguration__CommandNames__${index}'
  value: commandName
}]

// Log Analytics Workspace for Container Apps logs and metrics
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${containerAppName}-logs'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// Application Insights for monitoring and tracing
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${containerAppName}-insights'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

// Container Apps Environment
resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// Managed Identity for the Container App
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${containerAppName}-identity'
  location: location
  tags: tags
}

// Reference the existing ACR (assumed to be in the same resource group)
resource existingAcr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = if (!empty(containerRegistryServer)) {
  name: containerRegistryName
}

// Grant the managed identity AcrPull access on the container registry (no passwords required)
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(containerRegistryServer)) {
  name: guid(containerRegistryServer, managedIdentity.id, acrPullRoleDefinitionId)
  scope: existingAcr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleDefinitionId)
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Container App running PoshMcp in web/HTTP mode
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: containerRegistryUsername != ''
        ? [
            {
              server: containerRegistryServer
              username: containerRegistryUsername
              passwordSecretRef: 'registry-password'
            }
          ]
        : !empty(containerRegistryServer)
          ? [
              {
                server: containerRegistryServer
                identity: managedIdentity.id
              }
            ]
          : []
      secrets: containerRegistryPassword != ''
        ? [
            {
              name: 'registry-password'
              value: containerRegistryPassword
            }
            {
              name: 'appinsights-connection-string'
              value: appInsights.properties.ConnectionString
            }
          ]
        : [
            {
              name: 'appinsights-connection-string'
              value: appInsights.properties.ConnectionString
            }
          ]
      activeRevisionsMode: 'Single'
    }
    template: {
      containers: [
        {
          name: 'poshmcp'
          image: containerImage
          resources: {
            cpu: json(cpuCores)
            memory: '${memoryGi}Gi'
          }
          env: concat(
            [
              {
                name: 'ASPNETCORE_ENVIRONMENT'
                value: 'Production'
              }
              {
                name: 'ASPNETCORE_URLS'
                value: 'http://+:8080'
              }
              {
                name: 'POSHMCP_TRANSPORT'
                value: 'http'
              }
              {
                name: 'PowerShellConfiguration__EnableDynamicReloadTools'
                value: string(enableDynamicReloadTools)
              }
              {
                name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
                secretRef: 'appinsights-connection-string'
              }
              {
                name: 'AZURE_CLIENT_ID'
                value: managedIdentity.properties.clientId
              }
            ],
            powerShellCommandEnvVars
          )
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 3
              failureThreshold: 3
            }
            {
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 0
              periodSeconds: 5
              timeoutSeconds: 3
              failureThreshold: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
  dependsOn: [
    acrPullRoleAssignment
  ]
}

// Outputs for reference and automation
output containerAppFQDN string = containerApp.properties.configuration.ingress.fqdn
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output logAnalyticsWorkspaceId string = logAnalytics.id
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output managedIdentityClientId string = managedIdentity.properties.clientId
output managedIdentityPrincipalId string = managedIdentity.properties.principalId
