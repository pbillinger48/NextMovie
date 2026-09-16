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

## Sign in with Google

`POST /api/v1/auth/google`

```json
{ "idToken": "eyJhbGciOiJSUzI1NiIs..." }
```

The client completes the Google flow itself and posts the resulting ID token
([ADR-0005](adr/0005-verify-google-id-tokens-at-the-api.md)) — the API handles no
redirects and holds no client secret. `200 OK` returns the same session body as
login, and that session refreshes and revokes like any other.

| Status | When |
|---|---|
| `400` | No ID token in the request. |
| `401` | The token failed verification: bad signature, wrong audience, wrong issuer, or expired. |
| `403` | Google reports the account's email address as unverified. |

What happens on success depends on what we already know:

| Situation | Result |
|---|---|
| The Google subject is already linked | Signed in as that account |
| Verified address matches an existing account | Linked to it, then signed in — the password keeps working |
| Verified address is unknown | A new account, with no password |
| Address is **not** verified by Google | `403`, whether or not an account exists |

Identity is keyed on Google's `sub`, never on the email address. Changing your
Google address keeps you in the same account, and whoever later acquires your old
address does not inherit it.

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

`watch` says where the signed-in viewer can see it, in the same shape as
recommendations. Anonymous visitors get `known: false` and nothing else —
availability depends on who is asking.

This page previously promised a taste profile here too. It has no schema and is
deferred, so it is not returned.

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

## Watchlist

`GET /api/v1/users/me/watchlist`

The films the user has saved, most recently saved first, each with where it can
be watched in their region.

```json
{
  "films": [
    {
      "movieId": "0199...",
      "title": "Heat",
      "posterPath": "/umSVjVdbVwtx5ryCA2QXL44Durm.jpg",
      "releaseDate": "1995-12-15",
      "runtime": 170,
      "averageRating": 7.9,
      "genres": ["Action", "Crime", "Drama"],
      "savedAt": "2026-09-16T21:40:00Z",
      "watch": { "streamingOn": ["Netflix"], "canStreamNow": true }
    }
  ]
}
```

**There is no watchlist table.** The watchlist *is* the set of films answered
`Saved` ([ADR-0011](adr/0011-recommendation-responses.md)); this endpoint is a
query over them. Two tables would let "saved" and "on the watchlist" disagree.

Films are added and removed through the response endpoints below, not here. At
most 100 are returned — availability costs an upstream call per film on a cold
cache, so an unbounded list would be an unbounded page load.

| Status | When |
|---|---|
| `401` | Not signed in. |

---

## Streaming Settings

`GET /api/v1/users/me/streaming`

Where this person watches and what they pay for — the settings that turn
"streaming somewhere" into "streaming on something you have"
([ADR-0010](adr/0010-streaming-availability.md)).

```json
{
  "region": "US",
  "countries": [{ "code": "AF", "name": "Afghanistan" }],
  "services": [
    {
      "id": 8,
      "name": "Netflix",
      "logoPath": "/pbpMk2JmcoNnQwx5JGpXngfoWtp.jpg",
      "subscribed": true,
      "offeredHere": true
    }
  ]
}
```

**Its own resource, not fields on the profile.** The service list is fetched from
an external catalogue and runs to a hundred entries; putting it on
`GET /users/me` would make every profile read pay for it, including the one the
site header does on every page.

`countries` is every ISO 3166-1 alpha-2 country, ordered by name — not only the
ones our availability source covers. Narrowing it would mean another upstream
call and another cache, and the failure it would prevent is already visible:
choose a country nothing is known about and `services` comes back empty.

`services` is the country's catalogue, ordered as the source ranks it for that
country rather than alphabetically. **`offeredHere: false` means a service the
user subscribes to that this country does not offer** — someone who ticked it and
then moved. It is listed rather than dropped: the subscription is still stored and
still shaping recommendations, so hiding it would leave no way to undo it.

| Status | When |
|---|---|
| `401` | Not signed in. |

---

## Update Streaming Settings

`PUT /api/v1/users/me/streaming`

```json
{
  "region": "GB",
  "providerIds": [8, 1899]
}
```

Returns the updated settings, in the same shape as `GET`.

**`PUT` replaces.** A service absent from `providerIds` is cancelled — that is the
only way unticking one can mean anything. An empty list says "I subscribe to
nothing", which is a real answer.

