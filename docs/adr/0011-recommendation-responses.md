# ADR-0011: Recommendation responses, and the watchlist as one of them

- **Status:** Accepted
- **Date:** 2026-09-16

## Context

`recommendation_events` records every impression: which film, at what rank, with
what score, what confidence, and the exact reasons shown (ADR-0008). It records
nothing about what the person did next. **The log holds the question and throws
away the answer.**

Three problems follow from that, and they are the same problem.

**Recommendations cannot learn.** `RecommendationEngine` excludes only what has
been watched or rated. A film shown and ignored is shown again tomorrow, and the
day after, indefinitely. The only way to stop seeing a film is to go and watch it,
which is a strange thing for a recommender to insist on.

**There is no watchlist.** `docs/requirements.md` and every user-flow document
promise one. It does not exist. "Save this for later" is the most basic thing a
discovery product does, and without it the answer to a good recommendation at
11pm on a Tuesday is to write the title on a piece of paper.

**The intended future model has no labels.** The plan is eventually to replace the
hand-tuned scorer with something learned. Impressions without outcomes are
unlabelled data. An impression paired with a response is a training row —
features on one side, an observed outcome on the other — and every day without one
is a day of usable signal discarded.

These are one feature. A response to a recommendation is simultaneously the
feedback signal, the exclusion rule, and the watchlist.

## Decision

**A user's response to a film is recorded once per film, and the watchlist is a
query over it.**

### The responses

| Response | Means | Where it goes |
|---|---|---|
| **Saved** | Want to watch this | `recommendation_responses` |
| **Not interested** | Stop showing me this | `recommendation_responses` |
| **Seen it** | I have already watched this | `watch_history` |

**"Seen it" is not a response.** It is a fact about viewing, and ADR-0006 already
says where viewings live. It writes a `WatchHistoryEntry` with
`Source = Native`, `WatchedOn = null` and `IsLoggedViewing = false` — precisely the
"I have seen this, I do not remember when" case that entity was built to hold.

Recording it as a third response kind would create two sources of truth for
"watched", and would leave a film the user has told us they have seen out of their
own history, their statistics, and the taste profile built from both. The button
sits beside the other two in the UI because that is where it is useful; that is a
question about the interface, not about the schema.

### One row per user and film

`(user_id, movie_id)` is unique. A person's current answer about a film is one
thing, and saving a film they had dismissed replaces the dismissal rather than
contradicting it.

The **watchlist is `response = Saved`**, ordered by when it was given. There is no
second table. Two tables would allow "saved" and "on the watchlist" to disagree,
and there is nothing a watchlist row needs that the response does not already
carry.

### All three exclude from future recommendations

Saved and dismissed films stop appearing — one because the decision is already
made, the other because that is the entire point. Seen-it films are excluded by
the watch-history rule that already exists.

### Attribution is resolved on the server

A response references the impression that prompted it, when there was one:
nullable, and looked up as the most recent `recommendation_event` for that user
and film. **The client does not send an event id.** A client-supplied identifier
could attribute a response to somebody else's impression, which would corrupt
exactly the data this exists to collect.

Responses also arrive from the film page, where there was no impression at all.
The column is nullable because the absence is real, not because it is convenient.

### Dismissal lasts until it is undone

Not time-decayed. A person who said they were not interested was making a
statement, and quietly re-showing the film in three months treats that statement
as noise. It is reversible instead: every response can be withdrawn, and dismissed
films remain reachable through search and the film page.

## Consequences

**Positive**

- Recommendations improve immediately in the crudest and most valuable way: they
  stop repeating themselves.
- The watchlist arrives for the cost of a query.
- `recommendation_events` becomes a labelled dataset. Features were already
  stored as-shown; outcomes now join them.
- The exclusion rule is one query over one table, not a union over three concepts.

**Negative / accepted costs**

- **"Not interested" is a blunt signal.** It conflates "wrong film", "wrong mood
  tonight", and "I saw the trailer and no". We are not distinguishing, because
  asking someone to explain a dismissal is how dismissals stop happening.
- **Aggressive dismissal shrinks an already thin candidate pool.** ADR-0009's
  discovery source keeps supplying new candidates, but a user who dismisses
  heavily will reach the end faster. Measurable, and a reason to widen sourcing
  rather than to soften the rule.
- **A saved film is a decision, not a viewing.** Nothing yet notices that a film
  sat on the watchlist for a year, and that silence is itself a signal we are
  choosing not to read for now.
- One more write on a page that was previously read-only, with the session and
  ownership checks that implies.

## Alternatives considered

**A separate watchlist table, and separate feedback**
The obvious decomposition, and how most codebases would arrive at it. Rejected
because the two would encode the same fact — "this person wants to watch this" —
in two places, free to disagree, with every reader obliged to consult both.

**Thumbs up and thumbs down**
Familiar, and a cleaner training label. Rejected because a thumb is an opinion
about a film nobody has seen, which is not a thing people can honestly give. Save
and dismiss are decisions about what to do next, which they can.

**Implicit feedback — clicks, dwell time, scroll depth**
What a large recommender would use, and it needs no buttons. Rejected because it
needs traffic this product does not have, and because a click is ambiguous in a
way a dismissal is not: opening a film page means interest or it means checking
whether you have already seen it.

**Time-decaying dismissals**
Re-show a dismissed film after some months on the theory that taste moves.
Rejected as guessing at a number with no evidence, and as overruling a person's
stated preference on a schedule they were never told about.

**Recording the response against the impression rather than the film**
Truer to the event log, and the natural shape for training data. Rejected because
every reader wants the current answer about a film — the exclusion rule, the
watchlist, the film page — and each would have to reduce a history of impressions
to it. The impression is referenced from the response instead, which keeps the
training pair without making every query reconstruct state.
