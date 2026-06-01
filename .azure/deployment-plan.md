# Azure Deployment Plan: PoshMcp Scenario Container Apps

**Status:** Ready for Validation
**Current datetime:** 2026-06-01T00:00:00Z
**Prepared by:** Amy
**Deployment recipe:** Terraform for Azure Container Apps
**Planned infrastructure folder:** `infra/`

## Purpose

Prepare Terraform infrastructure for hosting multiple PoshMcp HTTP server instances on Azure Container Apps so different runtime, authentication, module, and configuration scenarios can be tested side by side.

This plan was approved by Steven and Terraform preparation artifacts have been generated under `infra/`.

## Current Repo / Workload Analysis

- PoshMcp is a .NET 10 MCP server with container support and a single runtime entrypoint through `docker-entrypoint.sh`.
- The repository Dockerfile publishes `PoshMcp.Server`, installs PowerShell, exposes port `8080`, runs as non-root user `appuser`, and defaults to:
	- `ASPNETCORE_URLS=http://+:8080`
	- `ASPNETCORE_ENVIRONMENT=Production`
	- `POSHMCP_TRANSPORT=http`
- The service already exposes health endpoints suitable for Container Apps probes:
	- `/health`
	- `/health/ready`
- PoshMcp supports Application Insights configuration through `ApplicationInsights` settings and `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- Scenario configuration can be supplied through environment variables and/or mounted configuration files. Existing examples cover basic, advanced, tenant, module, environment, and Azure-oriented configurations.
- The repo has Azure deployment integration tests that exercise image build, Azure image customization, ACR push, Container Apps deployment, and health validation, but there is no existing Terraform infrastructure folder in this repo.

## Reference Terraform Observations

Reviewed reference folder: `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami-mcp_testing\infra\terraform\deploy`.

Reusable patterns observed:

- Terraform root uses `azurerm ~> 4.0`, `azuread ~> 3.0`, `random ~> 3.0`, and `kreuzwerker/docker ~> 3.0` providers.
- Naming is centralized with the `Azure/naming/azurerm` module and a project suffix.
- Root module delegates to clear child modules:
	- `core`: resource group, Azure Container Registry, storage account/share, Log Analytics, Application Insights.
	- `identity`: Entra app registration, service principal, user-assigned managed identity, ACR Pull, Graph permissions, and app roles.
	- `app`: Docker build/push, Container Apps Environment, Azure Files environment storage, and Container App.
	- `grafana`: optional managed Grafana when an admin principal is supplied.
- The app module configures ACA with:
	- single revision mode
	- external HTTPS ingress on port `8080`
	- user-assigned managed identity
	- registry access via managed identity
	- App Insights connection string as an ACA secret
	- Azure Files mount
	- `/health`, `/health/ready` startup/liveness/readiness probes
	- dynamic environment variables via a map
- The reference includes Docker image build/push inside Terraform. That is useful for all-in-one test deployments but should be treated as optional in this repo so CI/CD can also pass a prebuilt image.

Intentional differences for PoshMcp scenario testing:

- Prefer a reusable multi-instance Container Apps module rather than a single hard-coded `azurerm_container_app`.
- Make Entra app registration/auth optional per scenario. Some tests need unauthenticated MCP HTTP behavior, while others need OAuth-protected resource behavior.
- Use per-scenario appsettings/config strategy instead of mutating a single root `appsettings.json` during Terraform apply.
- Avoid broad Microsoft Graph application permissions by default. Only add elevated identity/app-registration flows behind explicit variables.

## Proposed Azure Architecture

Terraform under `infra/` will provision:

- One resource group for the scenario test environment.
- One Azure Container Registry, unless an existing image/registry is supplied.
- One Log Analytics workspace.
- One workspace-linked Application Insights resource.
- One Azure Container Apps Environment shared by all scenario instances.
- Optional Azure Files storage for scenarios that need mounted config, scripts, modules, or persistent test data.
- One user-assigned managed identity for Container Apps image pull and optional Azure access.
- Multiple Azure Container Apps, one per scenario definition.

Each Container App will run the PoshMcp image in HTTP mode on port `8080`, with health probes wired to the existing endpoints. External ingress will be enabled by default for test reachability, with an option to disable ingress for private-only scenarios later.

## Terraform Layout Under `infra/`

After approval, generate this layout:

```text
infra/
	README.md
	main.tf
	variables.tf
	outputs.tf
	providers.tf
	versions.tf
	terraform.tfvars.example
	modules/
		core/
			main.tf
			variables.tf
			outputs.tf
		identity/
			main.tf
			variables.tf
			outputs.tf
		scenario-app/
			main.tf
			variables.tf
			outputs.tf
