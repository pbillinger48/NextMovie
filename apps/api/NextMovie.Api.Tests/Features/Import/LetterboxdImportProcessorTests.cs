using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Features.Import;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Import;

/// <summary>
/// Drives the import worker's actual work against real PostgreSQL.
/// </summary>
/// <remarks>
/// Called directly rather than through the background service: claiming, stale
/// claims and resumption are the sharp edges ADR-0007 named, and testing them
/// through a polling loop would mean asserting on timing instead of behaviour.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class LetterboxdImportProcessorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    private async Task ClearAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "delete from import_items; delete from import_jobs; delete from ratings; "
            + "delete from watch_history; delete from movie_genre; delete from movies; "
            + "delete from refresh_tokens; delete from users;",
            Ct);
    }

    // --- claiming ---

    [Fact]
    public async Task Claims_a_waiting_job()
    {
        var (_, jobId) = await SeedJobAsync([Row("Inception", 2010)]);
        await using var context = NewContext();

        var claimed = await context.Processor.ClaimNextJobAsync(Ct);

        Assert.NotNull(claimed);
        Assert.Equal(jobId, claimed.Id);
        Assert.Equal(ImportJobStatus.Running, claimed.Status);
        Assert.Equal(Now, claimed.ClaimedAt);
    }

    [Fact]
    public async Task Claims_nothing_when_the_queue_is_empty()
    {
        await using var context = NewContext();

        Assert.Null(await context.Processor.ClaimNextJobAsync(Ct));
    }

    [Fact]
    public async Task Leaves_a_job_another_worker_is_already_running()
    {
        var (_, jobId) = await SeedJobAsync([Row("Inception", 2010)]);

        await using (var db = postgres.CreateContext())
        {
            var job = await db.ImportJobs.SingleAsync(job => job.Id == jobId, Ct);
            job.Status = ImportJobStatus.Running;
            job.ClaimedAt = Now.AddMinutes(-1);
            await db.SaveChangesAsync(Ct);
        }

        await using var context = NewContext();

        // A healthy import in progress must not be stolen from itself.
        Assert.Null(await context.Processor.ClaimNextJobAsync(Ct));
    }

    [Fact]
    public async Task Reclaims_a_job_whose_worker_died()
    {
        var (_, jobId) = await SeedJobAsync([Row("Inception", 2010)]);

        await using (var db = postgres.CreateContext())
        {
            var job = await db.ImportJobs.SingleAsync(job => job.Id == jobId, Ct);
            job.Status = ImportJobStatus.Running;
            job.ClaimedAt = Now.AddHours(-1);
            await db.SaveChangesAsync(Ct);
        }

        await using var context = NewContext();

        // Otherwise a deploy mid-import strands somebody's library on Running
        // until a person notices.
        var claimed = await context.Processor.ClaimNextJobAsync(Ct);

        Assert.NotNull(claimed);
        Assert.Equal(jobId, claimed.Id);
        Assert.Equal(Now, claimed.ClaimedAt);
    }

    // --- processing ---

    [Fact]
    public async Task A_matched_row_becomes_a_rating_and_a_viewing()
    {
        var (userId, _) = await SeedJobAsync([Row("Inception", 2010, rating: 4.5m, watchedOn: new DateOnly(2022, 1, 2))]);
        await using var context = NewContext();

        await ProcessAsync(context);

        await using var db = postgres.CreateContext();

        var rating = await db.Ratings.SingleAsync(Ct);
        Assert.Equal(userId, rating.UserId);
        Assert.Equal(4.5m, rating.Value);

        // Provenance is the whole point: this is what a re-import checks before
        // overwriting anything.
        Assert.Equal(LibrarySource.LetterboxdImport, rating.Source);

        var viewing = await db.WatchHistory.SingleAsync(Ct);
        Assert.Equal(new DateOnly(2022, 1, 2), viewing.WatchedOn);

        // Imports discover films nobody searched for, so the catalogue grows the
        // same way search makes it grow.
        var movie = await db.Movies.SingleAsync(Ct);
        Assert.Equal(27205, movie.TmdbId);

        var job = await db.ImportJobs.SingleAsync(Ct);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal(1, job.MatchedItems);
        Assert.NotNull(job.CompletedAt);

        var item = await db.ImportItems.SingleAsync(Ct);
        Assert.Equal(ImportItemStatus.Matched, item.Status);
        Assert.Equal(MatchMethod.Exact, item.MatchMethod);
        Assert.Equal(movie.Id, item.MatchedMovieId);
    }

    [Fact]
    public async Task An_ambiguous_row_writes_nothing_and_keeps_its_candidates()
    {
        await SeedJobAsync([Row("Close Call", 2010, rating: 4m)]);

        await using var context = NewContext(tmdb => tmdb.Results["Close Call"] =
        [
            Dto(1, "Close Call", "2010-01-01", votes: 720),
            Dto(2, "Close Call", "2010-01-01", votes: 399),
        ]);

        await ProcessAsync(context);

        await using var db = postgres.CreateContext();

        // A guess would be invisible and permanent. Nothing reaches the user's
        // library until a person chooses.
        Assert.Equal(0, await db.Ratings.CountAsync(Ct));
        Assert.Equal(0, await db.WatchHistory.CountAsync(Ct));

        var item = await db.ImportItems.SingleAsync(Ct);
        Assert.Equal(ImportItemStatus.Ambiguous, item.Status);

        // Stored so the reconciliation screen can offer the same choice without
        // searching TMDb again.
        Assert.Equal([1, 2], item.CandidateTmdbIds);

        Assert.Equal(1, (await db.ImportJobs.SingleAsync(Ct)).AmbiguousItems);
    }

    [Fact]
    public async Task A_row_tmdb_knows_nothing_about_is_unresolved()
    {
        await SeedJobAsync([Row("Some Television Series", 2019)]);

        await using var context = NewContext(tmdb => tmdb.Results["Some Television Series"] = []);

        await ProcessAsync(context);

        await using var db = postgres.CreateContext();

        var item = await db.ImportItems.SingleAsync(Ct);

        // Unresolved, and specifically not "not a film" — that claim needs
        // positive evidence, not the absence of a match.
        Assert.Equal(ImportItemStatus.Unresolved, item.Status);
        Assert.Equal(1, (await db.ImportJobs.SingleAsync(Ct)).UnresolvedItems);
    }

    [Fact]
    public async Task An_import_never_overwrites_a_rating_the_user_typed()
    {
        var (userId, _) = await SeedJobAsync([Row("Inception", 2010, rating: 2m)]);
        var movieId = await SeedMovieAsync();

        await using (var db = postgres.CreateContext())
        {
            var library = new UserLibrary(db, NullLogger<UserLibrary>.Instance);
            await library.RateAsync(userId, movieId, 5.0m, LibrarySource.Native, Now, Ct);
        }

        await using var context = NewContext();
        await ProcessAsync(context);

        await using var verify = postgres.CreateContext();
        var rating = await verify.Ratings.SingleAsync(Ct);

        // They changed their mind since exporting; the CSV is the stale copy.
        Assert.Equal(5.0m, rating.Value);
        Assert.Equal(LibrarySource.Native, rating.Source);
    }

    [Fact]
    public async Task Importing_the_same_export_twice_does_not_duplicate_viewings()
    {
        var watched = new DateOnly(2022, 1, 2);
        await SeedJobAsync([Row("Inception", 2010, rating: 4m, watchedOn: watched)]);

        await using (var first = NewContext())
        {
            await ProcessAsync(first);
        }

        await SeedJobAsync([Row("Inception", 2010, rating: 4m, watchedOn: watched)], email: null);

        await using (var second = NewContext())
        {
            await ProcessAsync(second);
        }

        await using var db = postgres.CreateContext();

        // The film URI makes a re-import idempotent; a second viewing on the same
        // date would turn every re-import into a phantom rewatch.
        Assert.Equal(1, await db.WatchHistory.CountAsync(Ct));
        Assert.Equal(1, await db.Ratings.CountAsync(Ct));
    }

    [Fact]
    public async Task Listing_the_same_film_in_two_exports_does_not_invent_a_rewatch()
    {
        // watched.csv says when the film was added to the list; ratings.csv says
        // when it was rated. Same film, different dates, one viewing — importing
        // both is how 49 phantom rewatches appeared on a real library.
        await SeedJobAsync([Row("Inception", 2010, watchedOn: new DateOnly(2022, 1, 2))]);

        await using (var first = NewContext())
        {
            await ProcessAsync(first);
        }

        await SeedJobAsync(
            [Row("Inception", 2010, rating: 4.5m, watchedOn: new DateOnly(2025, 7, 11))],
            email: null);

        await using (var second = NewContext())
        {
            await ProcessAsync(second);
        }

        await using var db = postgres.CreateContext();

        Assert.Equal(1, await db.WatchHistory.CountAsync(Ct));

        // The rating from the second file still lands — the point is not to
        // ignore the row, only to stop it inventing a viewing.
        Assert.Equal(4.5m, (await db.Ratings.SingleAsync(Ct)).Value);
    }

    [Fact]
    public async Task A_diary_export_can_still_record_a_genuine_rewatch()
    {
        // diary.csv is a log of viewings, one row each. A film watched twice is
        // two rows, and both belong in the history.
        await SeedJobAsync(
        [
            Row("Inception", 2010, watchedOn: new DateOnly(2022, 1, 2), isLoggedViewing: true),
            Row("Inception", 2010, watchedOn: new DateOnly(2025, 7, 11), isLoggedViewing: true),
        ]);

        await using var context = NewContext();
        await ProcessAsync(context);

        await using var db = postgres.CreateContext();

        Assert.Equal(2, await db.WatchHistory.CountAsync(Ct));
    }

    [Fact]
    public async Task A_diary_export_still_does_not_duplicate_the_same_viewing()
    {
        var watched = new DateOnly(2022, 1, 2);

        await SeedJobAsync([Row("Inception", 2010, watchedOn: watched, isLoggedViewing: true)]);

        await using (var first = NewContext())
        {
            await ProcessAsync(first);
        }

        await SeedJobAsync([Row("Inception", 2010, watchedOn: watched, isLoggedViewing: true)], email: null);

        await using (var second = NewContext())
        {
            await ProcessAsync(second);
        }

        await using var db = postgres.CreateContext();

        Assert.Equal(1, await db.WatchHistory.CountAsync(Ct));
    }

    [Fact]
    public async Task A_diary_date_replaces_what_a_list_only_guessed_at()
    {
        // watched.csv gives the date the row was created; diary.csv gives the day
        // the film was actually seen. They describe one viewing, so the diary
        // upgrades the list row rather than sitting beside it.
        await SeedJobAsync([Row("Inception", 2010, watchedOn: new DateOnly(2022, 1, 2))]);

        await using (var list = NewContext())
        {
            await ProcessAsync(list);
        }

        await SeedJobAsync(
            [Row("Inception", 2010, watchedOn: new DateOnly(2024, 4, 28), isLoggedViewing: true)],
            email: null);

        await using (var diary = NewContext())
        {
            await ProcessAsync(diary);
        }

        await using var db = postgres.CreateContext();

        var viewing = await db.WatchHistory.SingleAsync(Ct);
        Assert.Equal(new DateOnly(2024, 4, 28), viewing.WatchedOn);
        Assert.True(viewing.IsLoggedViewing);
    }

    [Fact]
    public async Task A_list_adds_nothing_to_a_film_the_diary_already_recorded()
    {
        // The same two files in the other order. Importing watched.csv after
        // diary.csv must not add a second viewing either.
        await SeedJobAsync(
            [Row("Inception", 2010, watchedOn: new DateOnly(2024, 4, 28), isLoggedViewing: true)]);

        await using (var diary = NewContext())
        {
            await ProcessAsync(diary);
        }

        await SeedJobAsync([Row("Inception", 2010, watchedOn: new DateOnly(2022, 1, 2))], email: null);

        await using (var list = NewContext())
        {
            await ProcessAsync(list);
        }

        await using var db = postgres.CreateContext();

        var viewing = await db.WatchHistory.SingleAsync(Ct);
        Assert.Equal(new DateOnly(2024, 4, 28), viewing.WatchedOn);
    }

    [Fact]
    public async Task A_second_diary_entry_still_records_a_real_rewatch()
    {
        // One list row, then two diary entries: the first upgrades, the second is
        // a genuine rewatch and must land.
        await SeedJobAsync([Row("Inception", 2010, watchedOn: new DateOnly(2022, 1, 2))]);

        await using (var list = NewContext())
        {
            await ProcessAsync(list);
        }

        await SeedJobAsync(
        [
            Row("Inception", 2010, watchedOn: new DateOnly(2024, 4, 28), isLoggedViewing: true),
            Row("Inception", 2010, watchedOn: new DateOnly(2025, 7, 11), isLoggedViewing: true),
        ],
            email: null);

        await using (var diary = NewContext())
        {
            await ProcessAsync(diary);
        }

        await using var db = postgres.CreateContext();

        var dates = await db.WatchHistory.Select(entry => entry.WatchedOn).ToListAsync(Ct);

        Assert.Equal(2, dates.Count);
        Assert.Contains(new DateOnly(2024, 4, 28), dates);
        Assert.Contains(new DateOnly(2025, 7, 11), dates);
    }

    [Fact]
    public async Task A_rewatch_survives_a_diary_date_matching_the_list_date()
    {
        // The list row's date often equals the first diary entry's date, because
        // that is the day the film was added. If the duplicate check treats the
        // list row as a logged viewing, the first diary entry is skipped, the
        // list row stays unclaimed, and the second entry consumes it as an
        // upgrade — silently losing a genuine rewatch. Three films in a real
        // library did exactly this.
        var sameDay = new DateOnly(2022, 1, 12);

        await SeedJobAsync([Row("Inception", 2010, watchedOn: sameDay)]);

        await using (var list = NewContext())
        {
            await ProcessAsync(list);
        }

        await SeedJobAsync(
        [
            Row("Inception", 2010, watchedOn: sameDay, isLoggedViewing: true),
            Row("Inception", 2010, watchedOn: new DateOnly(2022, 1, 15), isLoggedViewing: true),
        ],
            email: null);

        await using (var diary = NewContext())
        {
            await ProcessAsync(diary);
        }

        await using var db = postgres.CreateContext();
        var dates = await db.WatchHistory.Select(entry => entry.WatchedOn).ToListAsync(Ct);

        Assert.Equal(2, dates.Count);
        Assert.Contains(sameDay, dates);
        Assert.Contains(new DateOnly(2022, 1, 15), dates);
    }

    [Fact]
    public async Task A_resumed_job_does_not_redo_work_it_already_applied()
    {
        await SeedJobAsync([Row("Inception", 2010), Row("Arrival", 2016)]);

        await using (var db = postgres.CreateContext())
        {
            var done = await db.ImportItems.FirstAsync(item => item.Name == "Inception", Ct);
            done.Status = ImportItemStatus.Matched;
            await db.SaveChangesAsync(Ct);
        }

        await using var context = NewContext();
        await ProcessAsync(context);

        // Only the outstanding row was looked up. Items carry their own status
        // precisely so an interrupted import resumes rather than restarts.
        Assert.Equal(["Arrival"], context.Tmdb.Searched);
    }

    [Fact]
    public async Task A_tmdb_outage_fails_the_job_and_leaves_its_rows_for_later()
    {
        // More than one batch, which is what makes the last assertion mean
        // something: a batch is resolved in full before any verdict is applied,
        // so "gave up early" can only be observed across batches.
        await SeedJobAsync([.. Enumerable.Range(0, 120).Select(i => Row($"Film {i}", 2010))]);

        await using var context = NewContext(tmdb => tmdb.Failure =
            new TmdbException("TMDb could not be reached.", statusCode: null));

        await ProcessAsync(context);

        await using var db = postgres.CreateContext();

        var job = await db.ImportJobs.SingleAsync(Ct);
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.NotNull(job.FailureReason);

        // Left Pending, not marked unresolved: nobody ever answered for these
        // rows, so recording a verdict would be inventing one. A later pass
        // reclaims the job and tries them again.
        Assert.True(await db.ImportItems.AllAsync(item => item.Status == ImportItemStatus.Pending, Ct));

        // It stopped after the first batch rather than sending every row into a
        // dead upstream. A whole batch is wasted, which is the cost of resolving
        // a batch concurrently; 120 rows would not be.
        Assert.InRange(context.Tmdb.Searched.Count, 1, 50);
    }

    // --- helpers ---

    private static LetterboxdEntry Row(
        string name,
        int? year,
        decimal? rating = null,
        DateOnly? watchedOn = null,
        bool isLoggedViewing = false) =>
        new(name, year, $"https://boxd.it/{name.GetHashCode():x}", rating, watchedOn, isLoggedViewing);

    private async Task<(Guid UserId, Guid JobId)> SeedJobAsync(
        IReadOnlyList<LetterboxdEntry> rows,
        string? email = "parker@example.com")
    {
        await using var db = postgres.CreateContext();

        var userId = await db.Users.Select(user => user.Id).FirstOrDefaultAsync(Ct);

        if (userId == Guid.Empty)
        {
            var user = new User { Email = email ?? "parker@example.com", DisplayName = "Parker" };
            db.Users.Add(user);
            await db.SaveChangesAsync(Ct);
            userId = user.Id;
        }

        var job = new ImportJob
        {
            UserId = userId,
            TotalItems = rows.Count,
            SkippedRows = 0,
            CreatedAt = Now,
        };

        foreach (var row in rows)
        {
            job.Items.Add(new ImportItem
            {
                ImportJobId = job.Id,
                Name = row.Name,
                Year = row.Year,
                FilmUri = row.FilmUri,
                Rating = row.Rating,
                WatchedOn = row.WatchedOn,
                IsLoggedViewing = row.IsLoggedViewing,
            });
        }

        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(Ct);

        return (userId, job.Id);
    }

    private async Task<Guid> SeedMovieAsync()
    {
        await using var db = postgres.CreateContext();

        var movie = new Movie { TmdbId = 27205, Title = "Inception" };
        db.Movies.Add(movie);
        await db.SaveChangesAsync(Ct);

        return movie.Id;
    }

    private static async Task ProcessAsync(ProcessorContext context)
    {
        var job = await context.Processor.ClaimNextJobAsync(Ct);

        Assert.NotNull(job);

        await context.Processor.ProcessAsync(job, Ct);
    }

    private ProcessorContext NewContext(Action<StubTmdb>? configure = null)
    {
        var db = postgres.CreateContext();
        var tmdb = new StubTmdb();
        configure?.Invoke(tmdb);

        var processor = new LetterboxdImportProcessor(
            db,
            tmdb,
            new MovieCatalog(db, NullLogger<MovieCatalog>.Instance),
            new UserLibrary(db, NullLogger<UserLibrary>.Instance),
            new FixedTimeProvider(Now),
            NullLogger<LetterboxdImportProcessor>.Instance);

        return new ProcessorContext(db, tmdb, processor);
    }

    private sealed record ProcessorContext(
        NextMovieDbContext Db,
        StubTmdb Tmdb,
        LetterboxdImportProcessor Processor) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static TmdbMovieDto Dto(int id, string title, string releaseDate, int votes = 5_000) => new()
    {
        Id = id,
        Title = title,
        ReleaseDate = releaseDate,
        VoteCount = votes,
        VoteAverage = 8.0,
    };

    private sealed class StubTmdb : StubTmdbBase
    {
        public StubTmdb()
        {
            Results["Inception"] = [Dto(27205, "Inception", "2010-07-15")];
            Results["Arrival"] = [Dto(329865, "Arrival", "2016-11-10")];
        }
    }

    private abstract class StubTmdbBase : ITmdbClient
    {
        public Dictionary<string, TmdbMovieDto[]> Results { get; } = [];

        public List<string> Searched { get; } = [];

        public TmdbException? Failure { get; set; }

        public Task<TmdbProviderListResponse> GetProvidersAsync(string region, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests do not ask for the service catalogue.");

        public Task<TmdbWatchProvidersResponse> GetWatchProvidersAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests do not ask about availability.");

        public Task<TmdbSearchResponse> GetRelatedMoviesAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests do not ask for related films.");

        public Task<TmdbSearchResponse> DiscoverBestInGenreAsync(
            int genreId,
            int minimumVotes,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests do not discover films.");

        public Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken)
        {
            lock (Searched)
            {
                Searched.Add(title);
            }

            if (Failure is not null)
            {
                return Task.FromException<TmdbSearchResponse>(Failure);
            }

            var results = Results.TryGetValue(title, out var matches) ? matches : [];

            return Task.FromResult(new TmdbSearchResponse
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = results.Length,
                Results = results,
            });
        }

        public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException<TmdbMovieDetailsResponse>(Failure);
            }

            var dto = Results.Values.SelectMany(r => r).FirstOrDefault(r => r.Id == tmdbId);

            return Task.FromResult(new TmdbMovieDetailsResponse
            {
                Id = tmdbId,
                Title = dto?.Title ?? "Unknown",
                ReleaseDate = dto?.ReleaseDate,
                Runtime = 148,
                VoteCount = dto?.VoteCount ?? 0,
                VoteAverage = dto?.VoteAverage,
                Status = "Released",
            });
        }
    }
}
