# A deployed application with no telemetry is a black box, and the first
# production incident is the wrong moment to discover that (ADR-0013).
resource "azurerm_log_analytics_workspace" "main" {
  name                = "${var.name}-logs"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  sku = "PerGB2018"

  # Thirty days. Long enough to investigate something noticed a fortnight late,
  # short enough that retention never becomes the reason the bill grew.
  retention_in_days = 30
}

resource "azurerm_application_insights" "main" {
  name                = "${var.name}-insights"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"

  # The free grant is 5 GB a month. Two applications with one user will not
  # approach it, and this ensures a runaway loop cannot quietly change that.
  daily_data_cap_in_gb = 1
}
