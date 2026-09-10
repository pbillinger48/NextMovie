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

# 3. Create your local environment files
cp .env.example .env                        # docker-compose only
cp apps/web/.env.example apps/web/.env.local  # the web app

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

### Where configuration lives

Three places, and each app only reads its own. **The root `.env` is read by
docker-compose alone** — .NET has no `.env` convention, and Next.js loads env
files from `apps/web/`, not the repository root.

| Setting | Where it goes |
|---|---|
| `POSTGRES_*` | root `.env` (docker-compose) |
| `ConnectionStrings:NextMovieDb`, `Google:ClientIds` | `apps/api/NextMovie.Api/appsettings.Development.json` (committed, no secrets) |
| `Tmdb:ApiReadAccessToken`, `Jwt:SigningKey` | .NET user-secrets (never in the repo) |
| `SESSION_COOKIE_PASSWORD`, `GOOGLE_CLIENT_*` | `apps/web/.env.local` |

In deployed environments the API's settings come from environment variables
instead (`ConnectionStrings__NextMovieDb`, `Google__ClientIds__0`) — .NET does map
double underscores onto configuration keys; it simply does not read `.env` files.

### API secrets

```bash
cd apps/api/NextMovie.Api
dotnet user-secrets set "Tmdb:ApiReadAccessToken" "<your-v4-token>"
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
```

Both are validated at startup, so the API refuses to run without them rather than
failing on the first request that needs one. `Google:ClientIds` is validated the
same way but is not a secret, so a placeholder ships in
`appsettings.Development.json` — it matches no real token, which is what you want
until you add one.

`Jwt:SigningKey` signs access tokens with HS256 and must be at least 32
characters. Treat it like the database password — anyone holding it can mint a
token for any account — and use a distinct key per environment.

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

The web app needs `SESSION_COOKIE_PASSWORD` in **`apps/web/.env.local`** (at least
32 characters) before anyone can sign in — it encrypts the session cookie
described in ADR-0004. Generate one with `openssl rand -base64 32`.

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

### Google sign-in

The web tier runs the authorization-code flow with PKCE and hands the resulting
ID token to the API, which verifies it against Google's published keys
(ADR-0005). To use it locally, create a **Web application** OAuth client at
[console.cloud.google.com/apis/credentials](https://console.cloud.google.com/apis/credentials),
register `http://localhost:3000/api/auth/google/callback` as an authorised
redirect URI, and put the credentials in `apps/web/.env.local`.

The client ID goes in **two** places, and they must match: `GOOGLE_CLIENT_ID` in
`apps/web/.env.local` for the web tier that obtains the token, and
`Google:ClientIds` in the API's `appsettings.Development.json` for the API that
decides whose tokens it will trust. The secret goes only in the web tier.

Everything else runs without them — the shipped placeholder simply means the
Google button fails at Google.

### Sessions

The API only ever accepts bearer tokens, because mobile will need them. The
Next.js tier converts that into an encrypted, `httpOnly` cookie the browser
cannot read (ADR-0004), so cross-site scripting cannot steal a session and replay
it elsewhere.

Access tokens last 15 minutes and are refreshed in [`apps/web/proxy.ts`](apps/web/proxy.ts)
before they lapse — the only place that both sees every request and may write a
cookie. Prefetches are excluded from it deliberately: every refresh rotates the
refresh token, and a speculative prefetch rotating one would revoke the session
it was trying to speed up.

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
| `pnpm test` | Run all JavaScript tests |

The .NET suite is separate, since Turborepo does not drive it:

```bash
dotnet test apps/api/NextMovie.slnx
```

It uses Testcontainers, so **Docker must be running** — the persistence and
endpoint tests execute against a real PostgreSQL rather than an in-memory fake
that would accept constraints the real database rejects.

`pnpm test` covers the web tier's pure logic: the token refresh boundary, the
session shape, the Google OAuth helpers (PKCE, nonce, state comparison), and how
API failures are translated for the user. Anything needing a browser, a cookie
round trip or a live API is not covered there and has to be exercised against a
running stack.

## Contributing

`main` is kept stable; work happens on feature branches. Project conventions are
documented in [CLAUDE.md](CLAUDE.md).