**Region and services move together** because they are one decision. Saving them
separately would leave a window where the region said Britain and the services
were the American ones, and anything recommended in that window would be
confidently wrong.

| Status | When |
|---|---|
| `400` | Missing region, a code that is not ISO 3166-1 alpha-2, an unknown service id, or more than 50 services. |
| `401` | As above. |

---

---

## Respond to a Film

`PUT /api/v1/movies/{id}/response`

```json
{ "response": "Saved" }
```

What the user wants to do about a film: `Saved`, `NotInterested`, or `Seen`.
Returns where the film now stands:

```json
{
  "movieId": "0199...",
  "response": "Saved",
  "watched": false,
  "respondedAt": "2026-09-16T21:40:00Z"
}
```

**`PUT`, because a person has one current answer about a film.** Saving twice
leaves it saved once; saving something previously dismissed replaces the
dismissal rather than storing a contradiction.

**`Seen` is not stored as a response.** It records a viewing instead — the same
place ratings put one, with a null date, because saying you have seen a film says
nothing about when ([ADR-0006](adr/0006-ratings-and-watch-history.md),
[ADR-0011](adr/0011-recommendation-responses.md)). So `response` comes back
`null` and `watched` comes back `true`. It also withdraws any earlier `Saved` or
`NotInterested`, which were statements about a film the user had not seen.

**All three stop the film being recommended again.** Before this, the only way to
stop seeing a film was to go and watch it.

The impression that prompted the response is recorded against it, resolved on the
server. **Clients do not send an event id** — one could attribute a response to
somebody else's impression, corrupting exactly the data this collects.

| Status | When |
|---|---|
| `400` | A response that is not one of the three. |
| `401` | Not signed in. |
| `404` | No film with that identifier is in the catalogue. |

---

## Withdraw a Response

`DELETE /api/v1/movies/{id}/response`

Takes a film off the watchlist, or un-hides a dismissed one — the same operation,
because they are the same row. Returns the resulting state, in the same shape as
`PUT`.

**Idempotent**, and no `404` for a film with no response: the caller asked for
there to be none, and there is none. Clients undo from a list that may already
have moved on.

**It does not un-watch anything.** Deleting viewings is a capability ADR-0006 does
not cover, and silently removing one here would destroy history this endpoint was
never asked about — so a film marked `Seen` still comes back `watched: true`.

| Status | When |
|---|---|
| `401` | Not signed in. |

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

`GET /api/v1/movies/{id}`

The identifier is **NextMovie's**, as returned by search — not TMDb's. A third
party's numbering is not part of our contract.

`200 OK`:

```json
{
  "id": "0199...",
  "tmdbId": 27205,
  "title": "Inception",
  "originalTitle": "Inception",
  "overview": "A thief who steals corporate secrets…",
  "posterPath": "/poster.jpg",
  "backdropPath": "/backdrop.jpg",
  "releaseDate": "2010-07-15",
  "runtime": 148,
  "averageRating": 8.4,
  "popularity": 82.3,
  "language": "en",
  "status": "Released",
  "genres": ["Action", "Science Fiction"]
}
```

| Status | When |
|---|---|
| `404` | No film with that identifier is in the catalogue. |

**Read-through, like search.** The film is served from our catalogue; TMDb is
consulted only to supply what search cannot — `runtime` and `status` — or to
refresh details older than seven days. After the first fetch, most reads never
leave our database.

**If TMDb is unavailable the film is still returned**, from whatever we already
hold. That includes films TMDb has since removed: our copy outlives theirs.

> This page previously promised streaming availability and a recommendation
> explanation here. Neither is returned: streaming availability has no schema and
> no region concept yet, and the recommendation engine is deferred. They will be
> added when the features exist rather than stubbed now.

---

## Trending Movies

GET

/api/v1/movies/trending

---

## New Releases

GET

/api/v1/movies/new

---

# Ratings

All rating endpoints require `Authorization: Bearer {token}` and act on the
account the token belongs to. Ratings use a **0.5–5.0 half-star scale**
([ADR-0006](adr/0006-ratings-and-watch-history.md)) — deliberately not TMDb's
0–10, which is the community rating on the film itself and a different quantity.

## Rate a film

`PUT /api/v1/movies/{id}/rating`

```json
{ "rating": 4.5 }
```

`PUT`, not `POST`: a person holds one opinion of a film at a time, so sending the
same rating twice leaves them rating it once.

