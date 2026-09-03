# ADR-0001: Next.js for web, with a separate ASP.NET Core API

- **Status:** Accepted
- **Date:** 2026-09-03

## Context

The project documentation contradicted itself on the web stack. `docs/architecture.md`
specified **React + TypeScript + Vite**, while the working brief specified
**Next.js 16 + React 19 + Tailwind**. These are materially different architectures and the
conflict had to be resolved before any scaffolding.

Choosing Next.js raises an obvious follow-up question that deserves an explicit answer:

> If Next.js can run server-side code, why is there a separate ASP.NET Core API at all?

Without a recorded answer, this ambiguity would produce a system where business logic
accretes in both places — the most likely long-term failure mode for this project.

Relevant constraints:

- Mobile (Expo / React Native) is a planned first-class client. It cannot consume React
  Server Components or Next.js server actions; it needs a real network API.
- `docs/architecture.md` already commits to API-first: "The API is the single source of
  truth for all business logic. All clients consume the same endpoints."
- The recommendation engine is the core domain and the intended demonstration of
  backend engineering ability.
- C# / ASP.NET Core is a skill this project exists partly to demonstrate.

## Decision

**Use Next.js 16 (App Router) for `apps/web`.**

**Keep ASP.NET Core as the sole owner of business logic**, with a strict division of
responsibility:

| Layer | Owns | Never owns |
|---|---|---|
| ASP.NET Core API | Domain logic, persistence, recommendations, external integrations, authorization decisions | UI concerns |
| Next.js server | Rendering, routing, session cookie handling, BFF-style request shaping, secret-holding calls to the API | Domain logic, direct database access |

Concretely:

- **Next.js never talks to PostgreSQL.** There is no EF-equivalent, no ORM, and no
  connection string in `apps/web`. This is the bright line that keeps the boundary honest.
- Next.js server components may call the API to fetch data, including with credentials the
  browser must not see.
- Any rule that mobile would also need **must** live in the API.

## Consequences

**Positive**

- Server rendering for the movie/discovery pages, which are the SEO-relevant surface of a
  discovery product.
- Secrets can be held server-side in the web tier without exposing them to the browser.
- Mobile and web share one contract, so the domain cannot drift between clients.
- The API stays independently testable and deployable.

**Negative / accepted costs**

- Two server runtimes to run locally and deploy. Local development requires both
  `pnpm dev` and the .NET API running, which raises setup friction. Mitigated by Docker
  Compose and documented setup steps.
- One extra network hop for server-rendered data (Next server → API) versus a Next.js app
  querying its own database directly.
- Continuous discipline is required. The temptation to "just do this one query in a route
  handler" will recur, and each instance erodes the boundary. The no-database-access rule
  above exists to make violations obvious in code review.

## Alternatives considered

**Vite SPA + ASP.NET Core** (as originally documented)
Cleanest separation and the simplest mental model — a pure client talking to one API over
CORS. Rejected because it forfeits server rendering and SEO for a content-discovery
product, and contradicts the stated stack. This remains a reasonable fallback if the
two-runtime cost proves unjustified.

**Next.js full-stack, no ASP.NET Core**
Fewer moving parts and the fastest path to a working web product. Rejected because mobile
is a first-class target and would be left without an API, and because it discards the C#
backend that is central to the project's purpose.

**Next.js as a strict BFF over a private API**
Essentially the chosen design, but with the API network-isolated so only Next.js may reach
it. Deferred rather than rejected — it is a deployment topology decision, not an
application architecture one, and can be adopted later without code changes.
