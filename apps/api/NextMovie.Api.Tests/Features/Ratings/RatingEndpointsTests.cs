using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Ratings;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Ratings;

/// <summary>
/// Drives rating, unrating and listing against real PostgreSQL.
/// </summary>
/// <remarks>
/// The rule under test is ADR-0006's: a rating implies a viewing, and it holds
/// however the rating arrived. Native entry is exercised here precisely so the
/// Letterboxd import later lands on a model that already has a second writer.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RatingEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    private NextMovieApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new NextMovieApiFactory(postgres.ConnectionString);

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
            "delete from ratings; delete from watch_history; delete from movie_genre; "
            + "delete from movies; delete from refresh_tokens; delete from users;",
            Ct);
    }

    [Fact]
    public async Task Rating_requires_a_signed_in_user()
    {
        var movieId = await SeedMovieAsync();
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/movies/{movieId}/rating",
            new RateMovieRequest(4.5m),
            Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rating_a_film_records_the_rating_and_a_viewing()
    {
        var movieId = await SeedMovieAsync();
        using var client = await SignedInClientAsync();

        var response = await RateAsync(client, movieId, 4.5m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MovieRating>(Ct);
        Assert.NotNull(body);
        Assert.Equal(4.5m, body.Rating);

        await using var db = postgres.CreateContext();
        var stored = await db.Ratings.SingleAsync(Ct);
        Assert.Equal(4.5m, stored.Value);

        // Native, not import — this is what stops a later Letterboxd import
        // overwriting something the user typed.
        Assert.Equal(LibrarySource.Native, stored.Source);

        // The implied viewing, with no date: rating from memory says nothing
        // about when the film was seen.
        var viewing = await db.WatchHistory.SingleAsync(Ct);
        Assert.Equal(movieId, viewing.MovieId);
        Assert.Null(viewing.WatchedOn);
    }

    [Fact]
    public async Task Rating_the_same_film_again_revises_rather_than_accumulates()
    {
        var movieId = await SeedMovieAsync();
        using var client = await SignedInClientAsync();

        await RateAsync(client, movieId, 3.0m);
        await RateAsync(client, movieId, 5.0m);

        await using var db = postgres.CreateContext();

        // One opinion per person per film, and one implied viewing — changing
        // your mind is not watching it again.
        var stored = await db.Ratings.SingleAsync(Ct);
        Assert.Equal(5.0m, stored.Value);
        Assert.Equal(1, await db.WatchHistory.CountAsync(Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5.5)]
    [InlineData(4.3)]
    [InlineData(8)]
    public async Task An_off_scale_rating_is_rejected(decimal rating)
    {
        var movieId = await SeedMovieAsync();
        using var client = await SignedInClientAsync();

        var response = await RateAsync(client, movieId, rating);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.Equal(0, await db.Ratings.CountAsync(Ct));
    }

    [Fact]
    public async Task Rating_a_film_that_is_not_in_the_catalogue_is_not_found()
    {
        using var client = await SignedInClientAsync();

        var response = await RateAsync(client, Guid.CreateVersion7(), 4.0m);

        // A 404 rather than the 500 a foreign key violation would produce.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unrating_removes_the_rating_but_keeps_the_viewing()
    {
        var movieId = await SeedMovieAsync();
        using var client = await SignedInClientAsync();
        await RateAsync(client, movieId, 4.0m);

        var response = await client.DeleteAsync($"/api/v1/movies/{movieId}/rating", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.Equal(0, await db.Ratings.CountAsync(Ct));

        // Changing your mind about a rating is not a claim that you never saw it.
        Assert.Equal(1, await db.WatchHistory.CountAsync(Ct));
    }

    [Fact]
    public async Task Unrating_something_never_rated_still_succeeds()
    {
        var movieId = await SeedMovieAsync();
        using var client = await SignedInClientAsync();

        // Deleting twice is not an error; a retried request must not look like a
        // failure.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/movies/{movieId}/rating", Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/movies/{movieId}/rating", Ct)).StatusCode);
    }

    [Fact]
    public async Task One_users_rating_is_invisible_to_another()
    {
        var movieId = await SeedMovieAsync();
        using var mine = await SignedInClientAsync("mine@example.com");
        using var theirs = await SignedInClientAsync("theirs@example.com");

        await RateAsync(mine, movieId, 5.0m);

        var response = await theirs.GetAsync("/api/v1/users/me/ratings", Ct);
        var listed = await response.Content.ReadFromJsonAsync<MyRatingsResponse>(Ct);

        Assert.NotNull(listed);
        Assert.Empty(listed.Ratings);
    }

    [Fact]
    public async Task Listing_returns_rated_films_with_enough_to_render_them()
    {
        var movieId = await SeedMovieAsync();
        using var client = await SignedInClientAsync();
        await RateAsync(client, movieId, 4.5m);

        var response = await client.GetAsync("/api/v1/users/me/ratings", Ct);
        var listed = await response.Content.ReadFromJsonAsync<MyRatingsResponse>(Ct);

        Assert.NotNull(listed);

        var rated = Assert.Single(listed.Ratings);
        Assert.Equal(movieId, rated.MovieId);
        Assert.Equal(4.5m, rated.Rating);

        // Title and poster travel with the rating so a list does not need a
        // request per row.
        Assert.Equal("Inception", rated.Title);
        Assert.Equal("/poster.jpg", rated.PosterPath);
    }

    [Fact]
    public async Task The_database_refuses_an_off_scale_rating_even_if_the_api_is_bypassed()
    {
        var movieId = await SeedMovieAsync();
        var userId = await SeedUserAsync();

        await using var db = postgres.CreateContext();
        db.Ratings.Add(new Rating
        {
            UserId = userId,
            MovieId = movieId,
            Value = 4.3m,
            Source = LibrarySource.Native,
        });

        // The check constraint is what makes the scale true rather than merely
        // validated — an import or a hand-written statement must not be able to
        // introduce a value the application would reject.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private static Task<HttpResponseMessage> RateAsync(HttpClient client, Guid movieId, decimal rating) =>
        client.PutAsJsonAsync($"/api/v1/movies/{movieId}/rating", new RateMovieRequest(rating), Ct);

    private async Task<Guid> SeedMovieAsync()
    {
        await using var db = postgres.CreateContext();

        var movie = new Movie
        {
            TmdbId = 27205,
            Title = "Inception",
            PosterPath = "/poster.jpg",
            ReleaseDate = new DateOnly(2010, 7, 15),
        };

        db.Movies.Add(movie);
        await db.SaveChangesAsync(Ct);

        return movie.Id;
    }

    private async Task<Guid> SeedUserAsync()
    {
        await using var db = postgres.CreateContext();

        var user = new User { Email = "direct@example.com", DisplayName = "Direct" };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        return user.Id;
    }

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
}
