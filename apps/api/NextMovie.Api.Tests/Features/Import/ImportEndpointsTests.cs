using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Import;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Import;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Import;

/// <summary>
/// Drives uploading a Letterboxd export and polling its status.
/// </summary>
/// <remarks>
/// No worker exists yet, so every job here stays <c>Pending</c> — which is the
/// point: this branch is the queue and the contract, and the thing that drains it
/// is deliberately separate (ADR-0007).
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ImportEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";

    private const string Export =
        """
        Date,Name,Year,Letterboxd URI,Rating
        2022-01-02,Don't Look Up,2021,https://boxd.it/o0Hc,4
        2022-01-03,"Crouching Tiger, Hidden Dragon",2000,https://boxd.it/abc,4.5
        """;

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
            + "delete from watch_history; delete from refresh_tokens; delete from users;",
            Ct);
    }

    [Fact]
    public async Task Importing_requires_a_signed_in_user()
    {
        using var client = _factory.CreateClient();

        var response = await UploadAsync(client, Export);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_export_is_accepted_and_queued()
    {
        using var client = await SignedInClientAsync();

        var response = await UploadAsync(client, Export);

        // 202, not 201: the work is accepted, not done.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<ImportJobStatusResponse>(Ct);
        Assert.NotNull(status);
        Assert.Equal("Pending", status.Status);
        Assert.Equal(2, status.TotalItems);

        // The Location header points at where the answer will appear.
        Assert.Equal($"/api/v1/import/{status.Id}", response.Headers.Location?.ToString());

        await using var db = postgres.CreateContext();
        var job = await db.ImportJobs.Include(j => j.Items).SingleAsync(Ct);

        Assert.Equal(ImportJobStatus.Pending, job.Status);
        Assert.Equal(2, job.Items.Count);
        Assert.All(job.Items, item => Assert.Equal(ImportItemStatus.Pending, item.Status));
    }

    [Fact]
    public async Task Every_row_is_stored_as_the_export_gave_it()
    {
        using var client = await SignedInClientAsync();
        await UploadAsync(client, Export);

        await using var db = postgres.CreateContext();
        var items = await db.ImportItems.OrderBy(item => item.Name).ToListAsync(Ct);

        // The comma inside the quoted title survived, which is the whole reason
        // a real CSV parser earns its dependency.
        Assert.Equal("Crouching Tiger, Hidden Dragon", items[0].Name);
        Assert.Equal(2000, items[0].Year);
        Assert.Equal(4.5m, items[0].Rating);

        Assert.Equal("Don't Look Up", items[1].Name);
        Assert.Equal("https://boxd.it/o0Hc", items[1].FilmUri);
        Assert.Equal(new DateOnly(2022, 1, 2), items[1].WatchedOn);
    }

    [Fact]
    public async Task Rows_with_no_title_are_reported_rather_than_vanishing()
    {
        using var client = await SignedInClientAsync();

        var response = await UploadAsync(
            client,
            """
            Date,Name,Year,Letterboxd URI
            2022-01-02,,2021,https://boxd.it/o0Hc
            2022-01-03,Inception,2010,https://boxd.it/abc
            """);

        var status = await response.Content.ReadFromJsonAsync<ImportJobStatusResponse>(Ct);

        // A user who exported two films and imported one should be told which
        // number is which.
        Assert.NotNull(status);
        Assert.Equal(1, status.TotalItems);
        Assert.Equal(1, status.SkippedRows);
    }

    [Theory]
    [InlineData("", "export.csv")]
    [InlineData("Date,Name,Year", "export.csv")]
    [InlineData(Export, "export.txt")]
    public async Task An_unusable_upload_is_rejected(string contents, string fileName)
    {
        using var client = await SignedInClientAsync();

        var response = await UploadAsync(client, contents, fileName);

        // Rejected while the user is still looking at the form, rather than two
        // minutes later inside a job status.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.Equal(0, await db.ImportJobs.CountAsync(Ct));
    }

    // --- status ---

    [Fact]
    public async Task An_import_can_be_polled()
    {
        using var client = await SignedInClientAsync();
        var queued = await UploadAsync(client, Export);
        var job = await queued.Content.ReadFromJsonAsync<ImportJobStatusResponse>(Ct);
        Assert.NotNull(job);

        var response = await client.GetAsync($"/api/v1/import/{job.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<ImportJobStatusResponse>(Ct);
        Assert.NotNull(status);
        Assert.Equal(job.Id, status.Id);
        Assert.Equal("Pending", status.Status);
        Assert.Null(status.CompletedAt);
    }

    [Fact]
    public async Task Another_users_import_is_not_found_rather_than_forbidden()
    {
        using var mine = await SignedInClientAsync("mine@example.com");
        using var theirs = await SignedInClientAsync("theirs@example.com");

        var queued = await UploadAsync(mine, Export);
        var job = await queued.Content.ReadFromJsonAsync<ImportJobStatusResponse>(Ct);
        Assert.NotNull(job);

        var response = await theirs.GetAsync($"/api/v1/import/{job.Id}", Ct);

        // 404, not 403: "that exists but is not yours" would be an oracle for how
        // many imports other people have run.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_import_is_not_found()
    {
        using var client = await SignedInClientAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/import/{Guid.CreateVersion7()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Polling_requires_a_signed_in_user()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/import/{Guid.CreateVersion7()}", Ct)).StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string contents,
        string fileName = "ratings.csv")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(contents));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);

        return await client.PostAsync("/api/v1/import/letterboxd", form, Ct);
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
