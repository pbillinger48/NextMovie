# ADR-0005: Clients obtain Google ID tokens; the API verifies them

- **Status:** Proposed
- **Date:** 2026-09-10

## Context

[requirements.md](../requirements.md) puts Google sign-in in the MVP, and it is
the last unbuilt item in Phase 2. Two decisions already constrain how it can
work:

- **[ADR-0003](0003-own-auth-endpoints-with-identity-password-hashing.md):** the
  API is bearer-token based, because Expo cannot rely on browser cookies. It
  issues its own access and refresh tokens and knows nothing about sessions.
- **[ADR-0004](0004-httponly-cookie-session-in-the-web-tier.md):** the browser's
  session is an encrypted cookie held by Next.js. The browser never receives a
  token, and the API is never taught about cookies.

Google's flow is redirect-based, which fits neither of those cleanly. A redirect
has to land *somewhere*, and whichever tier owns that landing grows the whole
apparatus around it: authorization state, PKCE verifiers, per-client redirect URI
registration, and a way to hand the result to the tier that actually holds the
session.

The schema is already prepared. ADR-0003 made `User.PasswordHash` nullable
specifically so an account can exist without a password, and named
`user_external_logins` as the additive table that would arrive with this feature.
Nothing here migrates existing data or changes the password flow.

## Decision

**Each client completes the Google flow itself and presents the resulting ID
token to a single API endpoint, which verifies it.**

```
browser ──> Next ── authorization code + PKCE ──> Google
            Next <────────── id_token ────────── Google
            Next ── POST /api/v1/auth/google { idToken } ──> API
            Next <────── access token + refresh token ────── API
            Next seals both into the httpOnly cookie

mobile  ── native Google SDK ──> id_token
        ── POST /api/v1/auth/google ──> the same endpoint
```

Concretely:

- **One new endpoint**, `POST /api/v1/auth/google`, taking an ID token and
  returning exactly what `login` returns. Refresh, rotation, family revocation
  and logout are unchanged — a Google session is a NextMovie session like any
  other.
- **The API verifies the ID token** before trusting a single claim in it:
  signature against Google's published JWKS (cached, and re-fetched on key
  rotation), `iss` of `https://accounts.google.com` or `accounts.google.com`,
  `exp` and `iat` within tolerance, and `aud` against an **allow-list of our own
  client IDs** — web today, iOS and Android later.
- **Identity is keyed on `sub`, never on email.** Google's subject identifier is
  stable for the life of the account; an email address is not. Keying on email
  would mean a user who changes their Google address becomes a stranger, and
  worse, that whoever later acquires their old address becomes them.
- **New table `user_external_logins`**: user id, provider, provider subject,
  created at, with a unique index on `(provider, provider_subject)`. A user may
  have several; a Google identity belongs to exactly one account.
- **Linking is automatic only when Google reports `email_verified`.** A verified
  Google address matching an existing account attaches the identity to it and
  signs the user in; both routes then reach one account and the password keeps
  working. An **unverified** address matching an existing account is refused, and
  so is creating a second account for it — the unique index on
  `normalized_email` forbids that anyway.
- **The client is responsible for its half of the flow**: PKCE, the `state`
  parameter, and a `nonce` echoed in the ID token. The web tier holds the client
  secret, as a confidential server-side client should.
- No password is created for an external-only account. `PasswordHash` stays null,
  and a later "set a password" flow is additive.

## Consequences

**Positive**

- **The API grows one endpoint and no redirect handling.** No authorization state
  to store, no PKCE verifiers to persist, no per-client redirect URIs, no
  one-time-code exchange. The security-critical part — verifying an ID token — is
  a well-specified operation against published keys.
- **Web and mobile share it exactly.** Expo will use the native Google SDK and
  post to the same endpoint, so mobile needs no new API surface.
- ADR-0004 is untouched: the browser still never sees a token, because Next does
  the exchange and seals the result.
- Sessions from Google are indistinguishable downstream, so rotation and
  revocation needed no special cases.

**Negative / accepted costs**

- **We accept an ID token instead of performing the code exchange ourselves.**
  Anything holding a valid, unexpired ID token minted for one of our audiences
  can sign in as that user. Audience pinning and Google's one-hour expiry bound
  this, and TLS between Next and Google closes the realistic interception path —
  but it is a genuinely weaker position than the API redeeming the code itself,
  and it is the trade this ADR makes for the simplicity above.
- **We trust Google's `email_verified` for linking.** That is a deliberate trust
  decision, not an oversight; if it is ever wrong, an account is taken over.
- Client IDs and secrets must be managed per client, and the audience allow-list
  grows with each one. A stale entry is a way in, so it is configuration that
  must be reviewed, not appended to.
- Google being unreachable takes this sign-in path down with it. The password
  path is unaffected.
- Unlinking a provider, and recovering an account whose only login is a Google
  account the user has lost, are not addressed here and will need their own work.

## Alternatives considered

**The API owns the redirect flow**
`/auth/google/start` and `/auth/google/callback` on the API, which exchanges the
code itself and redirects back to the web tier with a short-lived one-time code
that Next swaps for the token pair. Genuinely more secure in one respect: the API
redeems the authorization code, so a leaked ID token is not sufficient to
impersonate anyone. Rejected because it moves a large amount of machinery into
the API — authorization state, PKCE storage, redirect URI configuration per
client, and an extra exchange endpoint that exists only to get tokens across a
tier boundary — for a single provider. Worth revisiting if a second or third
provider arrives, at which point that apparatus starts paying for itself, or if
the ID-token exposure above proves unacceptable.

**Auth.js (NextAuth) in the web tier**
The conventional Next.js answer, and it would handle the provider dance,
callbacks and session cookie in a few lines. Rejected because it wants to own the
session, which ADR-0004 already decided and which the API's tokens already are —
we would be running two session systems and reconciling them. It also does
nothing for mobile, so the API would still need its own path.

**A managed provider for all authentication**
Already considered and rejected in ADR-0003, for reasons that have not changed.
Recorded here only so the option is not silently forgotten now that a second
identity source is arriving.
