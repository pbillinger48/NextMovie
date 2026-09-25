# One plan per tier, per ADR-0013. They could share a single B1 and halve the
# bill; they do not, so that a runaway request in one tier cannot starve the
# other, and so either can be resized without touching the one beside it.
resource "azurerm_service_plan" "api" {
  name                = "${var.name}-api-plan"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = "B1"
}

resource "azurerm_service_plan" "web" {
  name                = "${var.name}-web-plan"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = "B1"
}

locals {
  # The vault reference syntax App Service resolves at startup. The application
  # reads an ordinary configuration value and never learns where it came from,
  # which is what keeps a laptop and production running identical code.
  secret = {
    for name in keys(merge(local.api_secrets, local.web_secrets)) :
    name => "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=${name})"
  }

  database_connection = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=${azurerm_key_vault_secret.database_connection.name})"
}

resource "azurerm_linux_web_app" "api" {
  name                = "${var.name}-api"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_service_plan.api.location
  service_plan_id     = azurerm_service_plan.api.id

  https_only = true

  identity {
    # System-assigned: the identity lives and dies with the application, so there
    # is no orphaned principal to find a use for later.
    type = "SystemAssigned"
  }

  site_config {
    application_stack {
      dotnet_version = "10.0"
    }

    # The API answers this without touching the database, so it reports whether
    # the process is up rather than whether everything is well.
    health_check_path = "/health"

    ftps_state = "Disabled"

    # Only the web tier calls this today, but mobile is a stated direction and a
    # phone calls the API directly (ADR-0013). CORS is therefore configured for
    # the browser origin that exists rather than left open.
    cors {
      allowed_origins     = ["https://${var.name}-web.azurewebsites.net"]
      support_credentials = false
    }
  }

  app_settings = {
    "ConnectionStrings__NextMovieDb" = local.database_connection
    "Tmdb__ApiReadAccessToken"       = local.secret["tmdb-api-token"]
    "Jwt__SigningKey"                = local.secret["jwt-signing-key"]
    "Jwt__Issuer"                    = "https://${var.name}-api.azurewebsites.net"
    "Jwt__Audience"                  = "https://${var.name}-web.azurewebsites.net"

    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.main.connection_string
    "ASPNETCORE_ENVIRONMENT"                = "Production"

    # Build on the runner, not here. Deploying a built artefact is what makes a
    # release reproducible from a commit.
    "SCM_DO_BUILD_DURING_DEPLOYMENT" = "false"
  }

  lifecycle {
    # Google client IDs are added out of band alongside the OAuth client itself,
    # and a release must not revert them.
    ignore_changes = [app_settings["Google__ClientIds__0"]]
  }
}

resource "azurerm_linux_web_app" "web" {
  name                = "${var.name}-web"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_service_plan.web.location
  service_plan_id     = azurerm_service_plan.web.id

  https_only = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    application_stack {
      node_version = "22-lts"
    }

    # Next.js is started explicitly rather than by Oryx's guesswork, which picks
    # `npm start` from the wrong workspace in a monorepo.
    app_command_line = "node apps/web/server.js"

    ftps_state = "Disabled"
  }

  app_settings = {
    # Server-side only. The browser never calls the API directly (ADR-0004), so
    # this is deliberately not a NEXT_PUBLIC_ value despite its name in the local
    # template — production resolves it inside the Next.js process.
    "API_BASE_URL"            = "https://${var.name}-api.azurewebsites.net"
    "SESSION_COOKIE_PASSWORD" = local.secret["session-cookie-password"]
    "GOOGLE_CLIENT_SECRET"    = local.secret["google-client-secret"]
    "GOOGLE_REDIRECT_URI"     = "https://${var.name}-web.azurewebsites.net/api/auth/google/callback"

    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.main.connection_string
    "NODE_ENV"                              = "production"

    "SCM_DO_BUILD_DURING_DEPLOYMENT" = "false"
  }

  lifecycle {
    ignore_changes = [app_settings["GOOGLE_CLIENT_ID"]]
  }
}
