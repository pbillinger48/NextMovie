# NextMovie API

Version: 0.1
Status: Draft
Last Updated: August 2026

---

# Overview

The NextMovie API provides a REST interface for both the mobile and web applications.

The API is the single source of truth for all business logic.

All clients consume the same endpoints.

---

# Design Principles

The API should be:

- RESTful
- Versioned
- Secure
- Stateless
- Mobile-first
- Fast
- Consistent

All endpoints return JSON.

---

# Authentication

Authentication uses JWT access tokens.

Supported login methods:

- Email/password
- Google OAuth
- Apple Sign In (future)

Every authenticated request includes:

Authorization: Bearer {token}

---

# API Versioning

/api/v1/

Future breaking changes will use:

/api/v2/

---

# Authentication

Implemented per [ADR-0003](adr/0003-own-auth-endpoints-with-identity-password-hashing.md).
The exact request and response schemas are generated from the API into
`packages/api-client/NextMovie.Api.json`; that document is authoritative where
this page disagrees with it.

## Register

`POST /api/v1/auth/register`

Creates an account and signs it in, so the client never has to follow
registration with a second call carrying the password again.

```json
{
  "email": "parker@example.com",
  "displayName": "Parker",
  "password": "at least 12 characters"
}
```

`201 Created` returns the same session body as login (below).

| Status | When |
|---|---|
| `400` | Invalid email, missing display name, or a password outside 12–128 characters. |
| `409` | An account already exists for that address, ignoring case. |

The `409` does reveal that an address is registered. That is an accepted,
documented exposure: the enumeration-resistant alternative is to answer `200`
and send "you already have an account" by email, which needs a mailer that does
not exist yet. Login leaks nothing.

---

## Login

`POST /api/v1/auth/login`

```json
{
  "email": "parker@example.com",
  "password": "at least 12 characters"
}
```

