using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Recommendations;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Recommendations;

/// <summary>
/// Tests that the engine's trace reports what actually happened.
/// </summary>
/// <remarks>
/// An instrument that silently reports zeros is worse than no instrument: it
/// reads as a finding. The offline evaluator decides whether a genre was never
/// sourced or was sourced and outranked purely from these numbers, and that
/// distinction has already sent one branch in the wrong direction.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RecommendationTraceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const int SciFiGenre = 878;

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    private NextMovieApiFactory _factory = null!;
    private StubTmdb _tmdb = null!;

    public Task InitializeAsync()
    {
        _tmdb = new StubTmdb();
        _factory = new NextMovieApiFactory(postgres.ConnectionString) { Tmdb = _tmdb };

        return ClearAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await ClearAsync();
    }

    private async Task ClearAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "delete from recommendation_responses; delete from recommendation_events; "
            + "delete from availability_offers; delete from movie_availability; "
            + "delete from user_streaming_providers; delete from streaming_providers; "
            + "delete from ratings; delete from watch_history; "
            + "delete from movie_genre; delete from movies; delete from refresh_tokens; delete from users;",
            Ct);
    }

    [Fact]
    public async Task The_trace_records_how_the_pool_was_assembled()
    {
        var userId = await SignedInWithHistoryAsync();
        var trace = new RecommendationTrace();

        var recommendations = await RecommendAsync(userId, count: 3, trace);

        Assert.NotEmpty(recommendations);

        // Each of these is a distinct claim the evaluator makes decisions on.
        Assert.NotEmpty(trace.SeedTmdbIds);
        Assert.NotEmpty(trace.DiscoveryGenres);
        Assert.True(trace.Fetched > 0, "nothing was recorded as fetched");
        Assert.NotEmpty(trace.Contending);
    }

    [Fact]
    public async Task The_contending_pool_carries_the_genres_of_the_films_in_it()
    {
        var userId = await SignedInWithHistoryAsync();
        var trace = new RecommendationTrace();

        await RecommendAsync(userId, count: 3, trace);

        // The evaluator counts genre membership across this to decide whether a
        // taste was outranked or never sourced. Empty genre lists would report
        // "none of the pool was on taste" for every taste there is.
        Assert.All(trace.Contending, film => Assert.Contains(SciFiGenre, film.GenreIds));
    }

    [Fact]
    public async Task Every_contending_film_carries_the_score_it_lost_or_won_with()
    {
        var userId = await SignedInWithHistoryAsync();
        var trace = new RecommendationTrace();

        await RecommendAsync(userId, count: 3, trace);

        // Without the components, the only possible next step after "ten musicals
        // competed and none were shown" is a guess about which weight is wrong.
        Assert.All(trace.Contending, film =>
        {
            Assert.InRange(film.Score, 0, 1);
            Assert.NotNull(film.Breakdown);
        });

        // Recorded in the order they were ranked, which is what makes "the best
        // one that lost" a meaningful thing to ask for.
        Assert.Equal(
            trace.Contending.Select(film => film.Score).OrderByDescending(score => score),
            trace.Contending.Select(film => film.Score));
    }

    [Fact]
    public async Task Seen_films_are_counted_out_before_the_pool_is_judged()
    {
        var userId = await SignedInWithHistoryAsync();
        var trace = new RecommendationTrace();

        await RecommendAsync(userId, count: 3, trace);

        // The seed film comes back among its own relatives, and it has been
        // watched. Unseen must therefore be strictly smaller than Fetched — if
        // they matched, the exclusion step was not being measured at all.
        Assert.True(
            trace.Unseen < trace.Fetched,
            $"expected exclusions to reduce {trace.Fetched} candidates, got {trace.Unseen}");

        // Nothing survives the quality floor that was already filtered out by it.
        Assert.True(trace.Contending.Count <= trace.Unseen);
    }

    [Fact]
    public async Task Recommending_without_a_trace_behaves_identically()
    {
        var userId = await SignedInWithHistoryAsync();

        var traced = await RecommendAsync(userId, count: 3, new RecommendationTrace());
        var untraced = await RecommendAsync(userId, count: 3, trace: null);

        // The trace is a diagnostic, not an input. If asking for one changed the
        // answer, every number it reported would be about a different run than
        // the one that shipped.
        Assert.Equal(
            traced.Select(film => film.Movie.Id),
            untraced.Select(film => film.Movie.Id));
    }

    // --- helpers ---

    private async Task<IReadOnlyList<Recommendation>> RecommendAsync(
        Guid userId,
        int count,
        RecommendationTrace? trace)
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RecommendationEngine>();

        return await engine.RecommendAsync(userId, count, Ct, trace);
    }

    private async Task<Guid> SignedInWithHistoryAsync()
    {
        using var registrar = _factory.CreateClient();

        var response = await registrar.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest("parker@example.com", "Parker", Password),
            Ct);

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);
        Assert.NotNull(session);

        await using var db = postgres.CreateContext();

        var user = await db.Users.FirstAsync(Ct);
        var genre = await db.Genres.FirstAsync(candidate => candidate.Id == SciFiGenre, Ct);

        var loved = new Movie
        {
            TmdbId = 27205,
            Title = "Inception",
            Runtime = 148,
            ReleaseDate = new DateOnly(2010, 7, 15),
        };

        loved.Genres.Add(genre);
        db.Movies.Add(loved);

        db.Ratings.Add(new Rating
        {
            UserId = user.Id,
            MovieId = loved.Id,
            Value = 5.0m,
            Source = LibrarySource.Native,
        });

        db.WatchHistory.Add(new WatchHistoryEntry
        {
            UserId = user.Id,
            MovieId = loved.Id,
            Source = LibrarySource.Native,
        });

        await db.SaveChangesAsync(Ct);

        // The seed returns among its own relatives, which is what makes the
        // exclusion step observable.
        _tmdb.Related[27205] =
        [
            Dto(27205, "Inception", "2010-07-15"),
            Dto(157336, "Interstellar", "2014-11-05"),
            Dto(335984, "Blade Runner 2049", "2017-10-04"),
            Dto(329865, "Arrival", "2016-11-10"),
        ];

        return user.Id;
    }

    private static TmdbMovieDto Dto(int id, string title, string releaseDate) => new()
    {
        Id = id,
        Title = title,
        ReleaseDate = releaseDate,
        VoteAverage = 8.0,
        VoteCount = 12_000,
        Popularity = 90,
        GenreIds = [SciFiGenre],
    };

    private sealed class StubTmdb : ITmdbClient
    {
        public Dictionary<int, TmdbMovieDto[]> Related { get; } = [];

        public Task<TmdbSearchResponse> GetRelatedMoviesAsync(int tmdbId, CancellationToken cancellationToken) =>
            Task.FromResult(Page(Related.GetValueOrDefault(tmdbId, [])));

        public Task<TmdbSearchResponse> DiscoverBestInGenreAsync(
            int genreId,
            int minimumVotes,
            CancellationToken cancellationToken) =>
            Task.FromResult(Page([]));

        public Task<TmdbWatchProvidersResponse> GetWatchProvidersAsync(int tmdbId, CancellationToken cancellationToken) =>
            Task.FromResult(new TmdbWatchProvidersResponse
            {
                Id = tmdbId,
                Results = new Dictionary<string, TmdbRegionProviders>(),
            });

        public Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbProviderListResponse> GetProvidersAsync(string region, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static TmdbSearchResponse Page(TmdbMovieDto[] results) => new()
        {
            Page = 1,
            TotalPages = 1,
            TotalResults = results.Length,
            Results = results,
        };
    }
}
