using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Infrastructure.Auth.Google;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Auth;

/// <summary>
/// Tests what happens after a Google identity is verified: create, link, or refuse.
/// </summary>
/// <remarks>
/// These are the rules ADR-0005 decided, and each has a wrong answer that is an
/// account takeover or a duplicate account rather than a cosmetic bug. Token
/// verification is stubbed here and tested for real in
/// <c>GoogleIdTokenVerifierTests</c>.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class GoogleSignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Email = "parker@example.com";
    private const string Subject = "112233445566778899000";

    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    public Task InitializeAsync() => ClearAccountsAsync();

    public Task DisposeAsync() => ClearAccountsAsync();

    private async Task ClearAccountsAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "delete from user_external_logins; delete from refresh_tokens; delete from users;", Ct);
    }

    [Fact]
    public async Task An_unknown_google_account_creates_one()
    {
        using var factory = FactoryFor(Identity());
        using var client = factory.CreateClient();

        var response = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = postgres.CreateContext();
        var user = await db.Users.SingleAsync(Ct);

        Assert.Equal(Email, user.Email);
        Assert.Equal("Parker", user.DisplayName);

        // ADR-0003 made PasswordHash nullable for exactly this: an account can
        // exist with no password at all.
        Assert.Null(user.PasswordHash);

        var login = await db.UserExternalLogins.SingleAsync(Ct);
        Assert.Equal(ExternalLoginProvider.Google, login.Provider);
        Assert.Equal(Subject, login.ProviderSubject);
        Assert.Equal(user.Id, login.UserId);
    }

    [Fact]
    public async Task Signing_in_twice_reuses_the_same_account()
    {
        using var factory = FactoryFor(Identity());
        using var client = factory.CreateClient();

        await SignInAsync(client);
        var second = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(Ct));
        Assert.Equal(1, await db.UserExternalLogins.CountAsync(Ct));
    }

    [Fact]
    public async Task A_verified_address_links_to_an_existing_password_account()
    {
        using var factory = FactoryFor(Identity());
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(Email, "Parker", Password),
            Ct);

        var response = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = postgres.CreateContext();

        // One account, reached two ways — not a duplicate, which the unique index
        // on the address would have refused anyway.
        var user = await db.Users.SingleAsync(Ct);
        Assert.Equal(user.Id, (await db.UserExternalLogins.SingleAsync(Ct)).UserId);

        // And the password still works: linking must not lock anyone out of the
        // way they already signed in.
        Assert.NotNull(user.PasswordHash);

        var withPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginUserRequest(Email, Password),
            Ct);

        Assert.Equal(HttpStatusCode.OK, withPassword.StatusCode);
    }

    [Fact]
    public async Task Linking_does_not_overwrite_a_profile_the_user_chose()
    {
        using var factory = FactoryFor(Identity(name: "Google Display Name"));
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(Email, "Parker's Own Name", Password),
            Ct);

        await SignInAsync(client);

        await using var db = postgres.CreateContext();

        // A name the user set here outranks whatever Google has on file.
        Assert.Equal("Parker's Own Name", (await db.Users.SingleAsync(Ct)).DisplayName);
    }

    [Fact]
    public async Task An_unverified_address_is_refused()
    {
        using var factory = FactoryFor(Identity(emailVerified: false));
        using var client = factory.CreateClient();

        var response = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = postgres.CreateContext();

        // Nothing created and nothing linked: an unverified address is an
        // unproven claim to own it, and acting on one is how this becomes an
        // account takeover.
        Assert.Equal(0, await db.Users.CountAsync(Ct));
        Assert.Equal(0, await db.UserExternalLogins.CountAsync(Ct));
    }

    [Fact]
    public async Task An_unverified_address_cannot_take_over_an_existing_account()
    {
        using var factory = FactoryFor(Identity(emailVerified: false));
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(Email, "Parker", Password),
            Ct);

        Assert.Equal(HttpStatusCode.Forbidden, (await SignInAsync(client)).StatusCode);

        await using var db = postgres.CreateContext();
        Assert.Equal(0, await db.UserExternalLogins.CountAsync(Ct));
    }

    [Fact]
    public async Task A_changed_google_address_still_reaches_the_same_account()
    {
        using var factory = FactoryFor(Identity());
        using var client = factory.CreateClient();
        await SignInAsync(client);

        // Same person, same Google account, new address. Because identity is
        // keyed on the subject rather than the email, this is the same user —
        // and, just as importantly, whoever later acquires the old address is
        // not.
        using var renamedFactory = FactoryFor(Identity(email: "parker.new@example.com"));
        using var renamedClient = renamedFactory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(renamedClient)).StatusCode);

        await using var db = postgres.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(Ct));
        Assert.Equal(1, await db.UserExternalLogins.CountAsync(Ct));
    }

    [Fact]
    public async Task An_unverifiable_token_is_refused()
    {
        using var factory = FactoryFor(identity: null);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client)).StatusCode);
    }

    [Fact]
    public async Task A_missing_token_is_a_validation_error()
    {
        using var factory = FactoryFor(Identity());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/google",
            new SignInWithGoogleRequest(null),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_google_session_refreshes_like_any_other()
    {
        using var factory = FactoryFor(Identity());
        using var client = factory.CreateClient();

        var session = await (await SignInAsync(client)).Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);
        Assert.NotNull(session);

        // The whole point of issuing our own tokens: nothing downstream needs to
        // know the session began at Google.
        var refreshed = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshSessionRequest(session.RefreshToken),
            Ct);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    private NextMovieApiFactory FactoryFor(GoogleIdentity? identity) =>
        new(postgres.ConnectionString) { GoogleIdTokens = new StubVerifier(identity) };

    private static GoogleIdentity Identity(
        string email = Email,
        bool emailVerified = true,
        string? name = "Parker") =>
        new(Subject, email, emailVerified, name, "https://lh3.googleusercontent.com/a/parker");

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/google",
            new SignInWithGoogleRequest("a-token-the-stub-verifier-accepts"),
            Ct);

    private sealed class StubVerifier(GoogleIdentity? identity) : IGoogleIdTokenVerifier
    {
        public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken) =>
            Task.FromResult(identity);
    }
}
