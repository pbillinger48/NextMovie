# Infrastructure

Azure, described in Terraform, per
[ADR-0013](../docs/adr/0013-deploy-to-azure-with-terraform.md).

This directory creates the environment. It does **not** deploy the application —
that is the release pipeline, and it comes next.

```
infra/
  bootstrap/    Storage account holding Terraform's own state. Run once.
  *.tf          The environment itself.
```

## What it creates

| Resource | Why | Approx. $/month |
|---|---|---|
| App Service plan × 2 (Linux B1) | One per tier, so neither can starve the other | 26 |
| Linux Web App × 2 | The API and the web tier | — |
| PostgreSQL Flexible Server (B1ms, 32 GB) | The database | 15 |
| Key Vault (standard) | Secrets, referenced rather than copied | <1 |
| Log Analytics + Application Insights | Telemetry, capped at 1 GB/day | 0–3 |
| Consumption budget | Alerts at 80% forecast and 100% actual | 0 |

Roughly **$45/month**. Azure has no hard spending cap, so the budget is an alarm,
not a brake.

## Before you start

You need the Azure CLI and Terraform, and an Azure subscription you are an Owner
on — Owner rather than Contributor, because this assigns roles.

```bash
brew install azure-cli hashicorp/tap/terraform
az login
az account set --subscription "<your subscription>"
```

**Check which PostgreSQL versions your region offers.** Local development runs 18;
`var.postgres_version` defaults to 17 because Flexible Server does not offer every
version everywhere:

```bash
az postgres flexible-server list-supported-versions --location eastus
```

## 1. Create the state backend

Terraform cannot create the place it stores its own state, so this runs first and
keeps its state locally. That local file describes nothing but an empty storage
account, so it does not matter if it is lost.

```bash
cd infra/bootstrap
terraform init
terraform apply
```

## 2. Create the environment

```bash
cd infra
terraform init
terraform apply -var budget_alert_email=you@example.com
```

Ten to fifteen minutes, almost all of it the database.

Note the outputs — `web_url`, `api_url`, `google_redirect_uri` and
`key_vault_name`. You need them below.

## 3. Put the real secrets in

Terraform creates four secrets containing the literal string `REPLACE-ME` and is
configured never to touch their values again. **Nothing works until these are
set**, and none of them should be a value you have used locally.

```bash
VAULT=$(terraform output -raw key_vault_name)

# The TMDb token you already have — the only one carried over from development.
az keyvault secret set --vault-name "$VAULT" --name tmdb-api-token \
  --value "$(dotnet user-secrets list --project ../apps/api/NextMovie.Api \
             | sed -n 's/^Tmdb:ApiReadAccessToken = //p')"

az keyvault secret set --vault-name "$VAULT" --name jwt-signing-key \
  --value "$(openssl rand -base64 48)"

az keyvault secret set --vault-name "$VAULT" --name session-cookie-password \
  --value "$(openssl rand -base64 32)"
```

The database connection string is written by Terraform, which generates the
password and stores it without it ever being displayed.

## 4. Google sign-in

Everything else works without this; the Google button does not.

At [Google Cloud Console → Credentials](https://console.cloud.google.com/apis/credentials),
on your Web application OAuth client, add the **authorised redirect URI** that
`terraform output google_redirect_uri` prints. Exactly — a trailing slash is a
different URI, and the failure appears on Google's error page rather than ours.

Then the client ID goes to both tiers and the secret to the web tier. The ID is
public; the secret is not.

```bash
az keyvault secret set --vault-name "$VAULT" --name google-client-secret --value "<secret>"

# The web tier obtains the token; the API decides whose tokens it trusts
# (ADR-0005). Both need the ID, so both are set, and Terraform ignores changes to
# these two settings so a later apply cannot revert them.
az webapp config appsettings set -g nextmovie-production -n nextmovie-web \
  --settings GOOGLE_CLIENT_ID="<client id>"

az webapp config appsettings set -g nextmovie-production -n nextmovie-api \
  --settings Google__ClientIds__0="<client id>"
```

## What is deliberately absent

**No staging environment.** The safety net is CI and branch protection (ADR-0013).
Proportionate for one user; not proportionate the moment somebody else depends on
this being up.

**No high availability**, and B1 is a single instance — a deploy is a brief
interruption.

**No "allow all Azure services" firewall rule.** That common shortcut admits every
other tenant's resources. The API's outbound addresses are named individually
instead, which costs nothing and grants far less.

## Tearing it down

```bash
cd infra && terraform destroy
cd bootstrap && terraform destroy
```

The Key Vault soft-deletes and holds its name for seven days. Purge protection is
off so it can be purged sooner if the name is needed:

```bash
az keyvault purge --name nextmovie-kv
```