`200 OK`:

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "accessTokenExpiresAt": "2026-09-06T12:15:00+00:00",
  "refreshToken": "0BFO4KMkr1cQ...",
  "refreshTokenExpiresAt": "2026-10-06T12:00:00+00:00",
  "user": {
    "id": "0199...",
    "email": "parker@example.com",
    "displayName": "Parker",
    "profileImageUrl": null
  }
}
```

The access token is a 15-minute JWT. The refresh token is an opaque 256-bit
value, stored only as a SHA-256 hash, valid for 30 days.

| Status | When |
|---|---|
| `400` | Email or password missing from the request. |
| `401` | Wrong password, no such account, **or** the account is locked out. |

The `401` is deliberately identical in all three cases, body included. Five
consecutive failures lock an account for 15 minutes, and the lockout is checked
before the password — so a correct password during a lockout still returns
`401`, and the response never says the account is locked. Saying so would
confirm the address exists.

---

## Refresh Token

`POST /api/v1/auth/refresh`

```json
{ "refreshToken": "0BFO4KMkr1cQ..." }
```

`200 OK` returns the same session body as login — **including a new refresh
token**. Every refresh rotates: the presented token is revoked and replaced
within the same family, so clients must store what comes back and discard what
they sent.

| Status | When |
|---|---|
| `400` | No refresh token in the request. |
| `401` | Unknown, expired, revoked, or replayed token. |

The `401` is identical in all four cases. Presenting an **already-rotated**
token is treated as theft and revokes the entire family, ending the session for
whoever else holds a token in it. A retry or two concurrent refreshes are
indistinguishable from a stolen token, so both holders are signed out — the
conservative response ADR-0003 specifies. Ordinary expiry is *not* treated as
theft and revokes nothing.

A locked-out account can still refresh. Lockout exists to stop password
guessing, and the holder of a valid refresh token has already authenticated;
blocking them would let anyone sign a user out of every device by deliberately
failing five sign-ins.

---

## Logout

`POST /api/v1/auth/logout`

```json
{ "refreshToken": "0BFO4KMkr1cQ..." }
```

`204 No Content`, always — including for an unknown or already-revoked token.
Sign-out is idempotent, and any other answer would let an unauthenticated caller
test whether a token is real. `400` only if the request carries no token at all.

Revokes the presented token's whole family, so it ends that device's session and
leaves other devices signed in.

**The access token is not revoked.** It is a stateless JWT and stays valid until
it expires, at most 15 minutes later. Signing out ends the ability to obtain new
access tokens, not the one already issued.

---

# User

Both endpoints require `Authorization: Bearer {token}` and act on the account the
token belongs to. There is no endpoint that takes a user id — a signed-in caller
can only ever read or change their own profile.

## Current User

`GET /api/v1/users/me`

`200 OK`:

```json
{
  "id": "0199...",
  "email": "parker@example.com",
  "displayName": "Parker",
  "profileImageUrl": null,
  "createdAt": "2026-09-06T12:00:00+00:00"
}
```

| Status | When |
|---|---|
| `401` | No token, an invalid or expired token, or a token whose account no longer exists. |

This page previously promised a taste profile and streaming providers here.
Neither has a schema and both are deferred, so neither is returned; they will be
added when the features exist rather than stubbed now.

---

## Update Profile

`PUT /api/v1/users/me`

```json
{
  "displayName": "Parker",
  "profileImageUrl": "https://example.com/parker.jpg"
}
```

Returns the updated profile, in the same shape as `GET`.

**`PUT` replaces.** A request that omits `profileImageUrl` clears it, so clients
must send the whole profile on every save. `profileImageUrl` must be an absolute
`http` or `https` URL — other schemes are rejected because clients render this
value as an image source.

| Status | When |
|---|---|
| `400` | Missing display name, one over 100 characters, or a URL that is not absolute http(s). |
| `401` | As above. |

**Email and password are not editable here.** Changing an email needs a
verification round trip before the address is claimed; changing a password needs
the current password and revokes every refresh token family. Each is its own
endpoint, not a field on a profile save. Neither is built yet.

---

# Movies

## Search Movies

GET

/api/v1/movies/search

Query Parameters

- title
- actor
- director
- genre
- page

---

## Movie Details

GET

/api/v1/movies/{movieId}

Returns

Movie metadata

Streaming availability

Recommendation explanation (if available)

---

## Trending Movies

GET

/api/v1/movies/trending

---

## New Releases

GET

/api/v1/movies/new

---

# Recommendations

## Tonight's Recommendation

GET

/api/v1/recommendations/tonight

Returns:

- Movie
- Match Score
- Confidence
- Explanation

---

## Recommendation Feed

GET

/api/v1/recommendations

Supports:

- Page
- Recipe
- Genre

---

## Recommendation Feedback

POST

/api/v1/recommendations/{id}/feedback

Actions

- Watched
- Liked
- Loved
- Not Interested
- Already Seen

---

# Ratings

## Rate Movie

POST

/api/v1/ratings

---

## Update Rating

PUT

/api/v1/ratings/{id}

---

## Delete Rating

DELETE

/api/v1/ratings/{id}

---

# Watch History

## Add Watched Movie

POST

/api/v1/watch-history

---

## Get Watch History

GET

/api/v1/watch-history

---

# Watchlist

## Get Watchlist

GET

/api/v1/watchlist

---

## Add Movie

POST

/api/v1/watchlist

---

## Remove Movie

DELETE

/api/v1/watchlist/{movieId}

---

# Streaming Providers

## User Providers

GET

/api/v1/streaming/providers

---

## Update Providers

PUT

/api/v1/streaming/providers

---

# Letterboxd

## Import

POST

/api/v1/import/letterboxd

Starts an asynchronous import.

Returns

Import Job Id

---

## Import Status

GET

/api/v1/import/{jobId}

Returns:

- Pending
- Running
- Completed
- Failed

---

# Taste Profile

## Get Taste Profile

GET

/api/v1/taste-profile

Returns:

- Favorite genres
- Favorite directors
- Favorite themes
- Favorite runtime
- Favorite decades

---

# Statistics

## Dashboard

GET

/api/v1/statistics

Returns:

- Movies watched
- Average rating
- Favorite genres
- Rating distribution
- Watch streak

---

# Health

GET

/api/v1/health

Used for monitoring.

---

# Error Format

All errors return:

{
    "status": 404,
    "title": "Movie Not Found",
    "detail": "The requested movie could not be found."
}

---

# Future APIs

- Friends
- Movie Night
- AI Assistant
- Notifications
- TV Shows
- Collections