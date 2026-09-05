using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Persistence;
using NextMovie.Api.Infrastructure.Tmdb;

namespace NextMovie.Api.Tests.Infrastructure.Persistence;

/// <summary>
/// Tests the ingestion path against real PostgreSQL.
/// </summary>
/// <remarks>
/// This is the risky code in Step 7: it deduplicates against a unique constraint,
/// resolves reference data, and preserves an ordering the caller depends on. None
/// of that can be verified without a real engine.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MovieCatalogTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestTimeout();

    private static CancellationToken TestTimeout() =>
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    /// <summary>Leaves the films table empty before each test; seeded genres stay.</summary>
    public async Task InitializeAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("delete from movie_genre; delete from movies;", Ct);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MovieCatalog CreateCatalog(NextMovieDbContext db) =>
        new(db, NullLogger<MovieCatalog>.Instance);

    private static MappedMovie Mapped(int tmdbId, string title, params int[] genreIds) =>
        new(new Movie { TmdbId = tmdbId, Title = title }, genreIds);

    [Fact]
    public async Task Inserts_films_it_has_not_seen()
    {
        await using var db = postgres.CreateContext();

        var stored = await CreateCatalog(db).UpsertAsync(
            [Mapped(329865, "Arrival", 878, 18)],
            Ct);

        var movie = Assert.Single(stored);
        Assert.Equal(329865, movie.TmdbId);
        Assert.Equal("Arrival", movie.Title);
        Assert.NotEqual(Guid.Empty, movie.Id);

        Assert.Equal(
            ["Drama", "Science Fiction"],
            movie.Genres.Select(g => g.Name).Order());
    }

    [Fact]
    public async Task Refreshes_an_existing_film_instead_of_duplicating_it()
    {
        await using (var first = postgres.CreateContext())
        {
            await CreateCatalog(first).UpsertAsync([Mapped(329865, "Arrival")], Ct);
        }

        await using var second = postgres.CreateContext();
        await CreateCatalog(second).UpsertAsync([Mapped(329865, "Arrival (2016)")], Ct);

        await using var verify = postgres.CreateContext();
        var all = await verify.Movies.Where(m => m.TmdbId == 329865).ToListAsync(Ct);

        var movie = Assert.Single(all);
        Assert.Equal("Arrival (2016)", movie.Title);
    }

    [Fact]
    public async Task Preserves_the_surrogate_key_and_created_timestamp_across_refreshes()
    {
        Guid originalId;
        DateTimeOffset originalCreatedAt;

        await using (var first = postgres.CreateContext())
        {
            var inserted = await CreateCatalog(first).UpsertAsync([Mapped(329865, "Arrival")], Ct);
            originalId = inserted[0].Id;
            originalCreatedAt = inserted[0].CreatedAt;
        }

        await using var second = postgres.CreateContext();
        var refreshed = await CreateCatalog(second).UpsertAsync([Mapped(329865, "Arrival Remastered")], Ct);

        // Clients store our Id. If a refresh changed it, every saved reference
        // anywhere would silently break.
        Assert.Equal(originalId, refreshed[0].Id);
        Assert.Equal(
            originalCreatedAt.ToUnixTimeMilliseconds(),
            refreshed[0].CreatedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Returns_results_in_the_order_supplied()
    {
        await using var db = postgres.CreateContext();

        var stored = await CreateCatalog(db).UpsertAsync(
            [
                Mapped(3, "Third"),
                Mapped(1, "First"),
                Mapped(2, "Second"),
            ],
            Ct);

        // TMDb orders search results by relevance. Reading back in database order
        // would destroy the ranking, which is the most valuable part of a search.
        Assert.Equal([3, 1, 2], stored.Select(m => m.TmdbId));
    }

    [Fact]
    public async Task Collapses_a_film_repeated_within_one_batch()
    {
        await using var db = postgres.CreateContext();

        var stored = await CreateCatalog(db).UpsertAsync(
            [
                Mapped(329865, "Arrival"),
                Mapped(329865, "Arrival duplicate"),
            ],
            Ct);

        Assert.Single(stored);
    }

    [Fact]
    public async Task Skips_genres_it_does_not_recognise()
    {
        await using var db = postgres.CreateContext();

        // 999999 is not a TMDb genre we have seeded. Dropping it loses a little
        // metadata; failing the whole search would lose the film entirely.
        var stored = await CreateCatalog(db).UpsertAsync(
            [Mapped(329865, "Arrival", 878, 999999)],
            Ct);

        var movie = Assert.Single(stored);
        Assert.Equal(["Science Fiction"], movie.Genres.Select(g => g.Name));
    }

    [Fact]
    public async Task Handles_an_empty_batch()
    {
        await using var db = postgres.CreateContext();

        Assert.Empty(await CreateCatalog(db).UpsertAsync([], Ct));
    }

    [Fact]
    public async Task Survives_two_concurrent_upserts_of_the_same_film()
    {
        // Two users searching the same title at once both try to insert it. The
        // unique constraint lets exactly one win; the loser must treat that as
        // success rather than surfacing a 500.
        await using var a = postgres.CreateContext();
        await using var b = postgres.CreateContext();

        var results = await Task.WhenAll(
            CreateCatalog(a).UpsertAsync([Mapped(329865, "Arrival")], Ct),
            CreateCatalog(b).UpsertAsync([Mapped(329865, "Arrival")], Ct));

        Assert.All(results, r => Assert.Single(r));

        await using var verify = postgres.CreateContext();
        Assert.Equal(1, await verify.Movies.CountAsync(m => m.TmdbId == 329865, Ct));
    }
}