```

Planned module responsibilities:

- `core`: resource group, ACR, Log Analytics, Application Insights, optional storage account/share.
- `identity`: user-assigned managed identity and ACR Pull role assignment. Optional Entra app registration may be included only when scenario auth config requires it.
- `scenario-app`: one `azurerm_container_app` per scenario using `for_each`, dynamic environment variables, optional secrets, optional Azure Files volume mounts, probes, ingress, and scale settings.

## Container App Instance Strategy

Use a variable-driven scenario map so adding a new test instance does not require a new Terraform file.

Proposed default scenarios after approval:

| Scenario key | Purpose | Key configuration |
| --- | --- | --- |
| `basic` | Minimal HTTP MCP server smoke test | `POSHMCP_TRANSPORT=http`, no auth, default command exposure |
| `advanced` | Module/runtime behavior | Out-of-process pool, selected modules/imports, result caching enabled |
| `azure` | Azure/managed identity behavior | Azure-oriented appsettings, `AZURE_CLIENT_ID`, App Insights enabled |
| `auth` | OAuth protected-resource testing | Auth enabled, Entra authority/audience/resource settings supplied |

Scenario map shape to generate:

```hcl
container_app_scenarios = {
	basic = {
		enabled      = true
		display_name = "basic"
		image        = null
		min_replicas = 0
		max_replicas = 1
		cpu          = 0.5
		memory       = "1.0Gi"
		env          = {}
		secrets      = {}
		config_file  = null
		external_ingress = true
	}
}
```

Each app name should derive from the base name and scenario key, for example `ca-poshmcp-basic`, `ca-poshmcp-advanced`, `ca-poshmcp-azure`, and `ca-poshmcp-auth`, subject to Azure naming limits.

## Image and Configuration Assumptions

- Default image source should be configurable. Terraform can either:
	- deploy an existing image reference, such as `ghcr.io/usepowershell/poshmcp/poshmcp:latest`, or
	- build and push from the local repo to ACR when explicitly enabled.
- The first implementation should support prebuilt image deployment first. Docker build/push inside Terraform can be added as an optional flag because it couples Terraform apply to local Docker availability.
- Container image must listen on port `8080` and run with `POSHMCP_TRANSPORT=http`.
- App configuration should be scenario-specific. Prefer ACA environment variables for simple overrides and optional mounted config files for larger appsettings profiles.
- App Insights should be enabled for scenarios by injecting the Application Insights connection string as a secret-backed environment variable.
- If mounted config is used, use `POSHMCP_CONFIGURATION` or the app's supported config path conventions rather than overwriting repository files during Terraform apply.

## Security and Observability Decisions

- Use user-assigned managed identity for Container Apps.
- Grant only `AcrPull` on the ACR by default.
- Do not grant subscription-wide `Contributor`, `Owner`, or broad Microsoft Graph application permissions by default.
- Store Application Insights connection string and other sensitive values as Container Apps secrets.
- Keep registry admin credentials disabled if the chosen ACR/image flow can use managed identity. If Docker-provider push is later enabled and requires admin credentials, call that out explicitly in the generated README.
- Use HTTPS-only external ingress for public test endpoints.
- Use `/health` for startup probes and `/health/ready` for readiness/liveness probes.
- Send container logs to Log Analytics through the shared ACA Environment.
- Send application telemetry to Application Insights when enabled.
- Tag all resources with workload, environment, owner, and scenario metadata.

## Variables To Generate After Approval

Root variables to generate:

- `subscription_id`
- `tenant_id`
- `location`
- `resource_group_name`
- `name_prefix`
- `tags`
- `container_image`
- `create_container_registry`
- `container_registry_name`
- `container_registry_server`
- `enable_terraform_docker_build`
- `docker_build_context`
- `dockerfile_path`
- `application_insights_enabled`
- `enable_storage_mounts`
- `storage_share_name`
- `container_app_scenarios`
- `default_min_replicas`
- `default_max_replicas`
- `default_cpu`
- `default_memory`
- `external_ingress_enabled`
- optional auth variables: `auth_tenant_id`, `auth_audience`, `auth_resource`, `auth_client_id`, `auth_scopes`

## Outputs To Generate After Approval

Root outputs to generate:

- `resource_group_name`
- `container_app_environment_id`
- `log_analytics_workspace_id`
- `application_insights_name`
- `application_insights_connection_string` (sensitive)
- `managed_identity_client_id`
- `managed_identity_principal_id`
- `acr_login_server`
- `scenario_container_apps`
- `scenario_fqdns`
- `scenario_urls`
- `scenario_health_urls`
- optional `storage_account_name`
- optional `storage_share_name`

## Generated Artifacts

Generated Terraform deployment files under `infra/`:

- `README.md`
- `.gitignore`
- `.terraform.lock.hcl`
- `versions.tf`
- `providers.tf`
- `main.tf`
- `variables.tf`
- `outputs.tf`
- `terraform.tfvars.example`
- `modules/core/*`
- `modules/identity/*`
- `modules/scenario-app/*`

Preparation validation completed:

- `terraform fmt -recursive`
- `terraform init -backend=false`
- `terraform validate`

## Validation Steps

Preparation validation after Terraform files are generated:

1. Run `terraform fmt -recursive` in `infra/`.
2. Run `terraform init -backend=false` in `infra/`.
3. Run `terraform validate` in `infra/`.
4. Run `terraform plan` with a populated `terraform.tfvars` only after Steven confirms Azure subscription, tenant, and location.
5. Verify planned Container Apps include expected scenario count and names.
6. After deployment is eventually handled by the Azure deployment workflow, validate each scenario:
	 - `GET /health`
	 - `GET /health/ready`
	 - MCP `initialize` against the scenario root endpoint where auth allows
	 - Application Insights request/metric flow when enabled

This task will not run `terraform apply`, `azd up`, `azd deploy`, or Azure deployment commands.

## Approval Gate

**Status:** Ready for Validation

Approval question for Steven:

> Approve this plan to generate Terraform files under `infra/` for the Azure Container Apps multi-scenario PoshMcp test environment?
