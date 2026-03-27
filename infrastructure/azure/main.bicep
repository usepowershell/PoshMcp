// Main Bicep template for deploying PoshMcp to Azure Container Apps
// This template creates:
// - Log Analytics Workspace for monitoring
// - Application Insights for APM
// - Container Apps Environment
// - Container App running PoshMcp in web mode
// - Managed Identity for secure Azure resource access

@description('Name of the Container App')
param containerAppName string = 'poshmcp'

@description('Name of the Container Apps Environment')
param environmentName string = 'poshmcp-env'

@description('Azure region for all resources')
param location string = resourceGroup().location

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

@description('Tags to apply to all resources')
param tags object = {
  application: 'PoshMcp'
  environment: 'production'
}

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

// Application Insights for APM and distributed tracing
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${containerAppName}-insights'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Container Apps Environment with integrated Log Analytics
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
    zoneRedundant: false
  }
}

// Managed Identity for the Container App
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${containerAppName}-identity'
  location: location
  tags: tags
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
      registries: containerRegistryUsername != '' ? [
        {
          server: containerRegistryServer
          username: containerRegistryUsername
          passwordSecretRef: 'registry-password'
        }
      ] : []
      secrets: containerRegistryPassword != '' ? [
        {
          name: 'registry-password'
          value: containerRegistryPassword
        }
        {
          name: 'appinsights-connection-string'
          value: appInsights.properties.ConnectionString
        }
      ] : [
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
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'POSHMCP_MODE'
              value: 'web'
            }
            {
              name: 'PowerShellConfiguration__FunctionNames__0'
              value: powerShellFunctions
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
          ]
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
}

// Outputs for reference and automation
output containerAppFQDN string = containerApp.properties.configuration.ingress.fqdn
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output logAnalyticsWorkspaceId string = logAnalytics.id
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output managedIdentityClientId string = managedIdentity.properties.clientId
output managedIdentityPrincipalId string = managedIdentity.properties.principalId
