locals {
  required_providers = var.enable_resource_provider_registration ? toset(["Microsoft.App", "Microsoft.OperationalInsights", "Microsoft.Insights", "Microsoft.ContainerRegistry"]) : toset([])

  effective_container_image = trimspace(var.container_image) != "" ? var.container_image : "${module.core.acr_login_server}/${var.container_image_repository}:${var.container_image_tag}"

  common_tags = merge(var.tags, {
    workload    = "poshmcp"
    environment = var.environment_name
    managed-by  = "terraform"
  })
}

resource "azurerm_resource_provider_registration" "required" {
  for_each = local.required_providers

  name = each.key
}

module "core" {
  source = "./modules/core"

  name_prefix                  = var.name_prefix
  environment_name             = var.environment_name
  location                     = var.location
  create_resource_group        = var.create_resource_group
  resource_group_name          = var.resource_group_name
  create_container_registry    = var.create_container_registry
  container_registry_name      = var.container_registry_name
  log_analytics_retention_days = var.log_analytics_retention_days
  application_insights_enabled = var.application_insights_enabled
  enable_storage_mounts        = var.enable_storage_mounts
  storage_share_name           = var.storage_share_name
  tags                         = local.common_tags

  depends_on = [azurerm_resource_provider_registration.required]
}

module "identity" {
  source = "./modules/identity"

  name_prefix         = var.name_prefix
  environment_name    = var.environment_name
  location            = module.core.resource_group_location
  resource_group_name = module.core.resource_group_name
  acr_id              = module.core.acr_id
  enable_acr_pull     = var.create_container_registry
  tags                = local.common_tags
}

module "scenario_apps" {
  source = "./modules/scenario-app"

  name_prefix                        = var.name_prefix
  environment_name                   = var.environment_name
  location                           = module.core.resource_group_location
  resource_group_name                = module.core.resource_group_name
  container_app_environment_name     = var.container_app_environment_name
  log_analytics_workspace_id         = module.core.log_analytics_workspace_id
  managed_identity_id                = module.identity.managed_identity_id
  managed_identity_client_id         = module.identity.managed_identity_client_id
  container_registry_server          = var.create_container_registry ? module.core.acr_login_server : var.container_registry_server
  container_registry_username        = var.container_registry_username
  container_registry_password        = var.container_registry_password
  use_managed_identity_registry_auth = var.create_container_registry && var.container_registry_username == ""
  default_container_image            = local.effective_container_image
  container_image_repository         = var.container_image_repository
  application_insights_enabled       = var.application_insights_enabled
  application_insights_connection    = module.core.application_insights_connection_string
  enable_storage_mounts              = var.enable_storage_mounts
  storage_account_name               = module.core.storage_account_name
  storage_account_access_key         = module.core.storage_account_primary_access_key
  storage_share_name                 = module.core.storage_share_name
  container_app_scenarios            = var.container_app_scenarios
  container_app_scenario_secrets     = var.container_app_scenario_secrets
  default_min_replicas               = var.default_min_replicas
  default_max_replicas               = var.default_max_replicas
  default_cpu                        = var.default_cpu
  default_memory                     = var.default_memory
  default_external_ingress_enabled   = var.external_ingress_enabled
  tags                               = local.common_tags

  depends_on = [module.identity]
}
