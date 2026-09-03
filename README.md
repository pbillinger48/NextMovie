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
```

PostgreSQL is published on **port 5433**, not the default 5432, so it coexists
with a Homebrew PostgreSQL service. See [`.env.example`](.env.example).

### TMDb credentials

TMDb credentials are **not** stored in `.env`. The API reads them via .NET
user-secrets, which keeps them outside the repository directory entirely:

```bash
cd apps/api/NextMovie.Api
dotnet user-secrets set "Tmdb:ApiReadAccessToken" "<your-v4-token>"
```

Get a free token at [themoviedb.org/settings/api](https://www.themoviedb.org/settings/api).

## Repository layout

```
apps/
  web/        Next.js web application
  api/        ASP.NET Core API
  mobile/     Expo app (planned)
packages/
  shared-types/   Types not owned by the API contract (see ADR-0002)
  shared-utils/   Shared utilities
docs/
  adr/        Architecture Decision Records
```

`apps/` and `packages/` are currently empty — they are populated as the walking
skeleton is built.

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
