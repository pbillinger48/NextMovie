# NextMovie — Project Rules

Movie discovery and recommendation platform. Mobile-first product, API-first architecture.

Full product docs live in [`docs/`](docs/). Decisions with lasting consequences are
recorded as ADRs in [`docs/adr/`](docs/adr/) — **read the ADR index before proposing
architectural changes.**

## Working agreement

Parker is using this project to grow into higher-level engineering roles. So:

- Explain *why*, not just *what*. Cover alternatives and tradeoffs for non-trivial work.
- Do not turn small, obvious changes into tutorials.
- For any non-trivial feature: inspect → explain findings → propose a plan → **wait for approval**.
- Never make an architectural decision unilaterally. Surface it and let Parker choose.
- Report failures honestly. Fix root causes; never mask, skip, or bypass a failing check.
- State plainly what remains broken if you cannot fix it.

## Stack

| Layer | Choice |
|---|---|
| Web | Next.js 16, React 19, TypeScript, Tailwind (`apps/web`) |
| API | ASP.NET Core, C# (`apps/api`) |
| Mobile | Expo / React Native (`apps/mobile`) — **deferred, not yet scaffolded** |
| Database | PostgreSQL via EF Core |
| Local infra | Docker Compose |
| Monorepo | pnpm workspaces + Turborepo |
| Node | 22 (pinned in `.nvmrc` — run `nvm use`) |

## Architecture rules

- **Vertical Slice Architecture** in the API. Each feature folder owns its endpoint,
  request, response, handler, validation, and data access. Minimise cross-feature coupling.
- The recommendation engine is a **domain module**, not a slice. It is cross-cutting by nature.
- Keep business logic out of infrastructure concerns (controllers, EF, HTTP clients).
- **Never let an external provider's DTOs become the domain model.** TMDb responses are
  mapped across an anti-corruption boundary into our own types.
- Types cross the C#/TS boundary by **generating TypeScript from the API's OpenAPI schema**
  (ADR-0002). Never hand-maintain duplicate DTOs in `packages/shared-types`.
- Build for maintainability before scalability. Do not add infrastructure for problems
  that have not been measured.

## Code standards

- No `any` without a written justification. Type safety is not traded for convenience.
- No new dependencies without a clear, stated reason.
- Tests target **meaningful business logic** — mapping, scoring, import matching, validation.
  Do not write tests that merely restate framework behaviour.
- Production-quality error handling and validation. API errors use RFC 7807 ProblemDetails.
- Consider security, performance, accessibility, and maintainability on every change.
- Do not modify files unrelated to the task at hand.
- Do not rewrite working code out of stylistic preference.

## Git workflow

- `main` stays stable. Work happens on feature branches (`feat/`, `chore/`, `fix/`, `docs/`).
- Clear commit messages. No giant commits mixing unrelated changes.
- **Never push, merge, or open a PR without asking first.**
- Never commit secrets, `.env` files, or generated artifacts.

## Secrets

- TMDb credentials live in **.NET user-secrets**, never in the repo. See `.env.example`.
- `.env` is gitignored. `.env.example` documents variables and holds no real values.
- `NEXT_PUBLIC_*` vars are browser-visible — never put a secret behind that prefix.

## Current state

Milestone 1 — "Walking Skeleton": browser → Next.js → ASP.NET API → EF Core → Postgres,
proven end-to-end via TMDb-backed movie search.

**Explicitly deferred** (do not build without being asked): authentication, users, ratings,
watch history, watchlists, Letterboxd import, the recommendation engine, mobile, Redis,
Azure, Terraform, Application Insights.

## Known doc issues

`docs/` was written before any code and contains inaccuracies not yet corrected —
see ADR-0001 and the assessment notes. Notably: `architecture.md` still says the web app
uses Vite (superseded by ADR-0001); Letterboxd has no public API and import must be
CSV-based; the schema has no region concept for streaming availability; and recommendation
feedback is modelled twice. Prefer ADRs over `docs/` where they conflict.
