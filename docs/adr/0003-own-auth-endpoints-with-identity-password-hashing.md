# ADR-0003: Own authentication endpoints, using Identity's password hasher

- **Status:** Accepted
- **Date:** 2026-09-06

## Context

[requirements.md](../requirements.md) puts email/password authentication in the
MVP. Two constraints shape how it can be built:

- **Mobile is a planned client.** Expo cannot rely on browser cookies, so the API
  must be **bearer-token based** regardless of what the web tier does.
- **The project exists partly to demonstrate backend engineering.** Outsourcing
  authentication entirely removes a substantial piece of that.

"Don't roll your own auth" is sound advice that is usually stated too broadly.
The meaningful line is between:

- **cryptographic primitives** — password hashing, token signing. Never write these.
- **the protocol around them** — token lifetimes, refresh rotation, revocation,
  lockout. These are ordinary engineering, and owning them is a different risk
  profile from writing a hasher.

This ADR takes the first as non-negotiable and the second as ours.

## Decision

**Write authentication as vertical slices over our own `users` table, using
ASP.NET Core Identity's `PasswordHasher<T>` for hashing.**

Concretely:

- The `User` entity stays as designed in [database.md](../database.md) — no
  `AspNet*` tables.
- `RegisterUser`, `LoginUser`, `RefreshToken`, `LogoutUser` are feature slices
  like any other.
- Passwords are hashed with `PasswordHasher<T>` (PBKDF2-HMAC-SHA256, 100k
  iterations, in a **versioned format** so the work factor can be raised later
  without invalidating existing hashes).
- **Access token:** JWT, 15 minutes, stateless.
- **Refresh token:** opaque high-entropy random value, 30 days, stored in the
  database and **rotated on every use**. Reuse of an already-rotated token
  revokes the entire token family — the standard detection for a stolen refresh
  token.
- **Refresh tokens are stored as a SHA-256 hash, not a slow hash.** This is
  deliberate and is *not* an inconsistency with password hashing: slow hashing
  exists to resist brute force against low-entropy, human-chosen secrets. A
  128-bit random token has no such weakness, and PBKDF2 on every refresh would
  buy nothing while costing latency on a hot path.
- **Lockout** after repeated failed attempts, to blunt credential stuffing.
- Login responses must not reveal whether an email exists — same response and
  broadly similar timing for unknown-user and wrong-password.

Session handling in the browser is a separate decision: see [ADR-0004](0004-httponly-cookie-session-in-the-web-tier.md).

## Consequences

**Positive**

- The schema stays ours: one clean `users` table matching the documented design,
  in snake_case, with no framework-imposed naming.
- Authentication is written the same way as every other feature, so there is one
  architectural style in the codebase rather than two.
- No vendor, no bill, no network dependency on the login path.
- The interesting parts — rotation, reuse detection, revocation — are visible and
  reviewable rather than hidden in a library.

**Negative / accepted costs**

- **We own protocol correctness.** Refresh rotation, family revocation, lockout
  and enumeration resistance are ours to get right, and each has sharp edges.
  They must be tested deliberately, not assumed.
- Features that Identity would have provided free — 2FA, external logins,
  password reset — are ours to build when wanted.
- **A standing risk of scope creep into crypto.** The rule is explicit: use
  vetted primitives; if a change would mean writing or tuning a cryptographic
  algorithm, that is a signal to stop and reconsider this ADR.

## Alternatives considered

**Full ASP.NET Core Identity**
The most conservative option, and genuinely defensible: battle-tested hashing,
lockout, security stamps for session invalidation, and a clear path to 2FA and
external logins. Rejected because it introduces roughly seven `AspNet*` tables
that conflict with the clean schema in `database.md` and with our snake_case
convention, and because `MapIdentityApi` generates endpoints that are difficult
to reshape once requirements diverge from what it anticipated. Worth revisiting
if 2FA and external logins become near-term requirements — at that point Identity
starts paying for its weight.

**A managed provider (Auth0, Clerk, Entra External ID)**
The strongest security posture with the least code: no password storage, breach
detection, MFA, social login and reset flows included. For many products this is
the right answer. Rejected here because it adds a vendor, an eventual bill and a
network dependency on login, and because it removes exactly the backend work this
project is meant to demonstrate.

**Writing our own password hashing**
Not seriously considered, and recorded only to be explicit: this is the line the
decision above deliberately does not cross.
