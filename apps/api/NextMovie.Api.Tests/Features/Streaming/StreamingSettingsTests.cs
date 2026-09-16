using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Streaming;
using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Streaming;

/// <summary>
/// Drives the settings that make availability personal.
/// </summary>
/// <remarks>
/// Without these, every film reads as "streaming somewhere" rather than
/// "streaming on something you pay for" — the distinction the whole availability
/// feature exists to make. The stub stands in for TMDb at the client, so the real
/// <c>TmdbServiceDirectory</c> and <c>ServiceCatalog</c> both run.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class StreamingSettingsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Email = "parker@example.com";
    private const string Password = "correct horse battery staple";

    private const int Netflix = 8;
    private const int Max = 1899;
    private const int SkyGo = 29;

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    private readonly StubTmdb _tmdb = new();

    private NextMovieApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _tmdb.Catalogues["US"] = [Provider(Netflix, "Netflix", 1), Provider(Max, "Max", 2)];
        _tmdb.Catalogues["GB"] = [Provider(SkyGo, "Sky Go", 1)];

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
            "delete from availability_offers; delete from movie_availability; "
            + "delete from user_streaming_providers; delete from regional_providers; "
            + "delete from region_catalogs; delete from streaming_providers; "
            + "delete from refresh_tokens; delete from users;",
            Ct);
    }

    // --- who may call ---

    [Fact]
    public async Task Settings_are_not_readable_without_a_token()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/users/me/streaming", Ct)).StatusCode);
    }

    [Fact]
    public async Task Settings_are_not_writable_without_a_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/streaming",
            new UpdateStreamingSettingsRequest("US", [Netflix]),
            Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- reading ---

    [Fact]
    public async Task A_new_account_is_offered_the_services_in_its_region()
    {
        using var client = await AuthenticatedClientAsync();

        var settings = await ReadAsync(client);

        Assert.Equal("US", settings.Region);

        // The point of fetching the catalogue upstream rather than reading the
        // services we happen to have seen on films: a brand-new account has seen
        // none, and would otherwise be offered an empty list forever.
        Assert.Equal(["Netflix", "Max"], settings.Services.Select(service => service.Name));
        Assert.All(settings.Services, service => Assert.False(service.Subscribed));
    }

    [Fact]
    public async Task Countries_are_offered_to_choose_between()
    {
        using var client = await AuthenticatedClientAsync();

        var countries = (await ReadAsync(client)).Countries;

        Assert.Contains(countries, country => country.Code == "US");
        Assert.Contains(countries, country => country.Code == "GB");
        Assert.All(countries, country => Assert.Equal(2, country.Code.Length));
    }

    // --- writing ---

    [Fact]
    public async Task A_subscription_is_saved_and_read_back()
    {
        using var client = await AuthenticatedClientAsync();

        var saved = await SaveAsync(client, "US", [Netflix]);

        Assert.True(Service(saved, Netflix).Subscribed);
        Assert.False(Service(saved, Max).Subscribed);

        // Read back through the other endpoint, so the write is proved to have
        // landed rather than merely been echoed.
        Assert.True(Service(await ReadAsync(client), Netflix).Subscribed);
    }

    [Fact]
    public async Task Unticking_a_service_cancels_it()
    {
        using var client = await AuthenticatedClientAsync();
        await SaveAsync(client, "US", [Netflix, Max]);

        var saved = await SaveAsync(client, "US", [Max]);

        // PUT replaces the set. If absence meant "leave alone", nobody could ever
        // tell us they had cancelled something.
        Assert.False(Service(saved, Netflix).Subscribed);
        Assert.True(Service(saved, Max).Subscribed);

        await using var db = postgres.CreateContext();
        Assert.Equal(Max, (await db.UserStreamingProviders.SingleAsync(Ct)).StreamingProviderId);
    }

    [Fact]
    public async Task Subscribing_to_nothing_is_a_valid_answer()
    {
        using var client = await AuthenticatedClientAsync();
        await SaveAsync(client, "US", [Netflix]);

        await SaveAsync(client, "US", []);

        await using var db = postgres.CreateContext();
        Assert.Empty(await db.UserStreamingProviders.ToListAsync(Ct));
    }

    [Fact]
    public async Task A_service_kept_across_a_save_keeps_its_start_date()
    {
        using var client = await AuthenticatedClientAsync();
        await SaveAsync(client, "US", [Netflix]);

        DateTimeOffset original;

        await using (var db = postgres.CreateContext())
        {
            original = (await db.UserStreamingProviders.SingleAsync(Ct)).AddedAt;
        }

        await SaveAsync(client, "US", [Netflix, Max]);

        await using var after = postgres.CreateContext();
        var netflix = await after.UserStreamingProviders
            .SingleAsync(subscription => subscription.StreamingProviderId == Netflix, Ct);

        // Reconciled rather than deleted and reinserted. Rewriting every row on
        // every save would make "since when" mean "since you last opened
        // settings".
        Assert.Equal(original, netflix.AddedAt);
    }

    // --- moving country ---

    [Fact]
    public async Task Changing_region_changes_which_services_are_offered()
    {
        using var client = await AuthenticatedClientAsync();

        var saved = await SaveAsync(client, "GB", []);

        Assert.Equal("GB", saved.Region);
        Assert.Equal(["Sky Go"], saved.Services.Select(service => service.Name));
    }

    [Fact]
    public async Task A_subscription_the_new_country_does_not_offer_is_shown_rather_than_hidden()
    {
        using var client = await AuthenticatedClientAsync();
        await SaveAsync(client, "US", [Netflix]);

        var saved = await SaveAsync(client, "GB", [Netflix]);

        var netflix = Service(saved, Netflix);

        // The row is still in the database and still shaping recommendations.
        // Dropping it from the list would leave the user unable to untick a
        // service they can see the effects of.
        Assert.True(netflix.Subscribed);
        Assert.False(netflix.OfferedHere);
        Assert.True(Service(saved, SkyGo).OfferedHere);
    }

    // --- refusing bad input ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("USA")]
    [InlineData("ZZ")]
    [InlineData("12")]
    public async Task An_unusable_region_is_refused(string region)
    {
        using var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/streaming",
            new UpdateStreamingSettingsRequest(region, []),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Region", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_service_we_have_never_heard_of_is_refused()
    {
        using var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/streaming",
            new UpdateStreamingSettingsRequest("US", [Netflix, 999_999]),
            Ct);

        // A foreign key would refuse this too, but as a 500 the caller cannot act
        // on. 400 names the id that was wrong.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("999999", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task An_implausible_number_of_services_is_refused()
    {
        using var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/streaming",
            new UpdateStreamingSettingsRequest("US", [.. Enumerable.Range(1, 200)]),
            Ct);

        // Not a product rule so much as a bound on what one request can make the
        // database do.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ProviderIds", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_region_is_accepted_in_any_case_and_stored_upper()
    {
        using var client = await AuthenticatedClientAsync();

        Assert.Equal("GB", (await SaveAsync(client, "gb", [])).Region);
    }

    // --- when TMDb is down ---

    [Fact]
    public async Task An_outage_still_shows_what_the_user_already_chose()
    {
        using var client = await AuthenticatedClientAsync();
        await SaveAsync(client, "US", [Netflix]);

        _tmdb.Unreachable = true;

        var settings = await ReadAsync(client);

        // An empty page would read as "you subscribe to nothing", which is a
        // claim about the user rather than about our upstream.
        Assert.True(Service(settings, Netflix).Subscribed);
    }

    // --- helpers ---

    private static TmdbProviderListItem Provider(int id, string name, int priority) => new()
    {
        ProviderId = id,
        ProviderName = name,
        LogoPath = "/logo.jpg",
        DisplayPriority = priority,
    };

    private static StreamingServiceOption Service(StreamingSettingsResponse settings, int providerId) =>
        Assert.Single(settings.Services, service => service.Id == providerId);

    private static async Task<StreamingSettingsResponse> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/users/me/streaming", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadBodyAsync(response);
    }

    private static async Task<StreamingSettingsResponse> SaveAsync(
        HttpClient client,
        string region,
        int[] providerIds)
    {
        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/streaming",
            new UpdateStreamingSettingsRequest(region, providerIds),
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadBodyAsync(response);
    }

    private static async Task<StreamingSettingsResponse> ReadBodyAsync(HttpResponseMessage response)
    {
        var settings = await response.Content.ReadFromJsonAsync<StreamingSettingsResponse>(Ct);

        Assert.NotNull(settings);
        return settings;
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        using var registrar = _factory.CreateClient();

        var registration = await registrar.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(Email, "Parker", Password),
            Ct);

        var session = await registration.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);
        Assert.NotNull(session);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return client;
    }

    private sealed class StubTmdb : ITmdbClient
    {
        public Dictionary<string, TmdbProviderListItem[]> Catalogues { get; } = [];

        public bool Unreachable { get; set; }

        public Task<TmdbProviderListResponse> GetProvidersAsync(string region, CancellationToken cancellationToken) =>
            Unreachable
                ? Task.FromException<TmdbProviderListResponse>(
                    new TmdbException("TMDb could not be reached.", statusCode: null))
                : Task.FromResult(new TmdbProviderListResponse
                {
                    Results = Catalogues.GetValueOrDefault(region, []),
                });

        public Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbSearchResponse> GetRelatedMoviesAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbSearchResponse> DiscoverBestInGenreAsync(int genreId, int minimumVotes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TmdbWatchProvidersResponse> GetWatchProvidersAsync(int tmdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
