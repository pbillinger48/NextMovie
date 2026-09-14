using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Features.Import;

/// <summary>
/// Claims import jobs and works them.
/// </summary>
/// <remarks>
/// Separate from the <see cref="ImportWorker"/> that calls it on a loop, so the
/// interesting behaviour — claiming, resolving, resuming — can be tested by
/// calling it directly rather than by starting a background service and hoping
/// about timing.
/// </remarks>
internal sealed class LetterboxdImportProcessor(
    NextMovieDbContext db,
    ITmdbClient tmdb,
    MovieCatalog catalog,
    UserLibrary library,
    TimeProvider time,
    ILogger<LetterboxdImportProcessor> logger)
{
    /// <summary>
    /// How many films are resolved against TMDb at once.
    /// </summary>
    /// <remarks>
    /// The spike measured 796 films in 32 seconds at this width — roughly 24
    /// requests a second against TMDb's ~50/s ceiling. Named here rather than
    /// discovered under load: raising it risks being rate-limited mid-import,
    /// and lowering it makes a large library take noticeably longer.
    /// </remarks>
    private const int TmdbConcurrency = 8;

    /// <summary>
    /// How many items are resolved and written per pass.
    /// </summary>
    /// <remarks>
    /// Bounds memory and gives the status endpoint something to report before the
    /// whole import finishes — a user watching a progress bar on a 3,000-film
    /// library should not see zero for two minutes.
    /// </remarks>
    private const int BatchSize = 50;

    /// <summary>
    /// After this long, a claimed job is assumed to belong to a worker that died.
    /// </summary>
    /// <remarks>
    /// Long enough that a slow but healthy import is never stolen from itself,
    /// short enough that a deploy does not strand somebody's import until
    /// somebody notices. Items carry their own status, so reclaiming resumes
    /// rather than restarts.
    /// </remarks>
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Consecutive TMDb failures before the whole job is called off.
    /// </summary>
    /// <remarks>
    /// One row TMDb cannot answer for is that row's problem. Ten in a row is
    /// TMDb being down, and grinding through three thousand of those to fail at
    /// the end helps nobody.
    /// <para>
    /// Counted while applying, not while resolving, so a batch already in flight
    /// finishes before the job gives up — an outage costs one batch of wasted
    /// requests rather than the whole library. That is the price of resolving a
    /// batch concurrently, and it is worth paying.
    /// </para>
    /// </remarks>
    private const int ConsecutiveFailuresBeforeGivingUp = 10;

    /// <summary>
    /// Takes ownership of one waiting job, if there is one.
    /// </summary>
    /// <remarks>
    /// <c>FOR UPDATE SKIP LOCKED</c> is what makes this safe to run from more
    /// than one process: two workers asking at the same moment get different
    /// jobs rather than the same one twice. There is one instance today; this
    /// costs a clause and means a second is a deployment decision rather than a
    /// correctness bug.
    /// </remarks>
    public async Task<ImportJob?> ClaimNextJobAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var staleBefore = now - StaleClaim;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var job = await db.ImportJobs
            .FromSql(
                $"""
                 SELECT * FROM import_jobs
                 WHERE status = 'Pending'
                    OR (status = 'Running' AND claimed_at < {staleBefore})
                 ORDER BY created_at
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
                 """)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return null;
        }

        if (job.Status == ImportJobStatus.Running)
        {
            logger.LogWarning(
                "Reclaiming import {JobId}, whose previous worker stopped at {ClaimedAt}",
                job.Id,
                job.ClaimedAt);
        }

        job.Status = ImportJobStatus.Running;
        job.ClaimedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return job;
    }

    /// <summary>Works a claimed job to completion, or fails it.</summary>
    public async Task ProcessAsync(ImportJob job, CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Only Pending items: an interrupted job resumes rather than
                // redoing work it already applied.
                var batch = await db.ImportItems
                    .Where(item => item.ImportJobId == job.Id && item.Status == ImportItemStatus.Pending)
                    .OrderBy(item => item.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

                if (batch.Count == 0)
                {
                    break;
                }

                var resolved = await ResolveAsync(batch, cancellationToken);

                foreach (var (item, candidates) in resolved)
                {
                    if (candidates is null)
                    {
                        consecutiveFailures++;

                        if (consecutiveFailures >= ConsecutiveFailuresBeforeGivingUp)
                        {
                            await FailAsync(job, "TMDb could not be reached.", cancellationToken);

                            return;
                        }

                        // Left Pending on purpose: this row was never answered
                        // for, so a later pass should try it again rather than
                        // recording a verdict nobody reached.
                        continue;
                    }

                    consecutiveFailures = 0;
                    await ApplyAsync(job, item, candidates.Candidates, candidates.Results, cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                // Shutting down mid-import. The job stays Running with its claim
                // timestamp; the stale-claim rule brings it back.
                logger.LogInformation("Import {JobId} interrupted; it will be resumed", job.Id);

                return;
            }

            job.Status = ImportJobStatus.Completed;
            job.CompletedAt = time.GetUtcNow();

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Import {JobId} finished: {Matched} matched, {Ambiguous} to review, {Unresolved} unresolved",
                job.Id,
                job.MatchedItems,
                job.AmbiguousItems,
                job.UnresolvedItems);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Import {JobId} failed", job.Id);

            await FailAsync(job, "The import stopped unexpectedly.", cancellationToken);
        }
    }

    /// <summary>
    /// Searches TMDb for every row in the batch, in parallel.
    /// </summary>
    /// <remarks>
    /// Reads only. Nothing here touches the database, because a
    /// <c>DbContext</c> is not thread-safe and the concurrency the spike measured
    /// is worth having: the writes that follow are sequential and fast, while
    /// these lookups are the slow part.
    /// <para>
    /// A null candidate list means TMDb did not answer, which is different from
    /// answering with nothing.
    /// </para>
    /// </remarks>
    private async Task<List<(ImportItem Item, ResolvedCandidates? Candidates)>> ResolveAsync(
        List<ImportItem> batch,
        CancellationToken cancellationToken)
    {
        var results = new (ImportItem Item, ResolvedCandidates? Candidates)[batch.Count];

        await Parallel.ForAsync(
            0,
            batch.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = TmdbConcurrency,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                var item = batch[index];

                try
                {
                    var search = await tmdb.SearchMoviesAsync(item.Name, page: 1, token);

                    var usable = search.Results
                        .Where(dto => dto.Id > 0 && !string.IsNullOrWhiteSpace(dto.Title))
                        .ToList();

                    results[index] = (item, new ResolvedCandidates(
                        [.. usable.Select(dto => new MatchCandidate(
                            dto.Id,
                            dto.Title!,
                            ReleaseYear(dto.ReleaseDate),
                            dto.VoteCount))],
                        usable));
                }
                catch (TmdbException exception)
                {
                    logger.LogWarning(
                        exception,
                        "TMDb search failed for import row {Name}; it will be retried",
                        item.Name);

                    results[index] = (item, null);
                }
            });

        return [.. results];
    }

    /// <summary>Records what the matcher decided, and applies it when it decided.</summary>
    private async Task ApplyAsync(
        ImportJob job,
        ImportItem item,
        IReadOnlyList<MatchCandidate> candidates,
        IReadOnlyList<TmdbMovieDto> searchResults,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var entry = new LetterboxdEntry(item.Name, item.Year, item.FilmUri, item.Rating, item.WatchedOn);
        var outcome = LetterboxdMatcher.Match(entry, candidates);

        item.ResolvedAt = now;

        switch (outcome)
        {
            case MatchOutcome.Matched matched:
                var movie = await StoreFilmAsync(matched.TmdbId, cancellationToken);

                if (movie is null)
                {
                    // TMDb offered a film the mapper will not accept. Rare, and
                    // treated as unresolved rather than crashing the import.
                    item.Status = ImportItemStatus.Unresolved;
                    job.UnresolvedItems++;

                    break;
                }

                await library.ImportAsync(
                    job.UserId,
                    movie.Id,
                    item.Rating,
                    item.WatchedOn,
                    now,
                    cancellationToken);

                item.Status = ImportItemStatus.Matched;
                item.MatchedMovieId = movie.Id;
                item.MatchMethod = matched.Method;
                job.MatchedItems++;

                break;

            case MatchOutcome.Ambiguous ambiguous:
                // Nothing is written to the user's library. A guess here would be
                // invisible and permanent; a row on a reconciliation screen is
                // neither.
                //
                // The candidates go into the catalogue now, while their TMDb data
                // is already in hand. Otherwise the review screen would have to
                // fetch each one later just to show a title — paying for network
                // calls we have already made.
                await StoreCandidatesAsync(searchResults, ambiguous.Candidates, cancellationToken);

                item.Status = ImportItemStatus.Ambiguous;
                item.CandidateTmdbIds = [.. ambiguous.Candidates.Select(candidate => candidate.TmdbId)];
                job.AmbiguousItems++;

                break;

            default:
                item.Status = ImportItemStatus.Unresolved;
                job.UnresolvedItems++;

                break;
        }
    }

    /// <summary>
    /// Puts the films a person will have to choose between into the catalogue.
    /// </summary>
    /// <remarks>
    /// Mapped from the search results already fetched, so this adds no TMDb
    /// traffic. They arrive with only the fields search carries — no runtime, no
    /// status — which is exactly what the details endpoint fills in later if
    /// anybody opens one.
    /// </remarks>
    private async Task StoreCandidatesAsync(
        IReadOnlyList<TmdbMovieDto> searchResults,
        IReadOnlyList<MatchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var wanted = candidates.Select(candidate => candidate.TmdbId).ToHashSet();

        var mapped = searchResults
            .Where(dto => wanted.Contains(dto.Id))
            .Select(TmdbMovieMapper.ToDomain)
            .OfType<MappedMovie>()
            .ToList();

        if (mapped.Count > 0)
        {
            await catalog.UpsertAsync(mapped, cancellationToken);
        }
    }

    /// <summary>
    /// Puts the matched film in our catalogue and returns our row for it.
    /// </summary>
    /// <remarks>
    /// Imports discover films nobody has searched for, so the catalogue grows the
    /// same way search makes it grow — through the same upsert, so there is one
    /// definition of how TMDb data becomes our data.
    /// </remarks>
    private async Task<Domain.Movie?> StoreFilmAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var details = await tmdb.GetMovieAsync(tmdbId, cancellationToken);
        var mapped = TmdbMovieMapper.ToDomain(details, time.GetUtcNow());

        if (mapped is null)
        {
            return null;
        }

        var stored = await catalog.UpsertAsync([mapped], cancellationToken);

        return stored.Count > 0 ? stored[0] : null;
    }

    private async Task FailAsync(ImportJob job, string reason, CancellationToken cancellationToken)
    {
        job.Status = ImportJobStatus.Failed;
        job.FailureReason = reason;
        job.CompletedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
    }

    private static int? ReleaseYear(string? releaseDate) =>
        releaseDate is { Length: >= 4 } && int.TryParse(releaseDate[..4], out var year) ? year : null;

    /// <summary>
    /// What TMDb offered for one row, in both the forms that are needed.
    /// </summary>
    /// <remarks>
    /// The matcher wants domain candidates; the catalogue wants the wire records
    /// they were built from. Carrying both avoids re-fetching films we already
    /// have in hand when a row turns out to need review.
    /// </remarks>
    private sealed record ResolvedCandidates(
        IReadOnlyList<MatchCandidate> Candidates,
        IReadOnlyList<TmdbMovieDto> Results);
}
