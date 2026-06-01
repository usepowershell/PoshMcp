terraform {
  required_providers {
    azurerm = {
      source = "hashicorp/azurerm"
    }
    random = {
      source = "hashicorp/random"
    }
  }
}

locals {
  normalized_prefix      = substr(replace(lower(var.name_prefix), "-", ""), 0, 16)
  normalized_environment = substr(replace(lower(var.environment_name), "-", ""), 0, 8)
  resource_group_name    = trimspace(var.resource_group_name) != "" ? var.resource_group_name : "rg-${var.name_prefix}-${var.environment_name}"
}

data "azurerm_resource_group" "existing" {
  count = var.create_resource_group ? 0 : 1

  name = local.resource_group_name
}

resource "azurerm_resource_group" "this" {
  count = var.create_resource_group ? 1 : 0

  name     = local.resource_group_name
  location = var.location
  tags     = var.tags
}

locals {
  resource_group_id       = var.create_resource_group ? azurerm_resource_group.this[0].id : data.azurerm_resource_group.existing[0].id
  effective_rg_name       = var.create_resource_group ? azurerm_resource_group.this[0].name : data.azurerm_resource_group.existing[0].name
  resource_group_location = var.create_resource_group ? azurerm_resource_group.this[0].location : data.azurerm_resource_group.existing[0].location
}

resource "random_string" "suffix" {
  length  = 6
  lower   = true
  upper   = false
  numeric = true
  special = false
}

locals {
  generated_acr_name   = substr("cr${local.normalized_prefix}${local.normalized_environment}${random_string.suffix.result}", 0, 50)
  effective_acr_name   = trimspace(var.container_registry_name) != "" ? var.container_registry_name : local.generated_acr_name
  storage_account_name = substr("st${local.normalized_prefix}${local.normalized_environment}${random_string.suffix.result}", 0, 24)
  log_analytics_name   = "law-${var.name_prefix}-${var.environment_name}"
  app_insights_name    = "appi-${var.name_prefix}-${var.environment_name}"
}

resource "azurerm_container_registry" "this" {
  count = var.create_container_registry ? 1 : 0

  name                = local.effective_acr_name
  resource_group_name = local.effective_rg_name
  location            = local.resource_group_location
  sku                 = "Standard"
  admin_enabled       = false
  tags                = var.tags
}

resource "azurerm_log_analytics_workspace" "this" {
  name                = local.log_analytics_name
  location            = local.resource_group_location
  resource_group_name = local.effective_rg_name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_analytics_retention_days
  tags                = var.tags
}

resource "azurerm_application_insights" "this" {
  count = var.application_insights_enabled ? 1 : 0

  name                = local.app_insights_name
  location            = local.resource_group_location
  resource_group_name = local.effective_rg_name
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"
  tags                = var.tags
}

resource "azurerm_storage_account" "this" {
  count = var.enable_storage_mounts ? 1 : 0

  name                            = local.storage_account_name
  resource_group_name             = local.effective_rg_name
  location                        = local.resource_group_location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  account_kind                    = "StorageV2"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = true
  tags                            = var.tags
}

resource "azurerm_storage_share" "this" {
  count = var.enable_storage_mounts ? 1 : 0

  name               = var.storage_share_name
  storage_account_id = azurerm_storage_account.this[0].id
  quota              = 10
  access_tier        = "TransactionOptimized"
}
