output "resource_group_name" {
  description = "Resource group used by the scenario deployment."
  value       = module.core.resource_group_name
}

output "container_app_environment_id" {
  description = "Azure Container Apps managed environment resource ID."
  value       = module.scenario_apps.container_app_environment_id
}

output "log_analytics_workspace_id" {
  description = "Log Analytics workspace resource ID used by the managed environment."
  value       = module.core.log_analytics_workspace_id
}

output "application_insights_name" {
  description = "Application Insights resource name, when enabled."
  value       = module.core.application_insights_name
}

output "application_insights_connection_string" {
  description = "Application Insights connection string, when enabled."
  value       = module.core.application_insights_connection_string
  sensitive   = true
}

output "managed_identity_client_id" {
  description = "Client ID of the user-assigned managed identity attached to every Container App."
  value       = module.identity.managed_identity_client_id
}

output "managed_identity_principal_id" {
  description = "Principal ID of the user-assigned managed identity attached to every Container App."
  value       = module.identity.managed_identity_principal_id
}

output "acr_login_server" {
  description = "Login server for the Terraform-created ACR, or null when ACR creation is disabled."
  value       = module.core.acr_login_server
}

output "scenario_container_apps" {
  description = "Container App resource names and IDs keyed by scenario."
  value       = module.scenario_apps.scenario_container_apps
}

output "scenario_fqdns" {
  description = "Container App FQDNs keyed by scenario. Null for scenarios without ingress."
  value       = module.scenario_apps.scenario_fqdns
}

output "scenario_urls" {
  description = "HTTPS URLs keyed by scenario. Null for scenarios without ingress."
  value       = module.scenario_apps.scenario_urls
}

output "scenario_health_urls" {
  description = "Health endpoint URLs keyed by scenario. Null for scenarios without ingress."
  value       = module.scenario_apps.scenario_health_urls
}

output "storage_account_name" {
  description = "Storage account name when storage mounts are enabled."
  value       = module.core.storage_account_name
}

output "storage_share_name" {
  description = "Azure Files share name when storage mounts are enabled."
  value       = module.core.storage_share_name
}
