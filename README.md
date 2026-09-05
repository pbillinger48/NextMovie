# NextMovie

Movie discovery and recommendation platform. Helps users find the best movie they
haven't seen that they can stream right now.

**Status:** 🚧 Early development — building the walking skeleton
(browser → Next.js → ASP.NET Core API → EF Core → PostgreSQL).

## Documentation

| Doc | Purpose |
|---|---|
| [Vision](docs/vision.md) | Mission and long-term goals |
| [PRD](docs/requirements.md) | Product requirements |
| [User flows](docs/user-flows.md) | Screen-by-screen journeys |
| [Architecture](docs/architecture.md) | System design |
| [Database](docs/database.md) | Schema design |
| [Recommendation engine](docs/recommendation-engine.md) | Core domain design |
| [API](docs/api.md) | Endpoint reference |
| [Roadmap](docs/roadmap.md) | Phased delivery plan |
| **[ADRs](docs/adr/)** | **Architecture decisions and their rationale** |
| [Spikes](docs/spikes/) | Time-boxed investigations and what they found |

> The documents in `docs/` were written before any code and contain known
> inaccuracies. Where an ADR conflicts with `docs/`, **the ADR is authoritative**.

## Tech stack

| Layer | Technology |
|---|---|
| Web | Next.js 16, React 19, TypeScript, Tailwind CSS |
| API | ASP.NET Core (.NET 10), C# |
| Mobile | Expo / React Native *(planned, not yet scaffolded)* |
| Database | PostgreSQL 18 via EF Core |
| Local infra | Docker Compose |
| Monorepo | pnpm workspaces + Turborepo |

## Prerequisites

- **Node.js 22** — the repo pins this in [`.nvmrc`](.nvmrc); run `nvm use`
- **pnpm 10** — `corepack enable`
- **.NET SDK 10** — `brew install --cask dotnet-sdk`
- **Docker Desktop** — for PostgreSQL

## Getting started

```bash
# 1. Use the pinned Node version
nvm use

# 2. Install JavaScript dependencies
pnpm install

# 3. Create your local environment file
cp .env.example .env

# 4. Start PostgreSQL
docker compose up -d

# 5. Verify the database is healthy
docker compose ps

# 6. Restore local .NET tools (pins dotnet-ef to the version this repo expects)
dotnet tool restore

# 7. Apply database migrations
dotnet ef database update --project apps/api/NextMovie.Api
```

PostgreSQL is published on **port 5433**, not the default 5432, so it coexists
with a Homebrew PostgreSQL service. See [`.env.example`](.env.example).

## Database migrations

Migrations are **never applied automatically**. Running DDL from application
startup races across instances during a rolling deploy, ships schema changes
unreviewed, and requires the application to hold permanent DDL permissions.

```bash
# Apply pending migrations locally
dotnet ef database update --project apps/api/NextMovie.Api

# Add a migration after changing an entity or its configuration
dotnet ef migrations add <Name> \
  --project apps/api/NextMovie.Api \
  --output-dir Infrastructure/Persistence/Migrations

# Undo the most recent migration (only if it has NOT been applied or pushed)
dotnet ef migrations remove --project apps/api/NextMovie.Api

# Review the SQL rather than trusting the tool
dotnet ef migrations script --project apps/api/NextMovie.Api --idempotent
```

For deployed environments, generate an idempotent script, review it, and let the
deploy pipeline apply it — the application itself never runs migrations.

### TMDb credentials

TMDb credentials are **not** stored in `.env`. The API reads them via .NET
user-secrets, which keeps them outside the repository directory entirely:

```bash
cd apps/api/NextMovie.Api
dotnet user-secrets set "Tmdb:ApiReadAccessToken" "<your-v4-token>"
```

Get a free token at [themoviedb.org/settings/api](https://www.themoviedb.org/settings/api).
Prefer the v4 **API Read Access Token** (a bearer JWT) over the v3 `api_key`.

> **Attribution is required.** TMDb's terms of use require any product built on
> their API to display their logo and the statement *"This product uses the TMDB
> API but is not endorsed or certified by TMDB."* This must appear in the web and
> mobile clients — it is an obligation, not a courtesy.

## Running the app

Two processes, in separate terminals:

```bash
# API — http://localhost:5080
dotnet run --project apps/api/NextMovie.Api

# Web — http://localhost:3000
pnpm --filter @nextmovie/web dev
```

Then open <http://localhost:3000> and search for a film.

```bash
# Or drive the API directly
curl "http://localhost:5080/api/v1/health"
curl "http://localhost:5080/api/v1/movies/search?title=arrival"
```

Search is **read-through**: results come from TMDb, are written into the local
catalogue, and are returned from NextMovie's own model — so responses carry
NextMovie identifiers, and the catalogue grows as people search.

The browser never calls the API directly. Search runs in a React Server
Component, so there is no CORS configuration and no API URL in client code
(ADR-0001).

## The API contract

Per [ADR-0002](docs/adr/0002-generate-typescript-from-openapi.md), TypeScript
types are **generated** from the API's OpenAPI schema and never hand-written.

```
C# endpoints + DTOs
        │  dotnet build   (OpenApiGenerateDocumentsOnBuild)
        ▼
packages/api-client/NextMovie.Api.json
        │  pnpm generate  (openapi-typescript)
        ▼
packages/api-client/src/schema.d.ts
        │
        ▼
     apps/web
```

Both artefacts are committed, so a fresh clone type-checks without a running
API. After changing any endpoint or DTO:

```bash
dotnet build apps/api/NextMovie.Api   # rewrites the OpenAPI document
pnpm generate                          # rewrites the TypeScript types
```

CI runs both and **fails if the committed output differs** — which is what makes
this a guarantee rather than a habit.

## Repository layout

```
apps/
  web/          Next.js web application
  api/          ASP.NET Core API
  mobile/       Expo app (planned)
packages/
  api-client/   OpenAPI document + generated TypeScript client
docs/
  adr/          Architecture Decision Records
```

`apps/mobile` is not scaffolded yet. There is deliberately no `shared-types`
package: the API contract is generated (ADR-0002), and a hand-written duplicate
would be free to drift from it.

## Scripts

Run from the repository root; Turborepo fans each task out across workspaces.

| Command | Description |
|---|---|
| `pnpm dev` | Start all apps in development mode |
| `pnpm build` | Build all packages and apps |
| `pnpm lint` | Lint all workspaces |
| `pnpm typecheck` | Type-check all workspaces |
| `pnpm test` | Run all tests |

## Contributing

`main` is kept stable; work happens on feature branches. Project conventions are
documented in [CLAUDE.md](CLAUDE.md).
