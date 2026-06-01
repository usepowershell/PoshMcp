terraform {
  required_providers {
    azurerm = {
      source = "hashicorp/azurerm"
    }
  }
}

locals {
  container_name              = "poshmcp"
  environment_name            = trimspace(var.container_app_environment_name) != "" ? var.container_app_environment_name : "cae-${var.name_prefix}-${var.environment_name}"
  environment_storage_name    = "poshmcp-config"
  has_registry_server         = trimspace(var.container_registry_server) != ""
  has_registry_credentials    = trimspace(var.container_registry_username) != "" && try(trimspace(var.container_registry_password), "") != ""
  scenario_storage_mount_path = "/app/config"

  enabled_scenarios = {
    for scenario_key, scenario in var.container_app_scenarios : scenario_key => scenario
    if try(scenario.enabled, true)
  }

  scenario_names = {
    for scenario_key, scenario in local.enabled_scenarios :
    scenario_key => substr("ca-${var.name_prefix}-${coalesce(try(scenario.name_suffix, null), scenario_key)}", 0, 32)
  }

  scenario_images = {
    for scenario_key, scenario in local.enabled_scenarios :
    scenario_key => trimspace(coalesce(try(scenario.image, null), "")) != "" ? scenario.image : (
      trimspace(coalesce(try(scenario.image_tag, null), "")) != "" && local.has_registry_server ? "${var.container_registry_server}/${var.container_image_repository}:${scenario.image_tag}" : var.default_container_image
    )
  }

  scenario_transport_modes = {
    for scenario_key, scenario in local.enabled_scenarios :
    scenario_key => lower(try(scenario.transport_mode, "http"))
  }

  scenario_ingress_enabled = {
    for scenario_key, scenario in local.enabled_scenarios :
    scenario_key => try(scenario.ingress_enabled, local.scenario_transport_modes[scenario_key] == "http")
  }

  scenario_plain_env = {
    for scenario_key, scenario in local.enabled_scenarios :
    scenario_key => merge({
      ASPNETCORE_ENVIRONMENT = "Production"
      ASPNETCORE_URLS        = "http://+:${try(scenario.target_port, 8080)}"
      POSHMCP_TRANSPORT      = local.scenario_transport_modes[scenario_key]
      AZURE_CLIENT_ID        = var.managed_identity_client_id
    }, try(scenario.env, {}))
  }

  scenario_secret_names = {
    for scenario_key in keys(local.enabled_scenarios) :
    scenario_key => toset(keys(nonsensitive(lookup(var.container_app_scenario_secrets, scenario_key, {}))))
  }
}

resource "azurerm_container_app_environment" "this" {
  name                       = local.environment_name
  location                   = var.location
  resource_group_name        = var.resource_group_name
  log_analytics_workspace_id = var.log_analytics_workspace_id
  tags                       = var.tags
}

resource "azurerm_container_app_environment_storage" "config" {
  count = var.enable_storage_mounts ? 1 : 0

  name                         = local.environment_storage_name
  container_app_environment_id = azurerm_container_app_environment.this.id
  account_name                 = var.storage_account_name
  share_name                   = var.storage_share_name
  access_key                   = var.storage_account_access_key
  access_mode                  = "ReadWrite"
}

