# Secrets live here and are referenced by the applications rather than copied into
# their settings (ADR-0013). App Settings alone are readable by anyone with
# contributor access to the resource and appear in deployment diffs; a vault puts
# the boundary somewhere a person can be granted or denied.
resource "azurerm_key_vault" "main" {
  name                = "${var.name}-kv"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  # Role assignments rather than access policies. Policies are the older model and
  # cannot express "read secrets but not manage them", which is precisely what the
  # applications need.
  rbac_authorization_enabled = true

  # Seven days is the minimum Azure allows. Purge protection is off deliberately:
  # with it on, a vault cannot be removed for 90 days, and this is a project where
  # tearing the environment down and rebuilding it is a reasonable thing to do.
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
}

# Whoever runs Terraform must be able to create the secret shells below.
resource "azurerm_role_assignment" "operator_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

locals {
  # Created empty. Terraform holds no secret values except the database password
  # it has to generate (see postgres.tf), so these are shells to be filled out of
  # band — a value passed through a variable would land in state.
  api_secrets = {
    "tmdb-api-token"  = "TMDb API read access token"
    "jwt-signing-key" = "HS256 signing key for access tokens"
  }

  web_secrets = {
    "session-cookie-password" = "Encrypts the browser session cookie (ADR-0004)"
    "google-client-secret"    = "Google OAuth client secret"
  }
}

resource "azurerm_key_vault_secret" "placeholder" {
  for_each = merge(local.api_secrets, local.web_secrets)

  name         = each.key
  key_vault_id = azurerm_key_vault.main.id
  content_type = each.value

  # A deliberately invalid placeholder. An empty string is not allowed, and
  # anything that looks like a credential invites someone to assume it works.
  value = "REPLACE-ME"

  depends_on = [azurerm_role_assignment.operator_secrets]

  lifecycle {
    # The whole point. Once a real value is set out of band, Terraform must never
    # propose putting the placeholder back.
    ignore_changes = [value]
  }
}

# The database password is the one secret Terraform does know, so it is written
# here rather than assembled by hand — and the connection string with it, so that
# no human ever needs to see either.
resource "azurerm_key_vault_secret" "database_connection" {
  name         = "database-connection"
  key_vault_id = azurerm_key_vault.main.id
  content_type = "Npgsql connection string"

  value = join(";", [
    "Host=${azurerm_postgresql_flexible_server.main.fqdn}",
    "Database=${azurerm_postgresql_flexible_server_database.main.name}",
    "Username=${azurerm_postgresql_flexible_server.main.administrator_login}",
    "Password=${random_password.postgres.result}",
    "SSL Mode=Require",
  ])

  depends_on = [azurerm_role_assignment.operator_secrets]
}

# Each application reads only what it needs. The web tier has no business holding
# a TMDb token, and the API has no business holding the cookie key.
resource "azurerm_role_assignment" "api_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}

resource "azurerm_role_assignment" "web_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_linux_web_app.web.identity[0].principal_id
}
