using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NextMovie.Api.Domain;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Features.Users;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Users;

/// <summary>
/// Drives the first authenticated endpoints end to end.
/// </summary>
/// <remarks>
/// Until now the bearer middleware was wired but inert — tokens were issued and
/// verified only in unit tests. These are the first tests that prove a token
/// minted by registration actually opens a protected endpoint, and that tokens
/// which should not open it do not.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CurrentUserTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Email = "parker@example.com";
    private const string ImageUrl = "https://example.com/parker.jpg";

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    private NextMovieApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new NextMovieApiFactory(postgres.ConnectionString);

        return ClearAccountsAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await ClearAccountsAsync();
    }

    private async Task ClearAccountsAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("delete from refresh_tokens; delete from users;", Ct);
    }

    // --- who may call ---

    [Fact]
    public async Task A_request_without_a_token_is_refused()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/users/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_token_is_refused()
    {
        using var client = Authenticated(_factory.CreateClient(), "not-a-jwt-at-all");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        using var client = _factory.CreateClient();
        var session = await RegisterAsync(client);

        var forged = ForgeTokenFor(session.User.Id);

        // Same issuer, same audience, same subject, correct shape — everything
        // except the signature. If the middleware were validating anything less
        // than the signature, anyone could mint a token for any account.
        using var forgedClient = Authenticated(_factory.CreateClient(), forged);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await forgedClient.GetAsync("/api/v1/users/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_token_whose_user_no_longer_exists_is_refused()
    {
        using var client = _factory.CreateClient();
        var session = await RegisterAsync(client);

        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("delete from refresh_tokens; delete from users;", Ct);
        }

        using var authenticated = Authenticated(_factory.CreateClient(), session.AccessToken);

        // The token is genuine and unexpired; its subject is gone. 401 tells the
        // client to sign in again, which is the only thing that can help it.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await authenticated.GetAsync("/api/v1/users/me", Ct)).StatusCode);
    }

    // --- reading the profile ---

    [Fact]
    public async Task A_registered_user_can_read_their_own_profile()
    {
        using var client = _factory.CreateClient();
        var session = await RegisterAsync(client);

        using var authenticated = Authenticated(_factory.CreateClient(), session.AccessToken);
        var response = await authenticated.GetAsync("/api/v1/users/me", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await ReadProfileAsync(response);
        Assert.Equal(session.User.Id, profile.Id);
        Assert.Equal(Email, profile.Email);
        Assert.Equal("Parker", profile.DisplayName);
        Assert.Null(profile.ProfileImageUrl);
    }

    // --- updating the profile ---

    [Fact]
    public async Task A_profile_update_is_persisted()
    {
        using var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me",
            new UpdateCurrentUserRequest("Parker B", ImageUrl),
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadProfileAsync(response);
        Assert.Equal("Parker B", updated.DisplayName);
        Assert.Equal(ImageUrl, updated.ProfileImageUrl);

        await using var db = postgres.CreateContext();
        var stored = await db.Users.SingleAsync(Ct);
        Assert.Equal("Parker B", stored.DisplayName);
        Assert.Equal(ImageUrl, stored.ProfileImageUrl);
    }

    [Fact]
    public async Task Omitting_the_image_clears_it()
    {
        using var client = await AuthenticatedClientAsync();
        await client.PutAsJsonAsync("/api/v1/users/me", new UpdateCurrentUserRequest("Parker", ImageUrl), Ct);

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me",
            new UpdateCurrentUserRequest("Parker", null),
            Ct);

        // PUT replaces the resource, so an absent field is an absent value. This
        // is the behaviour clients most often get wrong, which is exactly why it
        // is pinned down here.
        Assert.Null((await ReadProfileAsync(response)).ProfileImageUrl);
    }

    [Theory]
    [InlineData("", ImageUrl, "DisplayName")]
    [InlineData("   ", ImageUrl, "DisplayName")]
    [InlineData("Parker", "javascript:alert(1)", "ProfileImageUrl")]
    [InlineData("Parker", "/relative/path.jpg", "ProfileImageUrl")]
    public async Task An_invalid_profile_is_rejected(string displayName, string? imageUrl, string expectedField)
    {
        using var client = await AuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me",
            new UpdateCurrentUserRequest(displayName, imageUrl),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedField, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_profile_update_cannot_change_the_email()
    {
        using var client = await AuthenticatedClientAsync();

        // Sent as raw JSON so the request carries a field the contract does not
        // define. An attempt to smuggle an email change through the profile
        // endpoint must be ignored, not honoured.
        using var body = JsonContent.Create(new
        {
            displayName = "Parker",
            email = "someone.else@example.com",
        });

        var response = await client.PutAsync("/api/v1/users/me", body, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = postgres.CreateContext();
        var stored = await db.Users.SingleAsync(Ct);
        Assert.Equal(Email, stored.Email);
        Assert.Equal("PARKER@EXAMPLE.COM", stored.NormalizedEmail);
    }

    // --- helpers ---

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        using var registrar = _factory.CreateClient();
        var session = await RegisterAsync(registrar);

        return Authenticated(_factory.CreateClient(), session.AccessToken);
    }

    private static HttpClient Authenticated(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Mints a token that is correct in every respect except its signature.</summary>
    private static string ForgeTokenFor(Guid userId)
    {
        var wrongKey = new JwtOptions
        {
            SigningKey = "a-different-key-that-is-also-long-enough-for-hs256",
            Issuer = NextMovieApiFactory.Issuer,
            Audience = NextMovieApiFactory.Audience,
        };

        var user = new User { Id = userId, Email = Email, DisplayName = "Parker" };

        return new JwtAccessTokenIssuer(Options.Create(wrongKey), TimeProvider.System).Issue(user).Value;
    }

    private static async Task<AuthenticationResponse> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(Email, "Parker", Password),
            Ct);

        var session = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);

        Assert.NotNull(session);
        return session;
    }

    private static async Task<UserProfileResponse> ReadProfileAsync(HttpResponseMessage response)
    {
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(Ct);

        Assert.NotNull(profile);
        return profile;
    }
}