`200 OK`:

```json
{
  "movieId": "0199...",
  "rating": 4.5,
  "ratedAt": "2026-09-10T12:00:00+00:00"
}
```

| Status | When |
|---|---|
| `400` | Outside 0.5–5.0, or not a whole or half star. |
| `401` | Not signed in. |
| `404` | No film with that identifier is in the catalogue. |

**Rating a film also records that it was watched**, with an unknown date, if no
viewing exists. Rating something means you have seen it, and a recommender that
kept suggesting films you had rated would be plainly broken.

---

## Your rating of a film

`GET /api/v1/movies/{id}/rating`

```json
{
  "movieId": "0199...",
  "rating": 4.5,
  "ratedAt": "2026-09-14T12:00:00+00:00"
}
```

`404` when you have not rated it — which is a different statement from a rating
of zero, and only one of them is representable.

Deliberately separate from the film details endpoint rather than folded into it:
details are readable by anyone, and making an anonymous-friendly response depend
on who is asking is how a cache eventually serves one person's opinion to
somebody else.

---

## Remove a rating

`DELETE /api/v1/movies/{id}/rating`

`204 No Content`, always — including when there was no rating to remove, since
deleting twice is not an error.

The film **stays in your watch history**. Changing your mind about a rating is not
a claim that you never saw it.

---

## Your ratings

`GET /api/v1/users/me/ratings`

Returns rated films, most recently rated first, capped at 200 for now. Each entry
carries the title, poster path and release date alongside the rating, so a list
renders without a request per row.

---

# Recommendations

`GET /api/v1/recommendations?count=10`

Requires `Authorization: Bearer {token}`. Returns films the user has not seen,
ranked against their viewing and rating history
([ADR-0008](adr/0008-recommendations-from-tmdb-relatedness.md)).

```json
{
  "recommendations": [
    {
      "movieId": "0199...",
      "title": "A Bronx Tale",
      "posterPath": "/abc.jpg",
      "releaseDate": "1993-09-29",
      "runtime": 121,
      "averageRating": 7.9,
      "genres": ["Crime", "Drama"],
      "rank": 2,
      "confidence": "High",
      "reasons": ["Well regarded — 7.9 on TMDb"],
      "watch": {
        "streamingOn": ["Netflix"],
        "streamingElsewhere": [],
        "rentOrBuy": ["Apple TV"],
        "canStreamNow": true,
        "known": true,
        "link": "https://www.themoviedb.org/movie/550/watch?locale=US"
      }
    }
  ]
}
```

Candidates come from TMDb's relatedness, seeded from the user's best-rated films
across the range of genres they watch. Scoring, filtering and explanation are
ours.

**There is no match percentage, deliberately.** The scores behind a list sit
within a few points of each other, and publishing them would imply a precision the
model does not have. `rank` and `reasons` are the parts that mean something.

`confidence` reflects how much history stands behind the judgement, including
whether the user has watched anything in that film's genres — a thousand ratings
say nothing useful about a documentary if none of them are documentaries.

**An empty list is a real answer**, returned when someone has rated nothing highly
enough to reason from. Falling back to whatever is popular would be a different
product wearing this one's clothes.

**Only films worth an evening are returned**
([ADR-0009](adr/0009-recommend-only-films-worth-an-evening.md)): at least 6.5 on
TMDb, at least 5,000 votes, and already released. Applied as a floor before
ranking rather than as a penalty within it, so a weak film cannot win on other
grounds. A shorter list of good films beats a full one with duds in it.

**No single genre may fill a response.** A library that is 40% drama otherwise
produces a list of twelve dramas — honest scores, useless list. A cap trades a
little fidelity for a list worth scanning.

**`watch` says where the signed-in viewer can see it**
([ADR-0010](adr/0010-streaming-availability.md)), in their region and against the
services they have said they subscribe to.

- `streamingOn` — services they pay for that carry it. They can press play.
- `streamingElsewhere` — carried somewhere they have not said they subscribe to,
  which includes free and ad-supported services needing no subscription.
- `rentOrBuy` — available for money on top. **Never described as streaming.**
- `known: false` — nobody has looked it up. That is not the same as unavailable,
  and clients must not present it as such: TMDb's provider data is not exhaustive.

