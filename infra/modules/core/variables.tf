variable "name_prefix" {
  description = "Short prefix used in generated Azure resource names."
  type        = string
}

variable "environment_name" {
  description = "Environment label included in names and tags."
  type        = string
}

variable "location" {
  description = "Azure region for created resources."
  type        = string
}

variable "create_resource_group" {
  description = "Create the resource group when true; look up an existing resource group when false."
  type        = bool
}

variable "resource_group_name" {
  description = "Resource group name to create or reuse."
  type        = string
}

variable "create_container_registry" {
  description = "Create an Azure Container Registry."
  type        = bool
}

variable "container_registry_name" {
  description = "Optional explicit ACR name."
  type        = string
}

variable "log_analytics_retention_days" {
  description = "Log Analytics retention in days."
  type        = number
}

variable "application_insights_enabled" {
  description = "Create Application Insights when true."
  type        = bool
}

variable "enable_storage_mounts" {
  description = "Create Azure Files storage for optional scenario mounts."
  type        = bool
}

variable "storage_share_name" {
  description = "Azure Files share name."
  type        = string
}

variable "tags" {
  description = "Tags applied to resources."
  type        = map(string)
}
