output "managed_identity_id" {
  value = azurerm_user_assigned_identity.this.id
}

output "managed_identity_name" {
  value = azurerm_user_assigned_identity.this.name
}

output "managed_identity_client_id" {
  value = azurerm_user_assigned_identity.this.client_id
}

output "managed_identity_principal_id" {
  value = azurerm_user_assigned_identity.this.principal_id
}