Availability **ranks** rather than filters. A film you can watch tonight is
promoted, one unavailable in your region is nudged down, and neither is hidden —
a great film you would have to rent is still worth knowing about, and filtering
would empty the page for anyone with one subscription.

---

## Recommendation Feedback

Not built. ADR-0008 defers the feedback *model*; every recommendation served is
already recorded, so the data to build one is accumulating.

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

Letterboxd has **no public API**, so the only import path is the user's CSV
export. See [the matching spike](spikes/letterboxd-tmdb-matching.md) for why this
is asynchronous and how reliably a row resolves to a film.

## Import

`POST /api/v1/import/letterboxd`

`multipart/form-data` with a `file` field holding `watched.csv`, `ratings.csv` or
`diary.csv`. Requires `Authorization: Bearer {token}`.

The file is parsed immediately and then queued
([ADR-0007](adr/0007-database-backed-import-jobs.md)) — a malformed CSV fails here
rather than two minutes later in a job status. **The file itself is not stored.**

`202 Accepted`, with a `Location` header pointing at the status endpoint:

```json
{
  "id": "0199...",
  "status": "Pending",
  "totalItems": 796,
  "matchedItems": 0,
  "ambiguousItems": 0,
  "unresolvedItems": 0,
  "skippedRows": 3,
  "failureReason": null,
  "createdAt": "2026-09-11T12:00:00+00:00",
  "completedAt": null
}
```

| Status | When |
|---|---|
| `400` | Empty file, not a `.csv`, larger than 5 MB, over 20,000 rows, or containing no films. |
| `401` | Not signed in. |

`skippedRows` counts rows the export contained that carried no title. They are
reported rather than dropped silently: a user who exported 800 films and imported
790 should be told which number is which.

---

## Import Status

`GET /api/v1/import/{jobId}`

Returns the same body as above, for polling. Requires
`Authorization: Bearer {token}`.

| `status` | Meaning |
|---|---|
| `Pending` | Queued, not started |
| `Running` | Being matched against TMDb |
| `Completed` | Finished — see the counts |
| `Failed` | Stopped for a systemic reason; `failureReason` says what |

A job belonging to somebody else returns `404`, not `403` — "that exists but is
not yours" would be an oracle for how many imports other people have run.

The counts split the outcome three ways, which is what the spike found the shape
of the problem to be: `matchedItems` resolved confidently, `ambiguousItems` need a
person to choose between candidates, and `unresolvedItems` found nothing
plausible. Roughly 7 rows per 800 are expected to need review.

---

## Rows needing review

`GET /api/v1/import/{jobId}/review`

Returns the rows the matcher declined to guess at, with the films each might
mean. Candidates were written into the catalogue while the import ran, so this
costs no TMDb calls.

```json
{
  "items": [
    {
      "itemId": "0199...",
      "name": "How to Make a Killing",
      "year": 2017,
      "rating": 4.0,
      "watchedOn": "2022-01-02",
      "status": "Ambiguous",
      "candidates": [
        {
          "movieId": "0199...",
          "tmdbId": 445226,
          "title": "How to Make a Killing",
          "releaseDate": "2017-03-10",
          "posterPath": "/abc.jpg",
          "averageRating": 6.4
        }
      ]
    }
  ]
}
```

`status` is `Ambiguous` when several films were plausible and nothing separated
them decisively, or `Unresolved` when nothing plausible was found — which often
means the row is television. **It is never reported as "not a film"**: that claim
needs positive evidence, and the absence of a match is not evidence.

---

## Resolve a row

`POST /api/v1/import/items/{itemId}/resolve`

```json
{ "movieId": "0199..." }
```

Applies the row's rating and viewing to the chosen film, exactly as a confident
match would have during the import, and records the match as `Manual`. Returns
the job's updated counts.

**Any film in the catalogue is accepted**, not only the candidates offered —
somebody who searched and found the right film should not be told their answer is
not on the list.

| Status | When |
|---|---|
| `400` | No film with that identifier is in the catalogue. |
| `401` | Not signed in. |
| `404` | No row awaiting review with that identifier belongs to you. |

---

## Dismiss a row

`POST /api/v1/import/items/{itemId}/dismiss`

Marks a row as deliberately not imported — a television series, or a film the
user does not want. Returns the job's updated counts.

The row is **dismissed, not deleted**: it stays as a record that the export
contained it and a person decided against it, which is the difference between
"we skipped 20 rows" and data quietly going missing.

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