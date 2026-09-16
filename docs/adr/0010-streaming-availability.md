# ADR-0010: Streaming availability, per region, cached daily

- **Status:** Accepted
- **Date:** 2026-09-16

## Context

NextMovie exists to find *"the best movie you haven't seen that you can stream
right now"*. It currently does the first two thirds. ADR-0008 deferred
availability deliberately — it needed a provider integration, a region model and a
subscriptions concept, and blocking recommendations behind all three would have
meant neither shipping. Recommendations now work, so the last clause is what is
left.

Three things make this harder than it looks.

**Availability is regional, and the schema has no notion of region.** A film on
Netflix in the United States is frequently not on Netflix in the United Kingdom.
`CLAUDE.md` has flagged this gap since before there was code.

**Availability is the most perishable data in the product.** Films leave services
monthly. A cached answer is wrong sooner than anything else we store, and a
recommendation that sends somebody to a service the film left last week is worse
than saying nothing.

**"Streaming" is several different things.** TMDb distinguishes subscription,
free-with-ads, rent and buy. Treating a £13.99 purchase as "you can watch this
tonight" would be a lie the product's own sentence does not support.

## Decision

**Availability is fetched from TMDb per film and region, cached for a day, and
used to rank rather than to exclude.**

### Where it comes from

TMDb's `/movie/{id}/watch/providers`, which is JustWatch data under the hood. No
second vendor, no second bill, and it arrives through the client and the
anti-corruption boundary that already exist.

### Region

A new `Region` on the user — an ISO 3166-1 alpha-2 code, chosen in settings,
defaulting to `US`. Stored on the user rather than inferred per request: a VPN or
a holiday should not silently change what a person is told they can watch.

Availability rows are keyed on `(movie, region)`, because that is the grain the
data actually has.

### Freshness

Cached with a `RefreshedAt`, re-fetched when older than 24 hours, read-through on
the request that needs it — the same pattern `GetMovieDetails` already uses. A
list of twelve films costs at most twelve TMDb calls and usually far fewer.

Wrong for at most a day. Catalogues change at roughly that granularity, and the
alternative — live fetching every time — doubles the cost of every recommendation
to buy hours of freshness nobody will notice.

### Subscriptions

The user ticks the services they pay for, from the providers TMDb lists for their
region. Explicit rather than inferred: guessing from viewing history means
guessing from where films are available *now* rather than when they were watched,
and a wrong guess is invisible to the person it is wrong about.

### How it is used

**Ranking, and labelling — not filtering.**

- A film on a service the user subscribes to gets a substantial boost, and is
  labelled with that service.
- A film available to rent or buy is shown and labelled as such.
- A film unavailable in their region is shown and labelled as such.

Filtering was the obvious reading of the product sentence and is rejected: with
one or two subscriptions the list collapses to nothing, and hiding an outstanding
film because it costs £3.49 serves nobody. Ranking keeps the promise for the
common case; labelling keeps it honest for the rest.

**Only subscription, free and ad-supported count as "stream right now".** Rent and
buy are shown, and never described as streaming.

## Consequences

**Positive**

- The product can finally answer its own sentence.
- The region model is where it belongs — on the data that is actually regional —
  rather than assumed away.
- Recommendations degrade rather than empty: someone with no subscriptions still
  gets good films, labelled honestly.
- Availability is stored per film, so it serves the film page and any later
  watchlist without another design.

**Negative / accepted costs**

- **A cached answer is sometimes wrong**, and this is the data where being wrong
  is most annoying. A day is a deliberate trade, not an oversight; the fix if it
  bites is a shorter window, at proportionate cost.
- **Subscriptions go stale** when somebody cancels and does not update their
  settings, and the product will confidently tell them a film is on a service they
  no longer have.
- **More TMDb calls on the recommendation path**, on top of ADR-0009's discovery
  queries. Caching candidate pools is now clearly the next performance question.
- **TMDb's provider data is not exhaustive** — smaller and regional services are
  patchy — so "not available" sometimes means "not known to TMDb". The wording
  should reflect that rather than asserting absence.
- A `user_streaming_providers` table and a settings screen are real work before a
  single recommendation changes.

## Alternatives considered

**Hard filtering to what the user can stream**
The literal reading of the product's promise, and the cleanest possible statement
of it. Rejected because it fails exactly when it matters: a person with one
subscription gets an empty page, and the honest response to "nothing on Netflix
tonight" is not silence.

**Annotating without ranking**
Show availability as information and leave the ordering alone. Rejected because
the top recommendation would routinely be something the person cannot watch, which
is the complaint this feature exists to answer.

**Inferring subscriptions from viewing history**
No setup, and appealing for that reason. Rejected because it infers from today's
availability what was true when somebody watched a film years ago, and because a
wrong inference is silent — the user never sees the assumption to correct it.

**A background job refreshing the whole catalogue**
Requests would never wait. Rejected as mostly wasted work: availability for
eleven hundred films, of which a dozen are ever recommended, is thousands of calls
per cycle to keep data fresh that nobody reads.
