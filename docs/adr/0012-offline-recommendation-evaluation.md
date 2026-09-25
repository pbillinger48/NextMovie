# ADR-0012: Evaluate recommendations offline, against a held-out library

- **Status:** Accepted
- **Date:** 2026-09-16

## Context

Every recommendation failure in this project has been found by a person reading
output, never by a test.

The taste profile that concluded its only user disliked animation passed its
tests. So did the scorer that returned films rated 6.0 while a hundred films
rated 8.0 or better sat unrecommended in the same catalogue. So did the import
that invented 297 rewatches. In each case the suite was green and the product was
wrong, and the fix came from running it against a real 796-film library and
squinting at the result.

That is not a testing gap that more unit tests close. **Unit tests answer "does
this compute what I said", and the open question is "is what I said any good".**

It is also the blocker on everything planned next. The scorer's weights were
chosen by hand and adjusted by eye; nobody can say whether the last adjustment
helped. And the intended move to a learned model is unstartable without it — you
cannot claim a model beats the current scorer with no way to compare them.

## Decision

**Recommendations are evaluated offline, by hiding films the user loved and
measuring whether the recommender finds them again.**

### The method

Take the films a user rated highly. Hide a deterministic, seeded fraction of them
— from ratings and watch history both — run the recommender against what remains,
and see where the hidden films land.

Seeded, so two runs of different code are comparable. An unseeded split would
make every improvement indistinguishable from a different shuffle.

### The metrics

| Metric | Question |
|---|---|
| **Recall@N** | What fraction of hidden films came back in the top N? |
| **MRR** | How high up? A film at rank 1 counts for more than one at rank 12. |
| **Hit rate** | What fraction of runs found anything at all? |

**Not precision.** A recommendation that is not a hidden film is not thereby
wrong — it is very often a good film the user has simply never seen, which is the
entire product. Precision would punish the recommender for working.

### Quality statistics, alongside recall

Median and minimum TMDb rating, films below the quality floor, distinct genres,
how many are streamable, and how many upstream calls the run cost.

These are not decoration. **The failure that actually happened — "these
recommendations are terrible" — was a quality failure, not a recall failure**, and
recall alone would not have caught it. A recommender that returns hidden films
*and* dross scores well on recall and is still bad.

### Where it runs

A console project, run by hand, against a real library and the real TMDb.

**Not a test.** It needs a populated database and a live upstream, and a test that
needs those is a test that fails in CI for reasons that have nothing to do with
the code. The suite stays fast, hermetic and trustworthy; this stays honest.

**The run is wrapped in a transaction that is always rolled back.** Hiding films
means deleting rows, and the recommender writes an impression for everything it
serves. Neither may survive: an evaluation that damages the library it evaluates
is worse than no evaluation, and impressions from a run nobody saw would poison
the interaction log that ADR-0011 exists to collect.

### What is testable stays testable

The metrics are a pure domain module with ordinary unit tests. The console project
is a shell that gathers rows, calls the engine and prints. **The arithmetic of
recall and MRR is exactly the kind of thing that is quietly wrong**, and it must
not be the one part of this with nothing checking it.

## Consequences

**Positive**

- A scoring change can be compared to the one before it, rather than argued about.
- The ML work becomes possible: a learned model has something to beat.
- The quality floor gets a standing alarm instead of an eyeball.
- The cost of a recommendation in upstream calls becomes visible, which is the
  measurement ADR-0010 asked for before caching candidate pools.

**Negative / accepted costs**

- **The number is a lower bound, not accuracy.** Most good recommendations are
  not hidden films and are invisible to recall. A rising number means improvement;
  a particular number means little on its own.
- **The hold-out set is biased.** A film someone rated is a film they chose to
  watch, and they chose it because something surfaced it — so popular and
  discoverable films are over-represented among the hidden ones. The evaluation
  therefore flatters a recommender that favours the popular, which is the exact
  failure mode ADR-0009 fought.
- **One library is not a dataset.** Every number here is n=1 until there are more
  users, and it should be read as a signal about this recommender for this person.
- **Each run costs real upstream calls** and takes real seconds. This is a tool
  for deciding, not something to run in a loop.
- Rolling back the transaction discards cached availability fetched during the
  run, so repeated runs re-fetch it.

## Amendment, 2026-09-22: a second mode

The first run of this harness reported recall 10.3% with **MRR 1.000** — in every
trial, the top recommendation was a held-out favourite. That is consistent with a
recommender that reads taste well, and equally consistent with one that returns
the canon to everybody, since most people have seen the canon. **Hold-out cannot
tell those apart**, and the ADR above predicted exactly that bias without
providing a way to resolve it.

So the harness gained a second mode. It builds readers with deliberately
contrasting tastes — each from one genre's worth of real loved films — asks the
engine what each should watch, and measures two things:

- **Overlap**, pairwise Jaccard across the lists. *A high number is bad.* Reported
  for the whole list and for the opening slots separately, because a list whose
  tail diverges and whose head does not is personalised where nobody looks.
- **On taste**, the share of each list carrying the genre its reader was built
  from. Overlap can only say two lists *differ*; this asks whether a list is
  *about* the reader it was made for. Two lists can be completely distinct and
  both wrong.

The second measure exists because the first was not enough. Overlap came back at
0.25 — the lists genuinely differ — while a reader built from nothing but musicals
was offered **no musicals at all**. Distinctness is not relevance, and only
reading the actual titles made that visible.

Each taste also reports its **sourcing chain**: whether the genre was queried at
all, and how many of its films survived the quality floor to compete for a slot.
Added after the first fix landed, because "not recommended" has two causes that
look identical from outside — never fetched, or fetched and outranked — and only
the first is a ceiling nothing downstream can lift. Inferring which from twelve
film titles produced the wrong answer twice.

**Unlike hold-out, this mode is not reproducible to the decimal.** Candidates are
fetched live and the run is rolled back, so nothing is cached between runs and
the figures move by a few points. Read it for its magnitude, not its precision.

## Alternatives considered

**Online A/B testing**
What a real recommender would do, and the only method that measures what people
actually want rather than what they once rated. Rejected on arithmetic: it needs
traffic this product does not have, and with one user every split has a sample
size of one.

**Precision@N, labelled by hand**
Ask the user to judge each of twelve recommendations. Rejected because it does not
scale past a few runs, and because the same list judged on two evenings gets two
answers — mood is a confound the method cannot separate from quality.

**Using `recommendation_responses` as ground truth**
The right answer eventually, and the reason ADR-0011 collects it: a dismissal is a
real negative label from the real user. Rejected *for now* only because the table
is days old and nearly empty. This ADR should be revisited once it is not.

**A test in the existing suite, marked to run only on demand**
No new project, and it lives beside the code it judges. Rejected because a test
that needs a populated database and a live third party is not a test, and marking
it skipped turns the suite into a place where some things run and some do not —
which is how a suite stops being believed.
