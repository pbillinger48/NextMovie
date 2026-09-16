using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Domain.Recommendations;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Recommendations;
using NextMovie.Api.Features.Watchlist;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Recommendations;

/// <summary>
/// Drives the feedback loop end to end: responding, excluding, and the watchlist.
/// </summary>
/// <remarks>
/// The failures that matter here are quiet ones. A dismissed film that keeps
/// coming back makes the buttons decorative; a response attributed to the wrong
/// impression corrupts the training data this exists to collect; and one person's
/// answers leaking into another's watchlist is the ordinary shape of an
/// authorisation bug.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MovieResponseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const int SciFiGenre = 878;
    private const int Netflix = 8;

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

    // --- who may call ---

    [Fact]
    public async Task Responding_requires_a_signed_in_user()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/movies/{Guid.NewGuid()}/response",
            new RespondToMovieRequest("Saved"),
            Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_watchlist_requires_a_signed_in_user()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/users/me/watchlist", Ct)).StatusCode);
    }

    [Fact]
    public async Task One_persons_answers_are_invisible_to_another()
    {
        using var parker = await SignedInClientAsync("parker@example.com");
        var film = await SeedFilmAsync("Heat");
        await RespondAsync(parker, film, "Saved");

        using var someoneElse = await SignedInClientAsync("other@example.com");

        // The user comes from the token, never from the request. A watchlist that
        // showed somebody else's saves would be the ordinary shape of an
        // authorisation bug.
        Assert.Empty((await WatchlistAsync(someoneElse)).Films);
        Assert.Null((await StateAsync(someoneElse, film)).Response);
    }

    // --- recording a response ---

    [Fact]
    public async Task An_unknown_film_is_a_404()
    {
        using var client = await SignedInClientAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/movies/{Guid.NewGuid()}/response",
            new RespondToMovieRequest("Saved"),
            Ct);

        // A film nobody has searched for is genuinely not in the catalogue. The
        // foreign key would refuse it too, but as a 500 nobody can act on.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("maybe")]
    public async Task An_answer_we_do_not_understand_is_refused(string? answer)
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/movies/{film}/response",
            new RespondToMovieRequest(answer),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The 400 names what was allowed, rather than leaving the client to guess.
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("NotInterested", body);
    }

    [Fact]
    public async Task Saving_a_film_is_recorded()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        var state = await RespondAsync(client, film, "Saved");

        Assert.Equal("Saved", state.Response);
        Assert.False(state.Watched);
        Assert.NotNull(state.RespondedAt);
    }

    [Fact]
    public async Task Saving_twice_leaves_one_answer()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        await RespondAsync(client, film, "Saved");
        await RespondAsync(client, film, "Saved");

        // A person has one current answer about a film. PUT is idempotent because
        // the resource is singular, and a double-tapped button must not save twice.
        await using var db = postgres.CreateContext();
        Assert.Single(await db.RecommendationResponses.ToListAsync(Ct));
    }

    [Fact]
    public async Task Saving_a_film_you_dismissed_replaces_the_dismissal()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        await RespondAsync(client, film, "NotInterested");
        var state = await RespondAsync(client, film, "Saved");

        // Changing your mind is not a contradiction to be stored alongside the
        // old answer.
        Assert.Equal("Saved", state.Response);

        await using var db = postgres.CreateContext();
        Assert.Equal(ResponseKind.Saved, (await db.RecommendationResponses.SingleAsync(Ct)).Kind);
    }

    // --- seen it ---

    [Fact]
    public async Task Seen_it_is_recorded_as_a_viewing_not_a_response()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        var state = await RespondAsync(client, film, "Seen");

        Assert.True(state.Watched);

        // ADR-0011: "I have already watched this" is a fact about viewing, and
        // ADR-0006 says where viewings live. Storing it as a third response kind
        // would give "watched" two sources of truth and leave the film out of the
        // taste profile built from history.
        Assert.Null(state.Response);

        await using var db = postgres.CreateContext();
        Assert.Empty(await db.RecommendationResponses.ToListAsync(Ct));

        var viewing = await db.WatchHistory.SingleAsync(Ct);
        Assert.Equal(LibrarySource.Native, viewing.Source);

        // Null, not today. Saying you have seen a film says nothing about when.
        Assert.Null(viewing.WatchedOn);
        Assert.False(viewing.IsLoggedViewing);
    }

    [Fact]
    public async Task Seen_it_withdraws_an_earlier_answer()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        await RespondAsync(client, film, "Saved");
        await RespondAsync(client, film, "Seen");

        // Saving was a statement about a film they had not seen. It does not
        // survive finding out that they had — and a watched film sitting on the
        // watchlist would be a standing invitation to watch it again.
        Assert.Empty((await WatchlistAsync(client)).Films);
    }

    [Fact]
    public async Task Seen_it_twice_does_not_invent_a_rewatch()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        await RespondAsync(client, film, "Seen");
        await RespondAsync(client, film, "Seen");

        // Telling us you have seen something is not telling us you watched it
        // again.
        await using var db = postgres.CreateContext();
        Assert.Single(await db.WatchHistory.ToListAsync(Ct));
    }

    // --- withdrawing ---

    [Fact]
    public async Task Withdrawing_removes_the_answer()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");
        await RespondAsync(client, film, "Saved");

        var state = await WithdrawAsync(client, film);

        Assert.Null(state.Response);
        Assert.Empty((await WatchlistAsync(client)).Films);
    }

    [Fact]
    public async Task Withdrawing_nothing_succeeds()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        // The caller asked for there to be no response, and there is none.
        // Clients undo from a list that may already have moved on, and failing
        // them for succeeding would be a worse contract.
        Assert.Null((await WithdrawAsync(client, film)).Response);
    }

    [Fact]
    public async Task Withdrawing_does_not_un_watch_a_film()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");
        await RespondAsync(client, film, "Seen");

        var state = await WithdrawAsync(client, film);

        // Deleting viewings is a capability ADR-0006 does not cover. Silently
        // removing one here would make this endpoint destroy history it was never
        // asked about.
        Assert.True(state.Watched);

        await using var db = postgres.CreateContext();
        Assert.Single(await db.WatchHistory.ToListAsync(Ct));
    }

    // --- attribution ---

    [Fact]
    public async Task A_response_is_attributed_to_the_impression_that_prompted_it()
    {
        var client = await SignedInWithHistoryAsync();
        var shown = (await RecommendationsAsync(client)).Recommendations[0];

        await RespondAsync(client, shown.MovieId, "NotInterested");

        await using var db = postgres.CreateContext();
        var response = await db.RecommendationResponses.SingleAsync(Ct);
        var served = await db.RecommendationEvents.SingleAsync(
            shownEvent => shownEvent.MovieId == shown.MovieId, Ct);

        // The other half of a training row: the event holds the features exactly
        // as shown, the response holds the outcome.
        Assert.Equal(served.Id, response.RecommendationEventId);

        client.Dispose();
    }

    [Fact]
    public async Task A_response_to_a_film_never_recommended_has_nothing_to_attribute()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        await RespondAsync(client, film, "Saved");

        // Responses arrive from the film page too. The column is nullable because
        // the absence is real, not because it is convenient.
        await using var db = postgres.CreateContext();
        Assert.Null((await db.RecommendationResponses.SingleAsync(Ct)).RecommendationEventId);
    }

    // --- exclusion ---

    [Fact]
    public async Task A_dismissed_film_stops_being_recommended()
    {
        var client = await SignedInWithHistoryAsync();
        var before = await RecommendationsAsync(client);
        var dismissed = before.Recommendations[0];

        await RespondAsync(client, dismissed.MovieId, "NotInterested");

        var after = await RecommendationsAsync(client);

        // The entire point. Before this, the only way to stop seeing a film was
        // to go and watch it.
        Assert.DoesNotContain(after.Recommendations, film => film.MovieId == dismissed.MovieId);
        Assert.NotEmpty(after.Recommendations);

        client.Dispose();
    }

    [Fact]
    public async Task A_saved_film_stops_being_recommended()
    {
        var client = await SignedInWithHistoryAsync();
        var saved = (await RecommendationsAsync(client)).Recommendations[0];

        await RespondAsync(client, saved.MovieId, "Saved");

        // The decision is already made and the film is already waiting on a list.
        // Recommending it again is the product forgetting what it was told.
        Assert.DoesNotContain(
            (await RecommendationsAsync(client)).Recommendations,
            film => film.MovieId == saved.MovieId);

        client.Dispose();
    }

    // --- the watchlist ---

    [Fact]
    public async Task The_watchlist_is_empty_until_something_is_saved()
    {
        using var client = await SignedInClientAsync();

        Assert.Empty((await WatchlistAsync(client)).Films);
    }

    [Fact]
    public async Task Saved_films_appear_most_recent_first()
    {
        using var client = await SignedInClientAsync();
        var heat = await SeedFilmAsync("Heat");
        var arrival = await SeedFilmAsync("Arrival");

        await RespondAsync(client, heat, "Saved");
        await RespondAsync(client, arrival, "Saved");

        // Newest first, because a watchlist is read from the top and the thing
        // you just saved is the thing you were just thinking about.
        Assert.Equal(["Arrival", "Heat"], (await WatchlistAsync(client)).Films.Select(film => film.Title));
    }

    [Fact]
    public async Task Dismissed_films_are_not_on_the_watchlist()
    {
        using var client = await SignedInClientAsync();
        var film = await SeedFilmAsync("Heat");

        await RespondAsync(client, film, "NotInterested");

        // The watchlist is a query over responses rather than a table of its own,
        // so this is the one place the query could be wrong.
        Assert.Empty((await WatchlistAsync(client)).Films);
    }

    [Fact]
    public async Task The_watchlist_says_where_to_watch_things()
    {
        using var client = await SignedInClientAsync();
        await SubscribeToAsync(Netflix, "Netflix");

        var film = await SeedFilmAsync("Heat");
        _tmdb.Availability[await TmdbIdForAsync("Heat")] = new TmdbRegionProviders
        {
            Flatrate = [new TmdbProviderDto { ProviderId = Netflix, ProviderName = "Netflix" }],
        };

        await RespondAsync(client, film, "Saved");

        var saved = Assert.Single((await WatchlistAsync(client)).Films);

        // People open a watchlist asking "what can I watch tonight", not "what did
        // I mean to watch".
        Assert.Equal(["Netflix"], saved.Watch.StreamingOn);
        Assert.True(saved.Watch.CanStreamNow);
    }

    // --- helpers ---

    private static async Task<MovieResponseState> RespondAsync(HttpClient client, Guid movieId, string answer)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/v1/movies/{movieId}/response",
            new RespondToMovieRequest(answer),
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<MovieResponseState>(Ct);
        Assert.NotNull(state);

        return state;
    }

    private static async Task<MovieResponseState> WithdrawAsync(HttpClient client, Guid movieId)
    {
        var response = await client.DeleteAsync($"/api/v1/movies/{movieId}/response", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<MovieResponseState>(Ct);
        Assert.NotNull(state);

        return state;
    }

    private static async Task<MovieResponseState> StateAsync(HttpClient client, Guid movieId)
    {
        var response = await client.GetAsync($"/api/v1/movies/{movieId}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var details = await response.Content.ReadFromJsonAsync<NextMovie.Api.Features.Movies.MovieDetails>(Ct);
        Assert.NotNull(details);

        return details.Response;
    }

    private static async Task<WatchlistResponse> WatchlistAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/users/me/watchlist", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<WatchlistResponse>(Ct);
        Assert.NotNull(body);

        return body;
    }

    private static async Task<RecommendationsResponse> RecommendationsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/recommendations", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RecommendationsResponse>(Ct);
        Assert.NotNull(body);
        Assert.NotEmpty(body.Recommendations);

        return body;
    }

    private async Task<Guid> SeedFilmAsync(string title)
    {
        await using var db = postgres.CreateContext();

        var film = new Movie
        {
            TmdbId = Random.Shared.Next(100_000, 999_999),
            Title = title,
            DetailsRefreshedAt = DateTimeOffset.UtcNow,
        };

        db.Movies.Add(film);
        await db.SaveChangesAsync(Ct);

        return film.Id;
    }

    private async Task SubscribeToAsync(int providerId, string name)
    {
        await using var db = postgres.CreateContext();

        var user = await db.Users.FirstAsync(Ct);

        db.StreamingProviders.Add(new StreamingProvider { Id = providerId, Name = name });
        db.UserStreamingProviders.Add(new UserStreamingProvider
        {
            UserId = user.Id,
            StreamingProviderId = providerId,
        });

        await db.SaveChangesAsync(Ct);
    }

    private async Task<int> TmdbIdForAsync(string title)
    {
        await using var db = postgres.CreateContext();

        return await db.Movies.Where(movie => movie.Title == title).Select(movie => movie.TmdbId).FirstAsync(Ct);
    }

    /// <summary>Somebody who has loved one film, so recommendations have a seed.</summary>
    private async Task<HttpClient> SignedInWithHistoryAsync()
    {
        var client = await SignedInClientAsync();

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

        _tmdb.Related[27205] =
        [
            Dto(157336, "Interstellar", "2014-11-05"),
            Dto(335984, "Blade Runner 2049", "2017-10-04"),
            Dto(329865, "Arrival", "2016-11-10"),
            Dto(693134, "Dune: Part Two", "2024-02-27"),
        ];

        return client;
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

    private async Task<HttpClient> SignedInClientAsync(string email = "parker@example.com")
    {
        using var registrar = _factory.CreateClient();

        var response = await registrar.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(email, "Parker", Password),
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

        public Dictionary<int, TmdbRegionProviders> Availability { get; } = [];

        public Task<TmdbSearchResponse> GetRelatedMoviesAsync(int tmdbId, CancellationToken cancellationToken) =>
            Task.FromResult(Page(Related.GetValueOrDefault(tmdbId, [])));

        public Task<TmdbSearchResponse> DiscoverBestInGenreAsync(
            int genreId,
            int minimumVotes,
            CancellationToken cancellationToken) =>
            Task.FromResult(Page([]));

        public Task<TmdbWatchProvidersResponse> GetWatchProvidersAsync(int tmdbId, CancellationToken cancellationToken)
        {
            var results = Availability.TryGetValue(tmdbId, out var region)
                ? new Dictionary<string, TmdbRegionProviders> { ["US"] = region }
                : [];

            return Task.FromResult(new TmdbWatchProvidersResponse { Id = tmdbId, Results = results });
        }

        public Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests seed films already enriched.");

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
