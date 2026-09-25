variable "location" {
  description = "Azure region. Painful to change once data exists in it."
  type        = string
  default     = "eastus"
}

variable "name" {
  description = "Prefix for every resource name. Lowercase letters and digits only — some Azure resource types accept nothing else."
  type        = string
  default     = "nextmovie"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,11}$", var.name))
    error_message = "The name must be 3-12 characters, lowercase letters and digits, starting with a letter."
  }
}

variable "monthly_budget" {
  description = "Spend in USD at which alerts are raised. Not a cap — Azure has no hard cap — so treat the alert as the control."
  type        = number
  default     = 60
}

variable "budget_alert_email" {
  description = "Where budget alerts are sent. The only variable with no sensible default."
  type        = string
}

variable "postgres_version" {
  description = <<-EOT
    PostgreSQL major version.

    Local development runs 18 (docker-compose.yml). Flexible Server may not offer
    18 in every region, so this is deliberately a variable and deliberately not
    assumed to match. Check before applying:

        az postgres flexible-server list-supported-versions --location <region>

    A one-major skew is safe for this application — EF Core and Npgsql target the
    protocol, not the server build — but it should be a known skew rather than a
    surprise.
  EOT
  type        = string
  default     = "17"
}
