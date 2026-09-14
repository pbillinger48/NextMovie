using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Domain.Library;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Import;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Import;

/// <summary>
/// Drives reconciliation: the rows the matcher refused to guess at.
/// </summary>
/// <remarks>
/// The matcher declining is only half a design. This is the other half — and the
/// thing that makes "a wrong match is worse than a visible failure" a trade
/// rather than an excuse.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ImportReviewTests(PostgresFixture postgres) : IAsyncLifetime
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
            "delete from import_items; delete from import_jobs; delete from ratings; "
            + "delete from watch_history; delete from movie_genre; delete from movies; "
            + "delete from refresh_tokens; delete from users;",
            Ct);
    }

    [Fact]
    public async Task Review_lists_ambiguous_rows_with_the_films_they_might_mean()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        var response = await client.GetAsync($"/api/v1/import/{setup.JobId}/review", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var review = await response.Content.ReadFromJsonAsync<ImportReviewResponse>(Ct);
        Assert.NotNull(review);

        var ambiguous = review.Items.Single(item => item.Status == "Ambiguous");
        Assert.Equal("Close Call", ambiguous.Name);
        Assert.Equal(4.0m, ambiguous.Rating);

        // The candidates were put in the catalogue while the import ran, so this
        // is a local join rather than a burst of TMDb lookups while somebody
        // waits for the page.
        Assert.Equal(2, ambiguous.Candidates.Count);
        Assert.Contains(ambiguous.Candidates, candidate => candidate.Title == "Close Call");
    }

    [Fact]
    public async Task Review_includes_rows_nothing_was_found_for()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        var review = await (await client.GetAsync($"/api/v1/import/{setup.JobId}/review", Ct))
            .Content.ReadFromJsonAsync<ImportReviewResponse>(Ct);

        Assert.NotNull(review);

        // Often television, which a film search can never match. The user still
        // has to be told it is there.
        var unresolved = review.Items.Single(item => item.Status == "Unresolved");
        Assert.Equal("Squid Game", unresolved.Name);
        Assert.Empty(unresolved.Candidates);
    }

    [Fact]
    public async Task Review_does_not_list_rows_that_matched()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        var review = await (await client.GetAsync($"/api/v1/import/{setup.JobId}/review", Ct))
            .Content.ReadFromJsonAsync<ImportReviewResponse>(Ct);

        Assert.NotNull(review);
        Assert.DoesNotContain(review.Items, item => item.Name == "Inception");
    }

    [Fact]
    public async Task Another_users_review_is_not_found()
    {
        var setup = await SeedAsync();
        using var intruder = await SignedInClientAsync("intruder@example.com");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await intruder.GetAsync($"/api/v1/import/{setup.JobId}/review", Ct)).StatusCode);
    }

    // --- resolving ---

    [Fact]
    public async Task Choosing_a_film_applies_the_rating_and_the_viewing()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/import/items/{setup.AmbiguousItemId}/resolve",
            new ResolveImportItemRequest(setup.CloseCallMovieId),
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = postgres.CreateContext();

        // Exactly what a confident match would have done, through the same path.
        var rating = await db.Ratings.SingleAsync(r => r.MovieId == setup.CloseCallMovieId, Ct);
        Assert.Equal(4.0m, rating.Value);
        Assert.Equal(LibrarySource.LetterboxdImport, rating.Source);

        Assert.True(await db.WatchHistory.AnyAsync(w => w.MovieId == setup.CloseCallMovieId, Ct));

        var item = await db.ImportItems.SingleAsync(i => i.Id == setup.AmbiguousItemId, Ct);
        Assert.Equal(ImportItemStatus.Matched, item.Status);

        // Recorded as a person's decision, not an algorithm's.
        Assert.Equal(MatchMethod.Manual, item.MatchMethod);
    }

    [Fact]
    public async Task Choosing_a_film_keeps_the_counts_adding_up()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/import/items/{setup.AmbiguousItemId}/resolve",
            new ResolveImportItemRequest(setup.CloseCallMovieId),
            Ct);

        var status = await response.Content.ReadFromJsonAsync<ImportJobStatusResponse>(Ct);
        Assert.NotNull(status);

        // A row leaves the review pile exactly as it joins the matched one.
        Assert.Equal(2, status.MatchedItems);
        Assert.Equal(0, status.AmbiguousItems);
        Assert.Equal(1, status.UnresolvedItems);
    }

    [Fact]
    public async Task A_film_outside_the_candidates_is_still_allowed()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        // Someone who searched and found the right film should not be told their
        // answer is not on the list.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/import/items/{setup.UnresolvedItemId}/resolve",
            new ResolveImportItemRequest(setup.InceptionMovieId),
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_film_that_does_not_exist_is_rejected()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/import/items/{setup.AmbiguousItemId}/resolve",
            new ResolveImportItemRequest(Guid.CreateVersion7()),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_row_that_already_matched_cannot_be_resolved_again()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        // Otherwise the counts would drift every time somebody retried.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/import/items/{setup.MatchedItemId}/resolve",
            new ResolveImportItemRequest(setup.InceptionMovieId),
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_row_cannot_be_resolved()
    {
        var setup = await SeedAsync();
        using var intruder = await SignedInClientAsync("intruder@example.com");

        var response = await intruder.PostAsJsonAsync(
            $"/api/v1/import/items/{setup.AmbiguousItemId}/resolve",
            new ResolveImportItemRequest(setup.CloseCallMovieId),
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- dismissing ---

    [Fact]
    public async Task Dismissing_records_the_decision_without_importing_anything()
    {
        var setup = await SeedAsync();
        using var client = await SignedInClientAsync(setup.Email);

        var response = await client.PostAsync(
            $"/api/v1/import/items/{setup.UnresolvedItemId}/dismiss",
            content: null,
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = postgres.CreateContext();
        var item = await db.ImportItems.SingleAsync(i => i.Id == setup.UnresolvedItemId, Ct);

        // Dismissed, not deleted: the row stays as a record that the export
        // contained it and a person decided against it.
        Assert.Equal(ImportItemStatus.Dismissed, item.Status);
        Assert.Equal(0, await db.Ratings.CountAsync(r => r.MovieId == setup.InceptionMovieId && r.Source == LibrarySource.LetterboxdImport, Ct));

        var status = await response.Content.ReadFromJsonAsync<ImportJobStatusResponse>(Ct);
        Assert.NotNull(status);
        Assert.Equal(0, status.UnresolvedItems);
    }

    // --- setup ---

    private sealed record Setup(
        string Email,
        Guid JobId,
        Guid AmbiguousItemId,
        Guid UnresolvedItemId,
        Guid MatchedItemId,
        Guid CloseCallMovieId,
        Guid InceptionMovieId);

    /// <summary>
    /// A finished import with one row of each interesting outcome.
    /// </summary>
    /// <remarks>
    /// The user is registered through the API rather than inserted directly, so
    /// they have a password and the test can sign in as them. Seeding a user row
    /// by hand produces an account that exists and cannot be used.
    /// </remarks>
    private async Task<Setup> SeedAsync()
    {
        const string email = "parker@example.com";

        var userId = await RegisterAsync(email);

        await using var db = postgres.CreateContext();

        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId, Ct);

        var inception = new Movie { TmdbId = 27205, Title = "Inception" };
        var closeCall = new Movie { TmdbId = 1, Title = "Close Call", PosterPath = "/a.jpg" };
        var otherCloseCall = new Movie { TmdbId = 2, Title = "Close Call" };
        db.Movies.AddRange(inception, closeCall, otherCloseCall);

        var job = new ImportJob
        {
            UserId = user.Id,
            TotalItems = 3,
            SkippedRows = 0,
            Status = ImportJobStatus.Completed,
            MatchedItems = 1,
            AmbiguousItems = 1,
            UnresolvedItems = 1,
        };

        var matched = new ImportItem
        {
            ImportJobId = job.Id,
            Name = "Inception",
            Year = 2010,
            Status = ImportItemStatus.Matched,
            MatchedMovieId = inception.Id,
            MatchMethod = MatchMethod.Exact,
        };

        var ambiguous = new ImportItem
        {
            ImportJobId = job.Id,
            Name = "Close Call",
            Year = 2010,
            Rating = 4.0m,
            WatchedOn = new DateOnly(2022, 1, 2),
            Status = ImportItemStatus.Ambiguous,
            CandidateTmdbIds = [1, 2],
        };

        var unresolved = new ImportItem
        {
            ImportJobId = job.Id,
            Name = "Squid Game",
            Year = 2021,
            Status = ImportItemStatus.Unresolved,
        };

        job.Items.Add(matched);
        job.Items.Add(ambiguous);
        job.Items.Add(unresolved);
        db.ImportJobs.Add(job);

        await db.SaveChangesAsync(Ct);

        return new Setup(email, job.Id, ambiguous.Id, unresolved.Id, matched.Id, closeCall.Id, inception.Id);
    }

    /// <summary>Registers an account through the API and returns its id.</summary>
    private async Task<Guid> RegisterAsync(string email)
    {
        using var registrar = _factory.CreateClient();

        var response = await registrar.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(email, "Parker", Password),
            Ct);

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);

        Assert.NotNull(session);

        return session.User.Id;
    }

    private async Task<HttpClient> SignedInClientAsync(string email)
    {
        using var registrar = _factory.CreateClient();

        // Already registered by the seed, so sign in; a second registration
        // would be a 409.
        var response = await registrar.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginUserRequest(email, Password),
            Ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RegisterAsync(email);

            response = await registrar.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginUserRequest(email, Password),
                Ct);
        }

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);
        Assert.NotNull(session);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return client;
    }
}
