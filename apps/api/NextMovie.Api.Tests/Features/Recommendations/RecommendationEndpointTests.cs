using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Recommendations;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Recommendations;

/// <summary>
/// Drives recommendations end to end against real PostgreSQL, with TMDb stubbed.
/// </summary>
/// <remarks>
/// The scoring itself is tested as pure logic elsewhere. What matters here is
/// everything around it: that films someone has already seen never appear, that
/// the reasons survive to the response, that a thin history produces an honest
/// empty answer rather than an invented one, and that what was served is written
/// down.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RecommendationEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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
            "delete from recommendation_events; delete from ratings; delete from watch_history; "
            + "delete from movie_genre; delete from movies; delete from refresh_tokens; delete from users;",
            Ct);
    }

    [Fact]
    public async Task Recommendations_require_a_signed_in_user()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/recommendations", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_user_with_no_history_gets_an_honest_empty_answer()
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/api/v1/recommendations", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecommendationsResponse>(Ct);
        Assert.NotNull(body);

        // Not a fallback to whatever is popular: that would be a different
        // product wearing this one's clothes.
        Assert.Empty(body.Recommendations);
        Assert.Equal(0, _tmdb.RelatedCalls);
        Assert.Equal(0, _tmdb.DiscoverCalls);
    }

    [Fact]
    public async Task Films_related_to_what_you_loved_are_recommended()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        var body = await ReadAsync(client);

        Assert.NotEmpty(body.Recommendations);
        Assert.Equal(1, _tmdb.RelatedCalls);

        // The second source: the best films in the genres they watch, which is
        // where great unseen films come from for somebody with a large library.
        Assert.True(_tmdb.DiscoverCalls > 0);

        var top = body.Recommendations[0];
        Assert.Equal(1, top.Rank);
        Assert.False(string.IsNullOrWhiteSpace(top.Title));
    }

    [Fact]
    public async Task Nothing_you_have_already_seen_is_recommended()
    {
        var (client, watchedTitle) = await SignedInWithHistoryAsync();

        var body = await ReadAsync(client);

        // The single most obvious way for a recommender to lose trust.
        Assert.DoesNotContain(body.Recommendations, film => film.Title == watchedTitle);
    }

    [Fact]
    public async Task Recommendations_carry_the_reasons_behind_them()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        var body = await ReadAsync(client);

        // At least one film should be able to say something about itself; a list
        // where nothing can be explained means the scoring found nothing.
        Assert.Contains(body.Recommendations, film => film.Reasons.Count > 0);
        Assert.All(body.Recommendations, film => Assert.Contains(
            film.Confidence,
            new[] { "Low", "Medium", "High" }));
    }

    [Fact]
    public async Task They_come_back_in_rank_order()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        var ranks = (await ReadAsync(client)).Recommendations.Select(film => film.Rank).ToList();

        Assert.Equal(Enumerable.Range(1, ranks.Count), ranks);
    }

    [Fact]
    public async Task No_single_genre_fills_the_list()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        var body = await ReadAsync(client, "?count=6");

        // A library that is mostly one genre otherwise produces six of the same
        // film: honest scores, useless list.
        var perGenre = body.Recommendations
            .SelectMany(film => film.Genres)
            .GroupBy(genre => genre)
            .Select(group => group.Count());

        Assert.All(perGenre, count => Assert.True(
            count <= body.Recommendations.Count,
            "no genre may exceed the number of recommendations"));
    }

    [Fact]
    public async Task What_was_served_is_written_down()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        var body = await ReadAsync(client);

        await using var db = postgres.CreateContext();
        var events = await db.RecommendationEvents.OrderBy(served => served.Rank).ToListAsync(Ct);

        // The only part of a future learned model that cannot be built later.
        Assert.Equal(body.Recommendations.Count, events.Count);
        Assert.Equal(1, events[0].Rank);
        Assert.Equal(body.Recommendations[0].MovieId, events[0].MovieId);

        // Stored as shown, because the model will change and an old event has to
        // stay interpretable.
        Assert.Equal(body.Recommendations[0].Reasons, events[0].Reasons);
    }

    [Fact]
    public async Task The_count_is_capped()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        var body = await ReadAsync(client, "?count=500");

        Assert.True(body.Recommendations.Count <= 30);
    }

    [Fact]
    public async Task A_poorly_rated_film_is_never_recommended()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        // TMDb offers something weak among the related films. It must not appear
        // however well its genres line up.
        _tmdb.Related[27205] =
        [
            .. _tmdb.Related[27205],
            new TmdbMovieDto
            {
                Id = 999_001,
                Title = "Direct To Video",
                ReleaseDate = "2016-01-01",
                VoteAverage = 5.4,
                VoteCount = 4_000,
                GenreIds = [SciFiGenre],
            },
        ];

        var body = await ReadAsync(client);

        Assert.DoesNotContain(body.Recommendations, film => film.Title == "Direct To Video");
    }

    [Fact]
    public async Task A_high_rating_from_too_few_people_is_never_recommended()
    {
        var (client, _) = await SignedInWithHistoryAsync();

        _tmdb.Related[27205] =
        [
            .. _tmdb.Related[27205],
            new TmdbMovieDto
            {
                Id = 999_002,
                Title = "Beloved By Its Twelve Fans",
                ReleaseDate = "2016-01-01",
                VoteAverage = 9.4,
                VoteCount = 12,
                GenreIds = [SciFiGenre],
            },
        ];

        var body = await ReadAsync(client);

        Assert.DoesNotContain(body.Recommendations, film => film.Title == "Beloved By Its Twelve Fans");
    }

    [Fact]
    public async Task A_tmdb_outage_produces_no_recommendations_rather_than_an_error()
    {
        var (client, _) = await SignedInWithHistoryAsync();
        _tmdb.Failure = new TmdbException("TMDb could not be reached.", statusCode: null);

        var response = await client.GetAsync("/api/v1/recommendations", Ct);

        // Degraded, not broken: a page that cannot suggest anything today is
        // better than one that returns a 500.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecommendationsResponse>(Ct);
        Assert.NotNull(body);
        Assert.Empty(body.Recommendations);
    }

    // --- setup ---

    private static async Task<RecommendationsResponse> ReadAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/v1/recommendations{query}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecommendationsResponse>(Ct);

        Assert.NotNull(body);
        return body;
    }

    /// <summary>Somebody who has loved one film, which becomes the seed.</summary>
    private async Task<(HttpClient Client, string WatchedTitle)> SignedInWithHistoryAsync()
    {
        var client = await SignedInClientAsync();

        await using var db = postgres.CreateContext();

        var user = await db.Users.FirstAsync(Ct);
        var genre = await db.Genres.FirstAsync(candidate => candidate.Id == SciFiGenre, Ct);

        var loved = new Movie { TmdbId = 27205, Title = "Inception", Runtime = 148, ReleaseDate = new DateOnly(2010, 7, 15) };
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

        // TMDb offers the seed itself back among the related films, which is
        // exactly the case that must never be recommended.
        _tmdb.Related[27205] =
        [
            Dto(27205, "Inception", "2010-07-15"),
            Dto(157336, "Interstellar", "2014-11-05"),
            Dto(335984, "Blade Runner 2049", "2017-10-04"),
            Dto(329865, "Arrival", "2016-11-10"),
        ];

        return (client, "Inception");
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

    private async Task<HttpClient> SignedInClientAsync()
    {
        using var registrar = _factory.CreateClient();

        var response = await registrar.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest("parker@example.com", "Parker", Password),
            Ct);

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);
        Assert.NotNull(session);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return client;
    }

    private sealed class StubTmdb : ITmdbClient
    {
        public Dictionary<int, TmdbMovieDto[]> Related { get; } = [];

        public int RelatedCalls { get; private set; }

        public TmdbException? Failure { get; set; }

        public Task<TmdbSearchResponse> GetRelatedMoviesAsync(int tmdbId, CancellationToken cancellationToken)
        {
            RelatedCalls++;

            if (Failure is not null)
            {
                return Task.FromException<TmdbSearchResponse>(Failure);
            }

            var results = Related.TryGetValue(tmdbId, out var films) ? films : [];

            return Task.FromResult(new TmdbSearchResponse
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = results.Length,
                Results = results,
            });
        }

        public Dictionary<int, TmdbMovieDto[]> BestInGenre { get; } = [];

        public int DiscoverCalls { get; private set; }

        public Task<TmdbSearchResponse> DiscoverBestInGenreAsync(
            int genreId,
            int minimumVotes,
            CancellationToken cancellationToken)
        {
            DiscoverCalls++;

            if (Failure is not null)
            {
                return Task.FromException<TmdbSearchResponse>(Failure);
            }

            var results = BestInGenre.TryGetValue(genreId, out var films) ? films : [];

            return Task.FromResult(new TmdbSearchResponse
            {
                Page = 1,
                TotalPages = 1,
                TotalResults = results.Length,
                Results = results,
            });
        }

        public Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests do not search.");

        public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests do not fetch details.");
    }
}
