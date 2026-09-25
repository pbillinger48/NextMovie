# Everything in one resource group, because everything here shares a lifecycle:
# if this application goes away, all of it goes away together.
resource "azurerm_resource_group" "main" {
  name     = "${var.name}-production"
  location = var.location
}

data "azurerm_client_config" "current" {}

# Azure has no hard spending cap, so this is an alarm rather than a brake. It
# exists because the usual way a side project becomes expensive is a resource
# nobody remembers creating, discovered on a statement a month later.
resource "azurerm_consumption_budget_resource_group" "main" {
  name              = "${var.name}-budget"
  resource_group_id = azurerm_resource_group.main.id

  amount     = var.monthly_budget
  time_grain = "Monthly"

  time_period {
    # Azure requires a start date on the first of a month, at or after the
    # current one.
    start_date = formatdate("YYYY-MM-01'T'00:00:00Z", timeadd(timestamp(), "720h"))
  }

  # Two thresholds, and the first is a forecast rather than a fact: being told
  # halfway through the month that the trend ends above budget is actionable,
  # whereas being told on the last day is history.
  notification {
    enabled        = true
    threshold      = 80
    operator       = "GreaterThan"
    threshold_type = "Forecasted"
    contact_emails = [var.budget_alert_email]
  }

  notification {
    enabled        = true
    threshold      = 100
    operator       = "GreaterThan"
    contact_emails = [var.budget_alert_email]
  }

  lifecycle {
    # start_date is computed from the current time, so every plan would otherwise
    # propose replacing the budget with an identical one.
    ignore_changes = [time_period]
  }
}
