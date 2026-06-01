# PoshMcp Azure Container Apps Scenario Terraform

This Terraform deployment provisions an Azure Container Apps environment that can run multiple PoshMcp server instances side by side for scenario testing. Each scenario is driven by `container_app_scenarios`, so adding a new test instance should usually mean editing variables rather than adding new Terraform resources.

## What It Creates

- Resource group, or reuse of an existing resource group.
- Log Analytics workspace for Container Apps environment logs.
- Application Insights, enabled by default and injected into each app as a secret-backed environment variable.
- Azure Container Apps managed environment.
- User-assigned managed identity attached to every Container App.
- Optional Azure Container Registry with `AcrPull` assigned to the managed identity.
- Optional Azure Files share for scenarios that need mounted config or test data.
- One `azurerm_container_app` per enabled scenario.

## Image Prerequisites

Terraform does not build or push the PoshMcp image. Build and push an image before applying the deployment.

For a Terraform-created ACR:

```powershell
az acr login --name <acr-name>
podman build -t <acr-login-server>/poshmcp:latest ..
podman push <acr-login-server>/poshmcp:latest
```

For an existing registry or public image, set `create_container_registry = false` and provide `container_image`, or set per-scenario `image` values.

The image must listen on port `8080`. HTTP scenarios set these defaults unless you override them:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://+:8080`
- `POSHMCP_TRANSPORT=http`

## Usage

```powershell
cd infra
terraform init
terraform plan -out poshmcp-scenarios.tfplan
terraform apply poshmcp-scenarios.tfplan
```

For preparation-only validation without touching Azure state:

```powershell
terraform fmt -recursive
terraform init -backend=false
terraform validate
```

Do not run `terraform apply` until the deployment has passed the Azure validation workflow and the image exists in the configured registry.

## Required Variables

Copy `terraform.tfvars.example` to `terraform.tfvars` and set at least:

- `subscription_id`
- `tenant_id`
- `location`
- image settings (`container_image`, or ACR plus `container_image_repository` and `container_image_tag`)

`terraform.tfvars` is ignored by Git so local subscription IDs and secrets stay out of the repo.

## Scenario Configuration

Scenarios are keyed by name:

```hcl
container_app_scenarios = {
  basic = {
    name_suffix    = "basic"
    transport_mode = "http"
    env = {
      POSHMCP_LOGGING__LOGLEVEL__DEFAULT = "Information"
    }
  }
}
```

Each scenario can set:

- `enabled`
- `name_suffix`
- `image` or `image_tag`
- `transport_mode` (`http` by default; `stdio` disables ingress unless explicitly overridden)
- `env`
- `secret_env`
- `cpu`
- `memory`
- `min_replicas`
- `max_replicas`
- `ingress_enabled`
- `external_ingress`
- `target_port`
- `health_path`
- `readiness_path`
- `mount_storage`
- `command`
- `args`

Default scenarios are `basic`, `advanced`, `tenant`, and `observability`.

## Secret Handling

Keep secret values in `container_app_scenario_secrets` and reference them from `secret_env`:

```hcl
container_app_scenario_secrets = {
  auth = {
    entra-client-secret = "use-a-secure-local-value"
  }
}

container_app_scenarios = {
  auth = {
    name_suffix = "auth"
    secret_env = {
      POSHMCP_AUTHENTICATION__CLIENTSECRET = "entra-client-secret"
    }
  }
}
```

Prefer managed identity and Azure-hosted secret injection workflows for production-like testing. Do not commit `terraform.tfvars` or any file containing real secrets.

## Health and Observability

HTTP scenarios get probes by default:

- Startup probe: `/health`
- Readiness probe: `/health/ready`
- Liveness probe: `/health/ready`

Application Insights is enabled by default. When enabled, the connection string is stored as a Container Apps secret and exposed to the app through `APPLICATIONINSIGHTS_CONNECTION_STRING`.

## Outputs

Useful outputs include:

- `scenario_urls`
- `scenario_health_urls`
- `scenario_fqdns`
- `resource_group_name`
- `container_app_environment_id`
- `log_analytics_workspace_id`
- `application_insights_name`
- `managed_identity_client_id`
- `acr_login_server`
