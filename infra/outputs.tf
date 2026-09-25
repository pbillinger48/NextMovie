output "api_url" {
  description = "Where the API answers. Also the JWT issuer."
  value       = "https://${azurerm_linux_web_app.api.default_hostname}"
}

output "web_url" {
  description = "The application."
  value       = "https://${azurerm_linux_web_app.web.default_hostname}"
}

output "google_redirect_uri" {
  description = "Register this exact value as an authorised redirect URI on the Google OAuth client, or sign-in fails on Google's own error page rather than ours."
  value       = "https://${azurerm_linux_web_app.web.default_hostname}/api/auth/google/callback"
}

output "key_vault_name" {
  description = "Where to write the real secret values. See infra/README.md."
  value       = azurerm_key_vault.main.name
}

output "postgres_fqdn" {
  description = "Database host, for the migration pipeline."
  value       = azurerm_postgresql_flexible_server.main.fqdn
}

output "api_outbound_ips" {
  description = "Addresses the API reaches the database from. Already allowed through its firewall; listed for diagnosing a connection that is refused."
  value       = azurerm_linux_web_app.api.outbound_ip_address_list
}
