terraform {
  required_version = "~> 1.13"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.40"
    }

    random = {
      source  = "hashicorp/random"
      version = "~> 3.7"
    }
  }

  # State lives in Azure Storage, not on a laptop. It records the database
  # administrator password (see postgres.tf), so the container holding it is the
  # most sensitive thing this configuration creates.
  #
  # Created by infra/bootstrap before this configuration can be initialised —
  # Terraform cannot create the place it stores its own state.
  backend "azurerm" {
    resource_group_name  = "nextmovie-tfstate"
    storage_account_name = "nextmovietfstate"
    container_name       = "tfstate"
    key                  = "production.tfstate"
    use_azuread_auth     = true
  }
}

provider "azurerm" {
  features {
    key_vault {
      # A soft-deleted vault blocks its own name for 90 days, which turns one
      # mistaken destroy into three months of choosing a new name.
      purge_soft_delete_on_destroy = false
    }
  }
}
