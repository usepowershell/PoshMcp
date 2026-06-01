variable "name_prefix" {
  description = "Short prefix used in generated Azure resource names."
  type        = string
}

variable "environment_name" {
  description = "Environment label included in names and tags."
  type        = string
}

variable "location" {
  description = "Azure region for the Container Apps environment."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group name for Container Apps resources."
  type        = string
}

variable "container_app_environment_name" {
  description = "Optional explicit Container Apps managed environment name."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace resource ID."
  type        = string
}

variable "managed_identity_id" {
  description = "User-assigned managed identity resource ID."
  type        = string
}

variable "managed_identity_client_id" {
  description = "User-assigned managed identity client ID."
  type        = string
}

variable "container_registry_server" {
  description = "Registry server used by Container Apps. Empty for public image scenarios."
  type        = string
}

variable "container_registry_username" {
  description = "Optional registry username."
  type        = string
}

variable "container_registry_password" {
  description = "Optional registry password."
  type        = string
  sensitive   = true
}

variable "use_managed_identity_registry_auth" {
  description = "Use the Container App managed identity for registry pulls."
  type        = bool
}

variable "default_container_image" {
  description = "Default full image reference."
  type        = string
}

variable "container_image_repository" {
  description = "Default repository name used when a scenario supplies only image_tag."
  type        = string
}

variable "application_insights_enabled" {
  description = "Inject Application Insights connection string when true."
  type        = bool
}

variable "application_insights_connection" {
  description = "Application Insights connection string."
  type        = string
  sensitive   = true
}

variable "enable_storage_mounts" {
  description = "Enable the environment storage definition for optional scenario mounts."
  type        = bool
}

variable "storage_account_name" {
  description = "Storage account name for Azure Files mount."
  type        = string
}

variable "storage_account_access_key" {
  description = "Storage account access key for Azure Files mount."
  type        = string
  sensitive   = true
}

variable "storage_share_name" {
  description = "Azure Files share name for optional scenario mounts."
  type        = string
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
}

variable "container_app_scenario_secrets" {
  description = "Sensitive per-scenario Container App secrets keyed by scenario name, then ACA secret name."
  type        = map(map(string))
  default     = {}
  sensitive   = true
}

variable "default_min_replicas" {
  description = "Default minimum replicas."
  type        = number
}

variable "default_max_replicas" {
  description = "Default maximum replicas."
  type        = number
}

variable "default_cpu" {
  description = "Default CPU cores."
  type        = number
}

variable "default_memory" {
  description = "Default memory."
  type        = string
}

variable "default_external_ingress_enabled" {
  description = "Default external ingress setting for HTTP scenarios."
  type        = bool
}

variable "tags" {
  description = "Tags applied to resources."
  type        = map(string)
}
