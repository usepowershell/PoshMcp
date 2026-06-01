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

variable "resource_group_name" {
  description = "Resource group name for the managed identity."
  type        = string
}

variable "acr_id" {
  description = "ACR resource ID for AcrPull assignment."
  type        = string
  default     = null
}

variable "enable_acr_pull" {
  description = "Assign AcrPull to the managed identity when true."
  type        = bool
}

variable "tags" {
  description = "Tags applied to resources."
  type        = map(string)
}