resource "azurerm_container_app" "scenario" {
  for_each = local.enabled_scenarios

  name                         = local.scenario_names[each.key]
  resource_group_name          = var.resource_group_name
  container_app_environment_id = azurerm_container_app_environment.this.id
  revision_mode                = "Single"
  tags                         = merge(var.tags, { scenario = each.key })

  identity {
    type         = "UserAssigned"
    identity_ids = [var.managed_identity_id]
  }

  dynamic "registry" {
    for_each = local.has_registry_server ? [1] : []

    content {
      server               = var.container_registry_server
      identity             = var.use_managed_identity_registry_auth ? var.managed_identity_id : null
      username             = local.has_registry_credentials ? var.container_registry_username : null
      password_secret_name = local.has_registry_credentials ? "registry-password" : null
    }
  }

  dynamic "secret" {
    for_each = var.application_insights_enabled ? [1] : []

    content {
      name  = "appinsights-connection-string"
      value = var.application_insights_connection
    }
  }

  dynamic "secret" {
    for_each = local.has_registry_credentials ? [1] : []

    content {
      name  = "registry-password"
      value = var.container_registry_password
    }
  }

  dynamic "secret" {
    for_each = local.scenario_secret_names[each.key]

    content {
      name  = secret.value
      value = lookup(lookup(var.container_app_scenario_secrets, each.key, {}), secret.value)
    }
  }

  template {
    min_replicas = try(each.value.min_replicas, var.default_min_replicas)
    max_replicas = try(each.value.max_replicas, var.default_max_replicas)

    dynamic "http_scale_rule" {
      for_each = local.scenario_transport_modes[each.key] == "http" ? [1] : []

      content {
        name                = "http-scaling"
        concurrent_requests = 50
      }
    }

    dynamic "volume" {
      for_each = var.enable_storage_mounts && try(each.value.mount_storage, false) ? [1] : []

      content {
        name         = local.environment_storage_name
        storage_name = azurerm_container_app_environment_storage.config[0].name
        storage_type = "AzureFile"
      }
    }

    container {
      name    = local.container_name
      image   = local.scenario_images[each.key]
      cpu     = try(each.value.cpu, var.default_cpu)
      memory  = try(each.value.memory, var.default_memory)
      command = try(each.value.command, null)
      args    = try(each.value.args, null)

      dynamic "env" {
        for_each = local.scenario_plain_env[each.key]

        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = var.application_insights_enabled ? [1] : []

        content {
          name        = "APPLICATIONINSIGHTS_CONNECTION_STRING"
          secret_name = "appinsights-connection-string"
        }
      }

      dynamic "env" {
        for_each = try(each.value.secret_env, {})

        content {
          name        = env.key
          secret_name = env.value
        }
      }

      dynamic "volume_mounts" {
        for_each = var.enable_storage_mounts && try(each.value.mount_storage, false) ? [1] : []

        content {
          name = local.environment_storage_name
          path = local.scenario_storage_mount_path
        }
      }

      dynamic "startup_probe" {
        for_each = local.scenario_transport_modes[each.key] == "http" ? [1] : []

        content {
          transport               = "HTTP"
          path                    = try(each.value.health_path, "/health")
          port                    = try(each.value.target_port, 8080)
          initial_delay           = 0
          interval_seconds        = 5
          timeout                 = 3
          failure_count_threshold = 30
        }
      }

      dynamic "readiness_probe" {
        for_each = local.scenario_transport_modes[each.key] == "http" ? [1] : []

        content {
          transport               = "HTTP"
          path                    = try(each.value.readiness_path, "/health/ready")
          port                    = try(each.value.target_port, 8080)
          initial_delay           = 5
          interval_seconds        = 10
          timeout                 = 3
          failure_count_threshold = 3
        }
      }

      dynamic "liveness_probe" {
        for_each = local.scenario_transport_modes[each.key] == "http" ? [1] : []

        content {
          transport               = "HTTP"
          path                    = try(each.value.readiness_path, "/health/ready")
          port                    = try(each.value.target_port, 8080)
          initial_delay           = 10
          interval_seconds        = 30
          timeout                 = 5
          failure_count_threshold = 3
        }
      }
    }
  }

  dynamic "ingress" {
    for_each = local.scenario_ingress_enabled[each.key] ? [1] : []

    content {
      external_enabled           = try(each.value.external_ingress, var.default_external_ingress_enabled)
      target_port                = try(each.value.target_port, 8080)
      transport                  = "http"
      allow_insecure_connections = false

      traffic_weight {
        latest_revision = true
        percentage      = 100
      }
    }
  }
}
