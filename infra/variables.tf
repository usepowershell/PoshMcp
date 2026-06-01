variable "subscription_id" {
  description = "Azure subscription ID. Leave null to use the active Azure CLI or environment context."
  type        = string
  default     = null
}

variable "tenant_id" {
  description = "Azure tenant ID. Leave null to use the active Azure CLI or environment context."
  type        = string
  default     = null
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "eastus"
}

variable "name_prefix" {
  description = "Short prefix used in generated Azure resource names."
  type        = string
  default     = "poshmcp"

  validation {
    condition     = can(regex("^[a-zA-Z0-9-]{3,20}$", var.name_prefix))
    error_message = "name_prefix must be 3-20 characters and contain only letters, numbers, and hyphens."
  }
}

variable "environment_name" {
  description = "Environment label included in names and tags."
  type        = string
  default     = "scenario"
}

variable "create_resource_group" {
  description = "When true, Terraform creates the resource group. When false, resource_group_name must name an existing resource group."
  type        = bool
  default     = true
}

variable "resource_group_name" {
  description = "Resource group name to create or reuse. Leave empty to derive a name from name_prefix and environment_name."
  type        = string
  default     = ""
}

variable "enable_resource_provider_registration" {
  description = "Register required Azure resource providers from Terraform. Disable if your identity lacks registration permissions."
  type        = bool
  default     = false
}

variable "create_container_registry" {
  description = "Create an Azure Container Registry for scenario images. Push the image before applying Container Apps."
  type        = bool
  default     = true
}

variable "container_registry_name" {
  description = "Optional ACR name when create_container_registry is true. Must be globally unique and alphanumeric."
  type        = string
  default     = ""
}

variable "container_registry_server" {
  description = "Existing registry server, such as myregistry.azurecr.io. Used when create_container_registry is false."
  type        = string
  default     = ""
}

variable "container_registry_username" {
  description = "Optional registry username for non-managed-identity registry auth. Prefer managed identity for ACR."
  type        = string
  default     = ""
}

variable "container_registry_password" {
  description = "Optional registry password for non-managed-identity registry auth. Prefer managed identity for ACR."
  type        = string
  default     = ""
  sensitive   = true
}

variable "container_image" {
  description = "Full default image reference. Leave empty to use the Terraform-created ACR login server plus container_image_repository:container_image_tag."
  type        = string
  default     = ""
}

variable "container_image_repository" {
  description = "Repository name used when container_image is empty."
  type        = string
  default     = "poshmcp"
}

variable "container_image_tag" {
  description = "Default image tag used when container_image is empty or a scenario supplies only image_tag."
  type        = string
  default     = "latest"
}

variable "container_app_environment_name" {
  description = "Optional explicit Container Apps managed environment name. Leave empty to derive one."
  type        = string
  default     = ""
}

variable "application_insights_enabled" {
  description = "Create Application Insights and inject its connection string into scenarios."
  type        = bool
  default     = true
}

variable "log_analytics_retention_days" {
  description = "Log Analytics retention in days."
  type        = number
  default     = 30
}

variable "enable_storage_mounts" {
  description = "Create an Azure Files share and allow scenarios to mount it."
  type        = bool
  default     = false
}

variable "storage_share_name" {
  description = "Azure Files share name used when storage mounts are enabled."
  type        = string
  default     = "poshmcp-config"
}

variable "default_min_replicas" {
  description = "Default minimum replicas for scenarios."
  type        = number
  default     = 0
}

variable "default_max_replicas" {
  description = "Default maximum replicas for scenarios."
  type        = number
  default     = 1
}

variable "default_cpu" {
  description = "Default CPU cores per Container App replica."
  type        = number
  default     = 0.5
}

variable "default_memory" {
  description = "Default memory per Container App replica."
  type        = string
  default     = "1.0Gi"
}

variable "external_ingress_enabled" {
  description = "Default external ingress setting for HTTP scenarios."
  type        = bool
  default     = true
}

variable "tags" {
  description = "Tags applied to all Azure resources."
  type        = map(string)
  default = {
    application = "PoshMcp"
    owner       = "platform-engineering"
  }
}

variable "container_app_scenarios" {
  description = "Scenario-specific Container App definitions keyed by scenario name."
  type = map(object({
    enabled          = optional(bool, true)
    name_suffix      = optional(string)
    image            = optional(string)
    image_tag        = optional(string)
    transport_mode   = optional(string, "http")
    env              = optional(map(string), {})
    secret_env       = optional(map(string), {})
    cpu              = optional(number)
    memory           = optional(string)
    min_replicas     = optional(number)
    max_replicas     = optional(number)
    ingress_enabled  = optional(bool)
    external_ingress = optional(bool)
    target_port      = optional(number, 8080)
    health_path      = optional(string, "/health")
    readiness_path   = optional(string, "/health/ready")
    mount_storage    = optional(bool, false)
    command          = optional(list(string))
    args             = optional(list(string))
  }))
  default = {
    basic = {
      name_suffix    = "basic"
      transport_mode = "http"
      env = {
        POSHMCP_LOGGING__LOGLEVEL__DEFAULT = "Information"
      }
    }
    advanced = {
      name_suffix    = "advanced"
      transport_mode = "http"
      cpu            = 1
      memory         = "2.0Gi"
      env = {
        POSHMCP_POWERSHELLCONFIGURATION__SUBPROCESSHOSTMODE = "ProcessPool"
        POSHMCP_POWERSHELLCONFIGURATION__PROCESSPOOLSIZE    = "2"
        POSHMCP_POWERSHELLCONFIGURATION__COMMANDTIMEOUT     = "00:02:00"
      }
    }
    tenant = {
      name_suffix    = "tenant"
      transport_mode = "http"
      env = {
        POSHMCP_AUTHENTICATION__ENABLED      = "false"
        POSHMCP_TENANTCONFIGURATION__ENABLED = "true"
      }
    }
    observability = {
      name_suffix    = "observability"
      transport_mode = "http"
      env = {
        POSHMCP_APPLICATIONINSIGHTS__ENABLED = "true"
        POSHMCP_TELEMETRY__METRICS__ENABLED  = "true"
      }
    }
  }
}

variable "container_app_scenario_secrets" {
  description = "Sensitive per-scenario Container App secrets keyed by scenario name, then ACA secret name. Reference them from container_app_scenarios[*].secret_env."
  type        = map(map(string))
  default     = {}
  sensitive   = true
}
