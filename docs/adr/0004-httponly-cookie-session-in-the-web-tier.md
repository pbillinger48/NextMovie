# ADR-0004: Keep the browser session in an httpOnly cookie held by Next.js

- **Status:** Accepted
- **Date:** 2026-09-06

## Context

[ADR-0003](0003-own-auth-endpoints-with-identity-password-hashing.md) makes the
API bearer-token based, because mobile requires it. That leaves an open question
the API cannot answer for the browser: **where does the web client keep its
session?**

The conventional SPA answer is a token in `localStorage` or memory, attached to
each request. Its failure mode is well known and severe: **any** cross-site
scripting anywhere on the page — including from a transitive npm dependency —
can read the token and take over the account. `httpOnly` exists precisely because
this pattern keeps failing.

[ADR-0001](0001-nextjs-for-web-with-separate-api.md) gives us an option most SPAs
do not have. Because the browser never calls the API directly, a server tier
already sits in the request path and can hold credentials the browser never sees.

## Decision

**The API only ever accepts bearer tokens. The Next.js tier converts that into an
`httpOnly` cookie for the browser.**

```
browser --[ httpOnly cookie ]--> Next.js --[ Bearer JWT ]--> ASP.NET API
mobile  ------------------------[ Bearer JWT ]------------> ASP.NET API
```

- Sign-in posts to a Next.js server action or route handler, which calls the API,
  receives the access and refresh tokens, and stores them in an **encrypted,
  `httpOnly`, `Secure`, `SameSite=Lax`** cookie.
- **The browser never receives a token in any form readable by JavaScript.**
- Server components read the cookie, decrypt it, and attach the bearer token when
  calling the API — the same `server-only` boundary already used for search.
- Token refresh happens server-side, invisibly to the browser.
- **The API is never taught about cookies.** Cookie handling is a web-tier
  concern wrapping a token API, not a second authentication scheme.

**CSRF must be handled deliberately.** Cookie-borne credentials are automatically
attached by the browser, which is exactly what CSRF exploits. Mitigations:
`SameSite=Lax` (blocks cross-site POSTs while preserving normal top-level
navigation), state-changing operations only over POST, and same-origin
verification on mutating handlers.

## Consequences

**Positive**

- **XSS cannot exfiltrate the session.** This is the whole point: an attacker who
  achieves script execution can still act as the user *within that page*, but
  cannot steal a token and replay it elsewhere, later, or at scale.
- No CORS configuration, because the browser still never contacts the API — the
  security surface ADR-0001 avoided stays closed.
- Authenticated pages can be server-rendered, since the server holds the
  credential at render time.
- Mobile is entirely unaffected and keeps using bearer tokens directly.

**Negative / accepted costs**

- **We have traded XSS token theft for CSRF exposure.** That is a good trade —
  CSRF has well-understood, cheap defences while XSS token theft does not — but
  it is a trade, not a free win, and the defences must actually be implemented.
- Real session code now lives in the web tier: encryption, expiry, refresh, and
  clearing on sign-out.
- Cookies cap at ~4KB. Holding both tokens should fit comfortably, but if the
  session grows this forces a move to server-side session storage.
- Two authentication paths exist in the system as a whole (cookie for web, bearer
  for mobile), even though the API itself only implements one. The seam is the
  web tier, and it must not leak into the API.

## Alternatives considered

**httpOnly cookie issued by the API directly**
Equally safe against token theft, and simpler in that the web tier holds no
session state. Rejected because it reopens the browser-to-API path ADR-0001
deliberately closed — requiring permanent CORS-with-credentials and cookie domain
configuration — and because cookie auth suits mobile poorly, so the API would end
up implementing two authentication schemes instead of one.

**Access token in `localStorage` or JavaScript memory**
The familiar SPA pattern, identical for web and mobile, and the least code.
Rejected on security grounds: it makes any XSS anywhere in the dependency tree an
account takeover, requires CORS, and prevents server-rendering authenticated
content. Memory-only storage narrows the window but does not close it, and costs
session persistence across reloads.
