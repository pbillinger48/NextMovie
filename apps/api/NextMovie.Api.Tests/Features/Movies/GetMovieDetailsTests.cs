using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Features.Movies;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Movies;

/// <summary>
/// Drives the film details endpoint against real PostgreSQL, with TMDb stubbed.
/// </summary>
/// <remarks>
/// The behaviour worth pinning down is when TMDb is consulted and what happens
/// when it cannot be: a catalogue that fails because a third party is down would
/// defeat the reason for keeping a catalogue at all.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class GetMovieDetailsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int TmdbId = 27205;

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    public Task InitializeAsync() => ClearMoviesAsync();

    public Task DisposeAsync() => ClearMoviesAsync();

    private async Task ClearMoviesAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("delete from movie_genre; delete from movies;", Ct);
    }

    [Fact]
    public async Task An_unknown_identifier_is_not_found()
    {
        var tmdb = new StubTmdb();
        using var factory = FactoryFor(tmdb);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/movies/{Guid.CreateVersion7()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Nothing to enrich, so nothing should have been asked of TMDb.
        Assert.Equal(0, tmdb.Calls);
    }

    [Fact]
    public async Task Something_that_is_not_an_identifier_is_not_found()
    {
        using var factory = FactoryFor(new StubTmdb());
        using var client = factory.CreateClient();

        // The route constraint rejects it before any handler runs, which keeps a
        // malformed id from reaching the database as a query.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/movies/not-a-guid", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_film_that_has_never_been_enriched_is_enriched_on_read()
    {
        var id = await SeedAsync(detailsRefreshedAt: null);
        var tmdb = new StubTmdb();
        using var factory = FactoryFor(tmdb);
        using var client = factory.CreateClient();

        var details = await ReadDetailsAsync(client, id);

        // Search cannot supply these two, so a film discovered by search has them
        // missing until exactly this moment.
        Assert.Equal(148, details.Runtime);
        Assert.Equal("Released", details.Status);
        Assert.Equal(1, tmdb.Calls);

        await using var db = postgres.CreateContext();
        Assert.NotNull((await db.Movies.SingleAsync(Ct)).DetailsRefreshedAt);
    }

    [Fact]
    public async Task Recently_enriched_details_are_served_without_calling_tmdb()
    {
        var id = await SeedAsync(detailsRefreshedAt: DateTimeOffset.UtcNow.AddHours(-1), runtime: 148);
        var tmdb = new StubTmdb();
        using var factory = FactoryFor(tmdb);
        using var client = factory.CreateClient();

        var details = await ReadDetailsAsync(client, id);

        Assert.Equal(148, details.Runtime);

        // The point of the freshness window: after the first fetch, the great
        // majority of reads never leave our database.
        Assert.Equal(0, tmdb.Calls);
    }

    [Fact]
    public async Task Stale_details_are_refreshed()
    {
        var id = await SeedAsync(detailsRefreshedAt: DateTimeOffset.UtcNow.AddDays(-30), runtime: 100);
        var tmdb = new StubTmdb();
        using var factory = FactoryFor(tmdb);
        using var client = factory.CreateClient();

        var details = await ReadDetailsAsync(client, id);

        Assert.Equal(1, tmdb.Calls);

        // Ratings and popularity drift; a month-old copy should not be served as
        // if it were current.
        Assert.Equal(148, details.Runtime);
    }

    [Fact]
    public async Task A_film_is_still_served_when_tmdb_is_unreachable()
    {
        var id = await SeedAsync(detailsRefreshedAt: null, runtime: null);
        var tmdb = new StubTmdb { Failure = new TmdbException("TMDb could not be reached.", statusCode: null) };
        using var factory = FactoryFor(tmdb);
        using var client = factory.CreateClient();

        var details = await ReadDetailsAsync(client, id);

        // Degraded, not failed. Refusing to show a film we already hold because a
        // third party is down would throw away the whole reason for holding it.
        Assert.Equal("Inception", details.Title);
        Assert.Null(details.Runtime);
    }

    [Fact]
    public async Task A_film_tmdb_no_longer_carries_is_still_served()
    {
        var id = await SeedAsync(detailsRefreshedAt: null);
        var tmdb = new StubTmdb
        {
            Failure = new TmdbException("TMDb returned 404.", HttpStatusCode.NotFound),
        };
        using var factory = FactoryFor(tmdb);
        using var client = factory.CreateClient();

        // Our copy outlives theirs: a film removed from TMDb does not vanish from
        // a catalogue people may have rated.
        Assert.Equal("Inception", (await ReadDetailsAsync(client, id)).Title);
    }

    [Fact]
    public async Task Unusable_details_leave_the_stored_copy_alone()
    {
        var id = await SeedAsync(detailsRefreshedAt: null);
        var tmdb = new StubTmdb { Response = new TmdbMovieDetailsResponse { Id = TmdbId, Title = null } };
        using var factory = FactoryFor(tmdb);
        using var client = factory.CreateClient();

        var details = await ReadDetailsAsync(client, id);

        // A response the mapper rejects must not overwrite a good row with
        // nothing.
        Assert.Equal("Inception", details.Title);

        await using var db = postgres.CreateContext();
        Assert.Null((await db.Movies.SingleAsync(Ct)).DetailsRefreshedAt);
    }

    [Fact]
    public async Task Genres_come_back_alphabetically()
    {
        var id = await SeedAsync(detailsRefreshedAt: null);
        using var factory = FactoryFor(new StubTmdb());
        using var client = factory.CreateClient();

        var details = await ReadDetailsAsync(client, id);

        Assert.Equal(["Action", "Science Fiction"], details.Genres);
    }

    private NextMovieApiFactory FactoryFor(ITmdbClient tmdb) =>
        new(postgres.ConnectionString) { Tmdb = tmdb };

    private async Task<Guid> SeedAsync(DateTimeOffset? detailsRefreshedAt, int? runtime = null)
    {
        await using var db = postgres.CreateContext();

        var movie = new Movie
        {
            TmdbId = TmdbId,
            Title = "Inception",
            Runtime = runtime,
            DetailsRefreshedAt = detailsRefreshedAt,
        };

        db.Movies.Add(movie);
        await db.SaveChangesAsync(Ct);

        return movie.Id;
    }

    private static async Task<MovieDetails> ReadDetailsAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/movies/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<MovieDetails>(Ct);

        Assert.NotNull(details);
        return details;
    }

    private sealed class StubTmdb : ITmdbClient
    {
        public int Calls { get; private set; }

        public TmdbException? Failure { get; init; }

        public TmdbMovieDetailsResponse Response { get; init; } = new()
        {
            Id = TmdbId,
            Title = "Inception",
            Overview = "A thief who steals corporate secrets.",
            ReleaseDate = "2010-07-15",
            Runtime = 148,
            VoteAverage = 8.4,
            VoteCount = 34000,
            OriginalLanguage = "en",
            Status = "Released",
            Genres =
            [
                new TmdbGenreDto { Id = 878, Name = "Science Fiction" },
                new TmdbGenreDto { Id = 28, Name = "Action" },
            ],
        };

        public Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests do not search.");

        public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
        {
            Calls++;

            return Failure is not null
                ? Task.FromException<TmdbMovieDetailsResponse>(Failure)
                : Task.FromResult(Response);
        }
    }
}
