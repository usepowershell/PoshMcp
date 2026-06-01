output "container_app_environment_id" {
  value = azurerm_container_app_environment.this.id
}

output "container_app_environment_name" {
  value = azurerm_container_app_environment.this.name
}

output "scenario_container_apps" {
  value = {
    for scenario_key, app in azurerm_container_app.scenario :
    scenario_key => {
      name = app.name
      id   = app.id
    }
  }
}

output "scenario_fqdns" {
  value = {
    for scenario_key, app in azurerm_container_app.scenario :
    scenario_key => try(app.ingress[0].fqdn, null)
  }
}

output "scenario_urls" {
  value = {
    for scenario_key, app in azurerm_container_app.scenario :
    scenario_key => try("https://${app.ingress[0].fqdn}", null)
  }
}

output "scenario_health_urls" {
  value = {
    for scenario_key, app in azurerm_container_app.scenario :
    scenario_key => try("https://${app.ingress[0].fqdn}/health", null)
  }
}
