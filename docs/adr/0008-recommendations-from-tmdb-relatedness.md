# ADR-0008: Recommend from TMDb relatedness, scored against a computed taste profile

- **Status:** Accepted
- **Date:** 2026-09-14

## Context

[recommendation-engine.md](../recommendation-engine.md) is the most detailed
document in `docs/`, and the only one written entirely before there was a schema.
Read against what now exists — 938 films, 768 viewings and 766 ratings from a real
library — four things are true that it could not have known.

**There is no candidate pool.** The catalogue grows only through search and
import, so 768 of its 938 films are ones this user has already watched. The 170
that remain are leftovers from ambiguous matches. An engine built on this
catalogue would have almost nothing to recommend, and the doc's pipeline — which
starts by excluding watched films from the catalogue — assumes a corpus that our
design never creates.

**Most of the scoring model is not buildable.** Of the doc's eight weighted
factors, director similarity (20%), themes (15%), actor similarity (10%) and the
feedback model (15%) all need data we do not store: there are no people tables, no
keywords, and no feedback. Sixty percent of the weight is unreachable.

**The streaming filter cannot be built yet.** The pipeline puts availability
*before* scoring, and the product's one-sentence promise is "watch right now". We
have no provider data and, as `CLAUDE.md` notes, no region concept.

**A persisted taste profile is solving a problem we do not have.** The doc
precomputes it "rather than analyzing every rating during every request". With 766
ratings that analysis is one aggregate over a few hundred rows.

## Decision

**Candidates come from TMDb's relatedness. Ranking, filtering and explanation are
ours.**

```
your highest-rated films
        │  TMDb /movie/{id}/recommendations
        ▼
pooled candidates
        │  drop anything watched, rated, or dismissed
        ▼
scored against a taste profile computed from your own ratings
        │
        ▼
ranked, with the reasons that produced the ranking
```

- **Seeded from what you liked.** The user's top-rated films are the seeds; TMDb
  answers "what else is like this". That relatedness is computed from a user base
  orders of magnitude larger than ours will ever be, and it is not something we
  can outcompute from nineteen genre labels. Buying it and adding our own
  personalisation on top is the honest build-versus-buy line — the same line
  ADR-0003 drew around password hashing.
- **Scored with what we hold:** genre affinity derived from your ratings per
  genre, runtime fit and era fit derived from what you actually watch, plus TMDb's
  community rating and popularity as weak signals. Roughly 40% of the doc's model,
  and enough to rank defensibly and explain honestly.
- **The taste profile is computed per request, not stored.** Behind an interface,
  so caching it later is an implementation change rather than a redesign.
- **Every recommendation carries its reasons**, assembled from the scoring
  components that actually moved it — not prose generated after the fact. An
  explanation that does not correspond to the ranking is a lie with good manners.
- **Confidence reflects how much history backs the judgement**, which is the
  doc's intent and needs no data we lack.
- **The engine is a domain module**, `Domain/Recommendations`, not a feature
  slice. `CLAUDE.md` names it as the one deliberate exception, because it is
  cross-cutting by nature: the endpoint that serves it is a slice, the scoring is
  not.

**Explicitly out of the first version:** streaming availability, and therefore
"why now". Recommendations will answer "why this film" and say plainly that they
cannot yet say where to watch it. Availability needs watch-provider data, a region
concept and the user's own subscriptions — its own feature, and blocking
recommendations behind it means neither ships.

**Also out:** recommendation feedback. It is 15% of the doc's weights and needs a
model that `database.md` currently describes twice, in two incompatible shapes.
When it is built it gets one model and its own decision; until then, scoring does
not pretend to learn.

## Consequences

**Positive**

- Something demonstrable without an ingestion pipeline, a people schema, or a
  provider integration standing in front of it.
- The candidate set is always fresh, and is never limited to films someone
  happened to search for.
- Building on real signals first tells us which missing ones actually matter,
  rather than guessing. If genre affinity alone produces good rankings, director
  similarity may not be worth its schema.
- Explanations are derived from the ranking, so they cannot drift from it.

**Negative / accepted costs**

- **We depend on TMDb for candidate quality.** If their relatedness is poor for a
  film, ours is too, and we cannot fix it from here. A corpus of our own remains
  the escape hatch.
- **A recommendation request makes several TMDb calls**, so it is slower than
  scoring a local corpus and it fails when TMDb does. Caching candidate pools is
  the obvious mitigation and is deliberately not built yet.
- **A new user with few ratings gets weak recommendations**, because there are no
  good seeds. Confidence will say so, which is the honest response, but it does
  not solve it.
- **The product's promise is not fully kept.** "The best movie you haven't seen
  that you can stream right now" is still missing its last clause, and the UI must
  not imply otherwise.
- `recommendation-engine.md` now disagrees with the implementation in several
  places. This ADR wins, per the rule in the ADR index.

## Alternatives considered

**Ingest a corpus from TMDb Discover**
A background job pulling films by genre, year and popularity into the catalogue,
giving thousands of candidates under our control and no third-party call at
request time. Genuinely better in the long run, and the right answer if TMDb's
relatedness disappoints. Rejected for now because it is a large piece of work —
how much to pull, how to keep it fresh, how large the database becomes — all of it
before a single recommendation exists to judge.

**Build people and keywords first**
Cast, crew and TMDb keywords, then score with director and theme similarity as the
doc intends. The explanations would be markedly better: "you have rated four
Villeneuve films five stars" persuades where "this is science fiction" does not.
Rejected as sequencing, not as an idea — it is the most likely second version, and
building the scoring interface first means adding a signal later is additive.

**Build streaming availability first**
Defensible, since availability is in the product's one-line promise. Rejected
because it puts an integration, a region model and a subscriptions feature in
front of the thing the product exists to do, and a recommendation nobody can see
is worth less than one that cannot yet tell you where to watch it.

**Persist the taste profile**
What the doc specifies. Rejected as premature: it adds invalidation on every
rating, every import and every watch, to avoid an aggregate that costs
microseconds at this size. The interface leaves room to change our minds once
there is a measurement that says we should.
