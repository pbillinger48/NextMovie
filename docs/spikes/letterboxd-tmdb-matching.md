# Spike: matching Letterboxd exports to TMDb

- **Date:** 2026-09-05
- **Status:** Complete
- **Verdict:** Viable — build the import

---

## Why this spike existed

Letterboxd has **no public API**. The only import path is the user's CSV export,
and those files identify films by **title and year** — not by TMDb id:

```csv
Date,Name,Year,Letterboxd URI,Rating
2022-01-02,Don't Look Up,2021,https://boxd.it/o0Hc,4
```

Every rating NextMovie imports must therefore be resolved to a TMDb film by
fuzzy-matching a title string. If that matching were only ~85% reliable, it would
change the schema, add a reconciliation feature to the roadmap, and undermine the
recommendation engine that consumes the ratings. None of the planning documents
acknowledged this risk, so it was measured before anything was built on top of it.

**Question:** given `(Name, Year)` from an export, how reliably can we resolve a
TMDb film — and when we cannot, why not?

---

## Method

A throwaway script resolved every row of a real 796-film `watched.csv` against
TMDb `/search/movie`, classified each outcome, and wrote every non-exact case out
for manual inspection. Two passes: a naive baseline, then one with three
heuristics, to measure what each heuristic was actually worth.

The script was deliberately disposable and is **not** in this repository. Its
output is this document.

---

## Results

| | Count | Share |
|---|---:|---:|
| Export rows | 796 | |
| Not films (TV titles and episodes) | 20 | 2.5% |
| **Actual films** | **776** | |
| **Auto-resolved** | **769** | **99.1%** |
| Need human review | 7 | 0.9% |

Naive title+year matching alone reached **91.8%**. Three heuristics closed the gap:

| Heuristic | Films recovered |
|---|---:|
| `vote_count` tiebreak | 98 (12.3%) |
| Subtitle prefix match | 3 |
| Leading-article folding | ~2 |

---

## Findings

### 1. TMDb holds several films sharing a title and year

This is the single largest source of apparent failure. Alongside the film a user
means, TMDb carries obscure shorts, student films and documentaries with the same
name and year — `Aladdin 1992` has 3 entries, `Hercules 1997` has 4.

`vote_count` separates them decisively. The **tightest** margins observed were
better than 10:1:

```
Justice League  2017    13,905 votes  vs  1,161
X-Men           2000    12,474 votes  vs    246
Kung Fu Panda   2008    12,950 votes  vs    486
```

Every close call was inspected manually; no wrong picks were found. A tiebreak
that silently chooses the wrong film is worse than a visible failure, so the
threshold is deliberately conservative: **≥50 votes and ≥10× the runner-up**.
Cases below that bar (4 films, e.g. `How to Make a Killing` at 720 vs 399) are
genuinely too close to call and must ask the user rather than guess.

### 2. Letterboxd lets users log television, and 2.5% of this export is TV

TMDb's `/search/movie` can never match these. They are a **category error, not a
matching failure**, and counting them as failures makes the import look worse
than it is.

- 11 TV titles — *Squid Game*, *Chernobyl*, *WandaVision*, *Mare of Easttown*
- 10 TV episodes — 9 individual *Black Mirror* episodes, *Stranger Things 5: The Finale*

The import must surface these explicitly as *"skipped — not a film"*. Dropping
them silently makes users believe data was lost.

### 3. `Letterboxd URI` means different things in different files

| File | URI refers to |
|---|---|
| `watched.csv`, `ratings.csv`, `watchlist.csv` | the **film** — stable across exports |
| `diary.csv`, `reviews.csv` | the **individual log entry** — one per viewing |

```
Spider-Man: No Way Home
  watched.csv  ->  boxd.it/nwRw     film URI
  diary.csv    ->  boxd.it/fwqimp   entry URI, different
```

The film URI is a **stable natural key** and the right basis for idempotent
re-import. Diary and review rows must instead be joined on `(Name, Year)`.

### 4. Year drift by exactly one year is systematic

16 films were off by one — festival premiere versus wide release (`Kingsman`
2014/2015, `The Bikeriders` 2023/2024). Letterboxd tends to record the premiere,
TMDb the release. A **±1 year tolerance is required**, not optional.

### 5. TMDb release dates are not immutable

`Hamilton` is logged as 2020; TMDb now reports **2025** for the same film
(id 556574) following a re-release. Anything keyed on release year will rot.

### 6. Failure-explaining logic can hide real bugs

The spike's own "is it TV?" fallback mislabelled `Hamilton` as television, because
it only ran after the film match failed. It was caught only by verifying the
claim against TMDb directly. Any heuristic that explains away a failure needs the
same scrutiny as one that produces a match.

### 7. Rate limits are not a constraint, but latency is

796 films resolved in **32s** at 8 concurrent requests (~24 req/s against TMDb's
~50/s ceiling). A 3,000-film library is roughly 2 minutes — too slow for a
request/response cycle, which confirms the asynchronous import job already
sketched in [api.md](../api.md).

---

## Design implications

1. **Import is a background job** with a status endpoint. Confirmed by measurement,
   not assumed.
2. **A reconciliation flow is needed, but it is small** — ~7 films per 800, not 40.
   "Review 7 unmatched films" is a modest screen, not a major feature.
3. **Persist match provenance and confidence** per imported row (`Exact`,
   `Subtitle`, `VoteCount`, `Manual`) so a bad match is auditable later instead of
   being indistinguishable from a good one.
4. **Store the Letterboxd film URI** to make re-import idempotent.
5. **Non-film entries need a real status**, not a silent drop.
6. **Ratings must carry a `Source`.** Letterboxd is one input path among several;
   users will also rate films natively, and the two must be distinguishable so a
   re-import never overwrites a hand-entered rating. `database.md` gives
   `WatchHistory` a `Source` but omits it on `Rating` — fix that when `Rating` is
   built.
7. **Convert the rating scale at the boundary.** Letterboxd is 0.5–5.0 in
   half-steps. Normalise on the way in, exactly as `TmdbMovieMapper` does for
   TMDb, and never let the external scale reach the domain.

---

## Limitations

**This is one library.** The sample skews modern, popular and English-language,
which is the easy case for title matching. A user whose history is mostly
non-English, silent-era or festival-circuit films would likely match worse:
original-versus-translated titles are exactly where `(Name, Year)` matching
breaks down, and this export barely tested that path.

99.1% should therefore be read as a **best case**, not an expected floor. The
reconciliation flow should be built to handle materially worse ratios gracefully
rather than sized against this one result.
