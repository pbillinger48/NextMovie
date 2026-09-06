# Architecture Decision Records

An ADR captures a decision that is **expensive to reverse**, together with the context that
made it the right call and the consequences we accepted. The value is not the decision —
it is the reasoning, which is otherwise lost within weeks.

Write an ADR when a choice constrains future work: frameworks, data models, boundaries
between systems, auth strategy, external dependencies. Do not write one for reversible
details like file layout or naming.

ADRs are **immutable once accepted**. If a decision changes, write a new ADR that supersedes
the old one and update the status of the original. Never edit history — the record of what
we believed, and why we were wrong, is the useful part.

Where an ADR and `docs/` disagree, **the ADR wins** — the product docs predate the code.

## Index

| ADR | Title | Status |
|---|---|---|
| [0001](0001-nextjs-for-web-with-separate-api.md) | Next.js for web, with a separate ASP.NET Core API | Accepted |
| [0002](0002-generate-typescript-from-openapi.md) | Generate TypeScript types from the OpenAPI schema | Accepted |
| [0003](0003-own-auth-endpoints-with-identity-password-hashing.md) | Own authentication endpoints, using Identity's password hasher | Accepted |
| [0004](0004-httponly-cookie-session-in-the-web-tier.md) | Keep the browser session in an httpOnly cookie held by Next.js | Accepted |

## Template

```markdown
# ADR-NNNN: Title

- **Status:** Proposed | Accepted | Superseded by ADR-NNNN
- **Date:** YYYY-MM-DD

## Context
The forces at play. What problem, what constraints, what we knew at the time.

## Decision
What we chose, stated plainly.

## Consequences
What this makes easy, what it makes hard, and what we accepted as a cost.

## Alternatives considered
What else was on the table and why it lost.
```
