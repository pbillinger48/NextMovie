# ADR-0002: Generate TypeScript types from the OpenAPI schema

- **Status:** Accepted
- **Date:** 2026-09-03

## Context

The backend is C# and the clients (web, later mobile) are TypeScript. Every API request and
response therefore has a type definition on both sides of a language boundary.

The original plan called for a hand-written `packages/shared-types` package. That approach
has a specific, well-known failure mode: the C# DTO and the TypeScript interface are two
independent declarations of the same contract, kept in sync only by human diligence. When a
backend field is renamed, retyped, or made nullable, **nothing breaks** — the TypeScript
type still compiles, still type-checks, and is now silently wrong. The failure surfaces at
runtime, in production, as `undefined`.

This is worth stating precisely, because it is the exact inversion of the goal: a package
introduced to guarantee type safety would instead provide *false confidence* in type safety.
An untyped `fetch` is safer, because nobody trusts it.

The project's stated principle is "do not sacrifice type safety for convenience." Honouring
that requires a **single source of truth**, mechanically enforced.

## Decision

**The API's OpenAPI schema is the single source of truth for the HTTP contract.**
TypeScript types are generated from it. They are never hand-written.

Pipeline:

```
C# endpoints + DTOs  →  OpenAPI schema  →  generated TypeScript  →  web / mobile
     (source of truth)      (artifact)         (artifact)
```

Rules:

1. ASP.NET Core produces the OpenAPI document from the real endpoints and DTOs.
2. A generator emits TypeScript into a workspace package consumed by clients.
3. **Generated output is committed** to the repository. It is a build input for the web app,
   and committing it keeps `pnpm install && pnpm build` working without a running API —
   which matters for CI, for fresh clones, and for editor tooling.
4. **CI regenerates and fails if the output differs from what is committed.** This is what
   converts the convention into a guarantee: a backend change that alters the contract
   without regenerating cannot merge.
5. `packages/shared-types` is reserved for types that are genuinely shared but **not** owned
   by the API contract. It starts empty. If it stays empty, that is a success, not a gap.

Generator selection (`NSwag` vs `Kiota` vs `openapi-typescript`) is an implementation
detail, deliberately left to Milestone 1 Step 8 when there is a real endpoint to generate
from. The decision recorded here is the *direction*, which is what constrains the design.

## Consequences

**Positive**

- Contract drift becomes a **build failure at the point of change**, not a runtime bug in
  production. This is the entire point.
- DTOs are written once, in the language that owns them.
- Mobile inherits the same generated contract for free.
- The OpenAPI document doubles as live API documentation, replacing the hand-maintained
  endpoint list in `docs/api.md` — which is already at risk of going stale.

**Negative / accepted costs**

- A generation step exists in the build, and contributors must know to run it. Mitigated by
  wiring it into a Turborepo task and enforcing it in CI.
- Generated code is committed, so diffs are noisier and reviewers must learn to skim
  generated files.
- The generated types are only as good as the OpenAPI schema. Sloppily annotated C#
  endpoints produce sloppy TypeScript — in particular, **C# nullability must be modelled
  accurately**, or the generated types will claim non-null where the API returns null.
  Nullable reference types should be enabled in the API from the first commit.
- We are coupled to a generator's conventions and its handling of edge cases
  (polymorphism, `oneOf`, dates as strings).

## Alternatives considered

**Hand-written `packages/shared-types`**
Zero tooling and full control over the emitted types. Rejected for the silent-drift failure
described above.

**No shared types; `fetch` returning `unknown`, validated with Zod at the boundary**
Genuinely defensible, and stronger than generation in one respect: it validates the data
that actually arrives at runtime, rather than trusting a schema. Rejected as the primary
mechanism because the Zod schemas are themselves hand-written duplicates of the C# DTOs —
reintroducing the same drift problem one layer down. **Worth revisiting selectively** for
untrusted or high-risk payloads, where runtime validation earns its cost.

**Generate C# from a hand-written OpenAPI spec (spec-first)**
Makes the contract a first-class artifact designed deliberately rather than emitted as a
byproduct, which is the stronger choice for a public or multi-team API. Rejected as
premature for a solo project with two internal clients: it adds a design step before every
endpoint and slows iteration while the domain is still moving. Reconsider if the API is
ever published externally (currently a non-goal per `docs/requirements.md`).
