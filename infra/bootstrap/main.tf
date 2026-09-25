# Terraform cannot create the place it stores its own state, so this creates it
# and keeps its own state locally — the one local state file in the project, and
# harmless because it describes nothing but an empty storage account.
#
# Run once, before `terraform init` in the parent directory:
#
#     cd infra/bootstrap && terraform init && terraform apply
#
# It is separate rather than a chicken-and-egg workaround in the main
# configuration because the alternatives all involve running Terraform twice with
# different backends, which is the kind of step somebody gets wrong at 1am.

terraform {
  required_version = "~> 1.13"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.40"
    }
  }
}

provider "azurerm" {
  features {}
}

variable "location" {
  description = "Must match the location used by the main configuration."
  type        = string
  default     = "eastus"
}

resource "azurerm_resource_group" "state" {
  name     = "nextmovie-tfstate"
  location = var.location
}

resource "azurerm_storage_account" "state" {
  name                = "nextmovietfstate"
  resource_group_name = azurerm_resource_group.state.name
  location            = azurerm_resource_group.state.location

  account_tier             = "Standard"
  account_replication_type = "LRS"

  # State records the database administrator password. Everything below treats
  # this account as the most sensitive thing in the subscription.
  https_traffic_only_enabled      = true
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false

  blob_properties {
    # Recovers a state file overwritten by a failed apply, which is the realistic
    # disaster here — far more likely than the account being lost.
    versioning_enabled = true

    delete_retention_policy {
      days = 30
    }
  }
}

resource "azurerm_storage_container" "state" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.state.id
  container_access_type = "private"
}

data "azurerm_client_config" "current" {}

# Shared keys are disabled above, so access is by identity. This grants the
# operator running Terraform; the CI pipeline's federated identity is granted the
# same role separately.
resource "azurerm_role_assignment" "operator" {
  scope                = azurerm_storage_account.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

output "next_step" {
  value = "Backend ready. Now: cd .. && terraform init"
}
