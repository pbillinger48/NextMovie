# The administrator password is generated here and therefore lives in Terraform
# state. That is a deliberate exception to ADR-0013's rule that Terraform never
# sees a secret value: the server cannot be created without one, and the
# alternative — a human-chosen password pasted into a variable — is worse in every
# respect.
#
# It is why the state backend is a restricted storage account rather than a local
# file, and why nothing else in this configuration carries a real secret.
resource "random_password" "postgres" {
  length = 32

  # Flexible Server rejects several punctuation characters in an administrator
  # password, and reports it as a generic provisioning failure.
  special          = true
  override_special = "!#%*-_=+"
}

resource "azurerm_postgresql_flexible_server" "main" {
  name                = "${var.name}-db"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  version = var.postgres_version

  # Burstable, and not a compromise: the catalogue is a thousand films and the
  # user count is one. This tier is already generous for that.
  sku_name   = "B_Standard_B1ms"
  storage_mb = 32768

  administrator_login    = "nextmovie"
  administrator_password = random_password.postgres.result

  # Seven days is the default and is adequate for data that can be rebuilt from a
  # Letterboxd export and TMDb. Longer retention costs more and protects less than
  # it appears to.
  backup_retention_days        = 7
  geo_redundant_backup_enabled = false

  # No high availability. It doubles the cost of the most expensive resource here
  # to protect a single-user application from an outage measured in minutes.
  zone = "1"

  public_network_access_enabled = true

  lifecycle {
    # Rotating this is a deliberate operation, not something a re-apply should do
    # while the application is holding open connections.
    ignore_changes = [administrator_password]
  }
}

resource "azurerm_postgresql_flexible_server_database" "main" {
  name      = "nextmovie"
  server_id = azurerm_postgresql_flexible_server.main.id

  charset   = "UTF8"
  collation = "en_US.utf8"
}

# Postgres is reachable from the public internet and closed to it by default.
# These rules open it to exactly two things.
#
# Deliberately NOT the "allow all Azure services" rule (0.0.0.0), which is a
# common shortcut and admits every other tenant's Azure resources along with
# ours. App Service outbound addresses are stable for the life of a plan, so
# naming them costs nothing and grants far less.
resource "azurerm_postgresql_flexible_server_firewall_rule" "api" {
  for_each = toset(azurerm_linux_web_app.api.outbound_ip_address_list)

  name             = "api-${replace(each.value, ".", "-")}"
  server_id        = azurerm_postgresql_flexible_server.main.id
  start_ip_address = each.value
  end_ip_address   = each.value
}

# The migration bundle runs from a GitHub Actions runner, whose address is not
# knowable in advance. The release pipeline opens a rule for its own address and
# removes it afterwards; this records that nothing is left standing open.
resource "azurerm_postgresql_flexible_server_configuration" "require_ssl" {
  name      = "require_secure_transport"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "ON"
}
