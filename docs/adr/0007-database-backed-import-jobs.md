# ADR-0007: Import jobs live in the database and are worked in-process

- **Status:** Accepted
- **Date:** 2026-09-11

## Context

The Letterboxd import cannot run inside a request. The
[spike](../spikes/letterboxd-tmdb-matching.md) measured 796 films resolved in 32
seconds at 8 concurrent TMDb requests; a 3,000-film library is roughly two
minutes. That is not a request/response cycle, which is why
[api.md](../api.md) has always sketched an async import with a status endpoint.

So something has to run the work, something has to remember it, and something has
to answer "how is it going". Those are three questions with one honest answer
between them.

Two things are already decided and constrain this:

- **[ADR-0006](0006-ratings-and-watch-history.md)** gives every rating and viewing
  a `Source`, so an import can write alongside hand-entered data without
  destroying it.
- **The matching rules** are pure and already built, and they produce three
  outcomes: a confident match, an ambiguous one needing a person, and an
  unresolved row. The spike expects roughly 7 ambiguous rows per 800 films, so a
  reconciliation flow is a small screen rather than a feature — but the rows have
  to be stored somewhere for it to read.

## Decision

**The queue is a table, and an in-process `BackgroundService` works it.**

```
POST /api/v1/import/letterboxd
  parse the CSV synchronously        -> reject a malformed file immediately
  write import_items   (Pending)     -> one row per export row
  write import_jobs    (Pending)     -> one row per upload
  return 202 with the job id

BackgroundService
  claim one Pending job  (SELECT ... FOR UPDATE SKIP LOCKED)
  resolve each item against TMDb, bounded concurrency
  write ratings and watch history through UserLibrary
  update counts; mark Completed

GET /api/v1/import/{jobId}
  read the job row
```

- **`import_jobs`** holds status, counts and timings. The status endpoint needs
  this table whether or not it is also the queue, which is what makes the queue
  nearly free.
- **`import_items`** holds one row per line of the export: what Letterboxd said,
  what it resolved to, **how** it resolved (`MatchMethod`), and what the
  alternatives were when it did not. This is the spike's requirement that match
  provenance be auditable, and it is what the reconciliation screen reads.
- **The CSV is parsed synchronously, on upload.** A malformed file should fail
  while the user is still looking at the upload form, not two minutes later in a
  job status. The parse is milliseconds; only the TMDb lookups are slow.
- **The raw file is not stored.** Once parsed into items, it has no further use,
  and keeping a copy of a user's viewing history around is a liability rather than
  an asset.
- **Claiming uses `FOR UPDATE SKIP LOCKED`.** There is one instance today. This
  costs one clause and means a second instance is a deployment decision rather
  than a correctness bug.
- **A job is resumable.** Items carry their own status, so a process that dies
  halfway leaves completed items completed. A claimed job that has gone stale is
  reclaimed rather than abandoned, and re-resolving an already-resolved item is
  skipped rather than repeated.
- **Concurrency against TMDb is one named constant**, set to the 8 the spike
  measured against TMDb's roughly 50/s ceiling. A number chosen once, in public,
  rather than discovered under load.
- **Failure is per-item where it can be.** One row that TMDb cannot resolve does
  not fail the import; it lands as unresolved and the user sees it. A job fails
  only when something systemic does — the database, or TMDb being down entirely.

## Consequences

**Positive**

- No new dependency, and no second datastore. The thing that remembers jobs is the
  thing that already has to remember everything else.
- A restart is survivable, which an in-memory queue would not have been. That
  matters more than it sounds: an import is the single longest operation in the
  product, so it is the one most likely to be interrupted by a deploy.
- Job and item rows make the import inspectable in `psql` during development,
  which is exactly when it will be least trustworthy.
- Re-import is idempotent on the Letterboxd film URI, and `Source` keeps
  hand-entered ratings safe from it.

**Negative / accepted costs**

- **We are writing a small job runner**, and job runners have well-known sharp
  edges: claiming, stale claims, retry, poison rows. They are handled above and
  each needs testing deliberately rather than assuming.
- **Polling has a floor on latency.** A job may sit for a few seconds before it is
  claimed. For a two-minute import nobody will notice, but this is not a
  low-latency queue and should not become one.
- **The work runs in the API process**, so a large import competes with request
  handling for CPU and connections. Acceptable at this size; the moment it is not,
  the worker moves out — and because the queue is a table, moving it is a
  deployment change rather than a rewrite.
- A completed import leaves `import_items` behind indefinitely. That is
  deliberate for now, since it is the audit trail; it will eventually need a
  retention policy.

## Alternatives considered

**In-memory channel and a hosted service**
Enqueue the job id on upload and let a worker read the channel. The least code by
some margin, and no polling. Rejected on its failure mode: a restart mid-import
loses the in-flight work while the database still says `Running`, with nothing to
recover it, and a second instance would only ever process uploads it received
itself. The database table was needed anyway, so this saves less than it appears
to.

**Hangfire**
A real job framework — persistence, retry with backoff, a dashboard, and
scheduling we will eventually want for things like refreshing stale films.
Rejected for now because it brings roughly ten tables of its own into a schema
ADR-0003 deliberately kept clean, to solve reliability problems we have not yet
measured at a scale we have not yet reached. Worth revisiting when there is a
second kind of background work, at which point writing a second bespoke runner
would be the wrong answer.

**Doing it synchronously and streaming progress**
Keep the request open and report progress over SSE or a websocket. Tempting
because it removes the job table entirely. Rejected because a two-minute request
is hostage to every timeout between the browser and the API, and because closing
the laptop would abandon an import the user believes is running.
