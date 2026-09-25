# ADR-0013: Deploy to Azure App Service, described in Terraform

- **Status:** Accepted
- **Date:** 2026-09-25

## Context

Fifty-five merged pull requests, 493 API tests, twelve accepted decisions — and
the application has never run anywhere but a laptop. Every operational question is
therefore still unanswered: how schema changes reach a live database, where
secrets live when there is no `dotnet user-secrets`, what happens on a cold start,
what the thing costs.

Those are not questions that can be reasoned out in advance. They are answered by
deploying, and until then the project is a very well-tested program rather than a
running system.

There is a second reason, and it is the one that decides the ordering. The
recommendation engine now has three metrics and a sourcing trace, and it can be
improved indefinitely — but the learned model this project is heading toward needs
interaction data, and `recommendation_events` holds twelve rows because twelve is
how many times anybody has opened the page. **Only a deployed application produces
a training set.**

## Decision

**Azure App Service for both tiers, Azure Database for PostgreSQL Flexible Server,
described in Terraform and deployed by GitHub Actions using federated credentials.**

### Why App Service

Two Linux App Service plans on the Burstable B1 tier, one per tier.

Rejected alternatives, in order of how seriously they were considered. **Container
Apps** scale to zero and would cost roughly half, at the price of a cold start on
the first request after an idle period — which is most requests, for a portfolio
project. **AKS** is the answer to a problem this does not have; a Kubernetes
cluster for two containers is a demonstration of enthusiasm rather than judgement.
**A single VM** is cheapest and teaches the most about operations, and is rejected
because patching, backups and TLS renewal become a standing obligation with no
relationship to the product.

The Next.js tier needs a Node runtime rather than static hosting: server
components, Server Actions and `proxy.ts` all execute per request, and ADR-0004
makes the proxy the only place a session cookie can be written. Azure Static Web
Apps would break the session model outright.

### Why Flexible Server

Burstable B1ms with 32 GB storage. The catalogue is a thousand films and one
user; the smallest tier is not a compromise, it is over-provisioned.

Automated backups are retained seven days, which is the default and is adequate
for data that can be rebuilt from a Letterboxd export and TMDb.

### Migrations are a gated pipeline stage, never a startup step

The README has said since the first migration that they are not applied
automatically, because DDL from application startup races across instances on
deploy. Production is where that stops being theoretical.

So the release pipeline runs an **EF migration bundle** — a self-contained
executable produced by `dotnet ef migrations bundle` — as a separate job that must
succeed before the new application version is released. The bundle is built from
the same commit as the code that needs it, so the two cannot disagree.

This means **a migration runs against the previous version of the application**,
which is a real constraint rather than an inconvenience: every migration must be
backward compatible with the code already running. Dropping a column the live
version still reads takes two releases. That discipline is the cost of deploying
without downtime, and it is better to adopt it now, with one user, than to
discover it later.

### Secrets live in Key Vault, referenced not copied

The API needs a TMDb token, a JWT signing key and a connection string; the web
tier needs a session cookie password and Google client credentials. All are Key
Vault secrets, surfaced to App Service through Key Vault references and read by
the application as ordinary configuration — so no application code changes between
a laptop and production.

App Settings alone would be simpler and are rejected for one reason: they are
readable by anybody with contributor access to the resource, and they appear in
deployment diffs. Key Vault keeps the boundary somewhere a person can be granted
or denied.

**Terraform never sees the secret values.** They are created empty and populated
out of band, because a secret in Terraform is a secret in the state file.

### Federated credentials, not a stored key

GitHub Actions authenticates to Azure through OIDC federation. There is no service
principal password in a repository secret, which is the credential most likely to
leak and the one nobody rotates.

### One environment

Production only. No staging, and no deployment slots — B1 does not offer them, and
the tier that does costs more than the rest of the infrastructure combined.

The honest statement of the trade: **the safety net is the CI pipeline and a
branch protection rule, not a staging environment.** For a single-user project
that is proportionate. It stops being proportionate the moment somebody else
depends on this being up.

### The API stays on the public internet

The browser never calls it — ADR-0004 routes every request through the Next.js
tier — so it could be restricted to the web app's outbound addresses today.

It is not, because mobile is a stated direction and a phone calls the API
directly. Locking it down now would buy a few months of marginal defence and then
have to be undone. It is authenticated on every endpoint that touches data, which
is the control that actually matters.

### Observability

Application Insights, at the free ingestion tier, with the ASP.NET Core
auto-instrumentation and nothing custom. `CLAUDE.md` deferred this along with
Azure itself; a deployed application with no telemetry is a black box, and the
first production incident is the wrong time to discover that.

## Consequences

**Positive**

- The operational questions get answers instead of assumptions.
- Usage generates the interaction data the learned model needs.
- Infrastructure is reviewable in a pull request, like everything else here.
- No long-lived cloud credential exists to leak.

**Negative / accepted costs**

- **Roughly $45 a month**, for a project with one user. Real money, and the
  strongest argument for Container Apps if it ever stops being worth it.
- **Every migration must be backward compatible** with the running version. A
  column rename becomes two releases. This is a permanent constraint on how
  schema changes are written.
- **No staging environment.** A bad release reaches the only environment there is.
- **B1 is one instance.** A deploy is a brief interruption, and there is no
  horizontal scale until the plan tier changes.
- Terraform state becomes a thing that must itself be stored and protected.

## Alternatives considered

**Azure Container Apps**
Half the cost, scale-to-zero, the same Terraform and OIDC story. Rejected because
cold starts land on the first visitor, and for a project whose purpose includes
being shown to people, the first visitor is the one that matters.

**Vercel, Fly.io and Neon**
Roughly a tenth the cost and the fastest route to a working URL — genuinely how
many small teams ship. Rejected because the infrastructure would be three
platforms' dashboards rather than a reviewable description of a system, which is
most of what this exercise is for.

**A single VPS running the existing Docker Compose file**
Cheapest, most instructive about operations, and already half-written. Rejected
because the operational burden is permanent and unrelated to the product.

**Applying migrations at application startup**
One fewer pipeline stage and no bundle to build. Rejected for the reason the
README has always given: concurrent instances race, and the loser can leave a
schema half-changed. It is also precisely the mechanism by which a rollback
becomes impossible.
