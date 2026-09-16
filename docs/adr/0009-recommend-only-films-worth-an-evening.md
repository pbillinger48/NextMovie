# ADR-0009: Recommend only films worth an evening

- **Status:** Accepted
- **Date:** 2026-09-16
- **Amends:** [ADR-0008](0008-recommendations-from-tmdb-relatedness.md) — candidate sources and quality

## Context

ADR-0008's engine shipped and was tried against a real 766-rating library. The
verdict from the person whose library it was: *"These recommendations are
terrible. I want the very best movies recommended."*

The numbers agreed. It returned films rated 6.0, 6.3 and 6.8 on TMDb while the
catalogue held a hundred films rated above 8. Four causes, none of them subtle:

- **Quality barely counted.** A rating mapped onto 0–10 put a 6.0 at 0.60 and an
  8.5 at 0.85 — a quarter of a point on a weight of 0.15, which taste could
  outvote without effort.
- **There was no floor.** Nothing prevented a poor film being returned if its
  genres lined up.
- **Vote count was not stored**, so 9.0 from twelve people and 9.0 from twelve
  thousand were the same number.
- **The candidate pool could not contain the answer.** Relatedness asks "what else
  is like the films you loved". For somebody who has watched eight hundred films,
  most of that answer is films they have already seen: their entire pool held
  *one* unwatched film rated above 8.

The last is the important one. No amount of reweighting recommends a film that
was never a candidate.

## Decision

**Quality decides, taste chooses among good films, and nothing below the bar is
returned at all.**

- **A hard floor, applied before ranking:** at least 6.5 on TMDb, at least 5,000
  votes, and already released. Not a penalty — a penalty can be outvoted by
  anything else in the score, and that is exactly how the failures above
  happened.
- **Quality is measured across 6.5–8.5** rather than 0–10, so the difference
  between a good film and a great one is the whole scale instead of a fifth of it.
- **Weights rebalanced**: quality 0.45, quality-and-taste interaction 0.30, taste
  0.15, runtime and era 0.05 each. Popularity is removed entirely — it measures
  how many people saw a film, which is not a claim about whether they should have.
- **A second candidate source**: TMDb Discover, asked for the best-reviewed films
  in the genres this person watches, alongside ADR-0008's relatedness. Not a
  corpus — the same kind of live query, asked a better question.
- **Vote count is stored on films**, because a rating without it is not evidence.

### Why 5,000 votes

Two failures set it. A direct-to-video children's film reached a list at 7.5 from
205 votes, rated by exactly the audience that sought it out. Then recently
released films arrived at 9.1 from a thousand votes, carrying the enthusiasm of
people who had queued for them. Five thousand is where established reputations sit
— *Psycho* has eleven thousand, *City of God* eight — and where hype has not yet
reached.

## Consequences

**Positive**

- On the library that prompted this, the list went from a median TMDb rating of
  about 7.0 with a 6.0 at rank six, to a median of 8.4 with nothing below 8.1:
  *City of God*, *Your Name*, *The Green Mile*, *One Flew Over the Cuckoo's Nest*,
  *Psycho*, *Howl's Moving Castle*, *Terminator 2*.
- Great films the user has not seen can now reach them at all, which relatedness
  alone could not manage for a large library.
- The reasons lead with quality, because quality is now what mostly decides the
  ranking — an explanation that opened with a genre would be describing the wrong
  thing.

**Negative / accepted costs**

- **Good films with smaller audiences are excluded.** A well-loved film with 3,000
  votes cannot be recommended. That is the deliberate price of asking for the best
  rather than the most agreeable, and it is the first thing to revisit if the
  lists start feeling narrow.
- **Recent films are effectively unrecommendable** until their vote count builds.
  Somebody wanting to know what is good *this month* is not served by this.
- **More TMDb calls per request** — up to five relatedness queries and five
  discovery queries. Still around a second, but the case for caching candidate
  pools is stronger than it was.
- The engine leans harder on TMDb's community rating as a proxy for quality. It is
  a decent one, and it is one number produced by a population with its own tastes.

## Alternatives considered

**Reweighting without a floor**
Push quality's weight higher and leave the ranking to sort it out. Rejected
because it is what the first version attempted in spirit: any weight can be
outvoted, and a diversified list will always find a slot to fill with the best of
a bad set. A floor cannot be argued with.

**Filtering on a curated list of acclaimed films**
An IMDb Top 250 or similar as the candidate set. Rejected as somebody else's
canon, and static: it recommends the same films to everyone, which is the failure
the taste profile exists to avoid.

**Ingesting a corpus**
Still the eventual answer if TMDb's queries prove limiting, and still rejected for
the reasons in ADR-0008 — it is a large piece of work whose value cannot be judged
until the simpler thing is shown to be insufficient. Discover gets most of the
benefit with none of the maintenance.
