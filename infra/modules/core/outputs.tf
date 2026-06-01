output "resource_group_id" {
  value = local.resource_group_id
}

output "resource_group_name" {
  value = local.effective_rg_name
}

output "resource_group_location" {
  value = local.resource_group_location
}

output "acr_id" {
  value = var.create_container_registry ? azurerm_container_registry.this[0].id : null
}

output "acr_name" {
  value = var.create_container_registry ? azurerm_container_registry.this[0].name : null
}

output "acr_login_server" {
  value = var.create_container_registry ? azurerm_container_registry.this[0].login_server : null
}

output "log_analytics_workspace_id" {
  value = azurerm_log_analytics_workspace.this.id
}

output "log_analytics_workspace_name" {
  value = azurerm_log_analytics_workspace.this.name
}

output "application_insights_name" {
  value = var.application_insights_enabled ? azurerm_application_insights.this[0].name : null
}

output "application_insights_connection_string" {
  value     = var.application_insights_enabled ? azurerm_application_insights.this[0].connection_string : null
  sensitive = true
}

output "storage_account_name" {
  value = var.enable_storage_mounts ? azurerm_storage_account.this[0].name : null
}

output "storage_account_primary_access_key" {
  value     = var.enable_storage_mounts ? azurerm_storage_account.this[0].primary_access_key : null
  sensitive = true
}

output "storage_share_name" {
  value = var.enable_storage_mounts ? azurerm_storage_share.this[0].name : null
}
