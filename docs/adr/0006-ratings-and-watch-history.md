# ADR-0006: Ratings and watch history

- **Status:** Proposed
- **Date:** 2026-09-10

## Context

Phase 4 is the Letterboxd import, and it has nowhere to put anything: neither
`ratings` nor `watch_history` exists. The obvious move — build the import and
create the tables it needs along the way — is the one to avoid. It would mean the
shape of our ratings is decided by the shape of somebody else's CSV, and the
[Letterboxd spike](../spikes/letterboxd-tmdb-matching.md) is explicit that
Letterboxd is *one input path among several*: users will rate films natively, and
a re-import must never overwrite what someone typed by hand.

So the schema is decided first, on its own terms, with native rating as a
first-class writer rather than a derivative of import. The import then lands on a
model that already has a second consumer, which is what makes "source-agnostic"
a property of the design rather than an intention.

Three facts from the spike constrain this:

- **Ratings arrive on a 0.5–5.0 half-star scale** from Letterboxd, and that scale
  must be converted at the boundary rather than leaking inward — the same rule
  `TmdbMovieMapper` already follows.
- **Provenance is mandatory.** Without it, re-import cannot distinguish an
  imported rating from a hand-entered one.
- **Viewings repeat.** The diary export records rewatches with dates; an opinion
  does not repeat in the same way.

[database.md](../database.md) sketches both entities but gives `Source` only to
`WatchHistory`, which its own implementation notes already flag as a defect.

## Decision

**Two tables, and a half-star scale stored exactly.**

### `watch_history` — an append-only log of viewings

One row per viewing. A film watched three times has three rows.

- `UserId`, `MovieId`, `WatchedAt` (**nullable**), `Source`, `CreatedAt`.
- No uniqueness on `(UserId, MovieId)`: rewatches are the point.
- `WatchedAt` is nullable because "I have seen this, I do not remember when" is a
  real and common state — `ratings.csv` carries no date at all, and a user rating
  a film from memory has none either. A sentinel date would be a lie that later
  sorts wrongly.

### `ratings` — one current opinion per user per film

- `UserId`, `MovieId`, `Rating`, `Source`, `CreatedAt`, `UpdatedAt`.
- **Unique on `(UserId, MovieId)`.** Re-rating updates the row; the database
  enforces that a person holds one opinion of a film at a time.
- Unrating deletes the row. An absent rating and a rating of zero are different
  statements, and only one of them is representable.

### The scale is 0.5–5.0 in half-steps, stored as `numeric(2,1)`

- A check constraint pins it to the range **and** to half-steps, so 4.3 cannot
  exist.
- `numeric`, not a floating-point type: the values are exact decimals and will be
  grouped, averaged and compared by the recommendation engine. Binary floating
  point invites the class of bug where two equal ratings are not equal.
- Letterboxd's scale is *identical*, so import is a copy rather than a
  conversion — nothing is rounded away and a re-export would round-trip.
- **TMDb's 0–10 community rating is a different thing entirely** and stays where
  it is, on `Movie.AverageRating`. One is what the world thinks of a film; the
  other is what this user thinks. They are never mixed.

### `Source` on both tables

An enum persisted by name: `Native`, `LetterboxdImport`. It answers "where did
this come from", and it is what lets a re-import leave hand-entered data alone.

**Match provenance is not stored here.** The spike calls for recording *how* a
Letterboxd row was resolved to a film (`Exact`, `Subtitle`, `VoteCount`,
`Manual`) and how confident that match was. That belongs to the import's own
records, alongside the raw CSV row it came from — putting it on `ratings` would
mean every rating carries columns that are meaningless for the native path. The
import feature will define those tables; this ADR only guarantees `Source`, which
is what tells you whether to go looking for them.

### A rating implies a viewing

Rating a film that has no `watch_history` row creates one, with a null
`WatchedAt`. Users mean "I have seen this" when they rate something, and a
recommendation engine that kept suggesting a film someone had rated would be
obviously broken. This is the one part of this ADR not driven by the spike or by
an existing document, and it is the part most worth challenging.

## Consequences

**Positive**

- Native rating and Letterboxd import are peers writing the same tables through
  the same rules, so the import cannot quietly define the domain.
- Re-import is safe by construction: `Source` distinguishes what may be
  overwritten from what may not.
- Rewatches, and dateless viewings, are both representable without a sentinel.
- No lossy conversion anywhere on the Letterboxd path.

**Negative / accepted costs**

- **Two tables mean two writes** for the common case of rating something new, and
  a transaction around them. That is the price of modelling a repeatable event
  and a mutable opinion as the different things they are.
- "Has this user seen this film?" is a question about `watch_history`, not
  `ratings`, and code that forgets this will get subtly wrong answers. The
  rating-implies-viewing rule above is what keeps the two consistent, and it has
  to be enforced in one place rather than remembered at each call site.
- A half-star scale is not directly comparable to TMDb's 0–10. Any future feature
  wanting to compare them must convert deliberately, which is a feature, not an
  oversight.
- `numeric` is marginally more awkward than an integer in C# and in EF mappings.

## Alternatives considered

**One table with a nullable rating**
A single `user_movies` row holding watched-ness and an optional rating. Fewer
joins, one obvious place to ask what a user thinks of a film. Rejected because
rewatches have nowhere to go: either rows duplicate — and then it is ambiguous
which one carries the rating — or the dates in the diary export are discarded.

**Append-only events, with current state derived**
Every watch and every rating as an immutable event. The best audit trail, and it
makes idempotent re-import almost free. Rejected as more machinery than is
warranted today: every read of "what did they rate this" becomes an aggregation,
and nothing in the MVP needs rating history. Worth revisiting if a "your year in
film" style feature ever wants to show how an opinion changed.

**Integer 1–10, or whole stars 1–5**
1–10 lines up numerically with TMDb and avoids decimals, but doubles every
imported value, so the stored number stops being the number the user chose.
Whole stars are simplest and lossy: 3.5 and 4.0 collapse, and no re-import can
recover the difference. Both were rejected for the same underlying reason — they
discard or distort information at the boundary that the chosen scale keeps
exactly.
