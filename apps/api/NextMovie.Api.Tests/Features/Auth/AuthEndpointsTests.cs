using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Authentication;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Auth;

/// <summary>
/// Drives registration and sign-in end to end against real PostgreSQL.
/// </summary>
/// <remarks>
/// These need the real database rather than a stub. Half of what is being
/// asserted — that a duplicate email is refused, that a refresh token row is
/// written alongside the account in one transaction — is enforced by the
/// database itself, and a fake would agree with whatever the code did.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AuthEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";

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

    // --- registration ---

    [Fact]
    public async Task Registering_creates_an_account_and_signs_it_in()
    {
        using var client = _factory.CreateClient();

        var response = await RegisterAsync(client, "parker@example.com");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var session = await ReadSessionAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));
        Assert.Equal("parker@example.com", session.User.Email);
        Assert.Equal("Parker", session.User.DisplayName);
        Assert.NotEqual(Guid.Empty, session.User.Id);
    }

    [Fact]
    public async Task Registering_never_stores_the_password_as_given()
    {
        using var client = _factory.CreateClient();

        await RegisterAsync(client, "parker@example.com");

        await using var db = postgres.CreateContext();
        var stored = await db.Users.SingleAsync(Ct);

        Assert.NotNull(stored.PasswordHash);
        Assert.DoesNotContain(Password, stored.PasswordHash);
    }

    [Fact]
    public async Task Registering_stores_the_refresh_token_only_as_a_hash()
    {
        using var client = _factory.CreateClient();

        var session = await ReadSessionAsync(await RegisterAsync(client, "parker@example.com"));

        await using var db = postgres.CreateContext();
        var stored = await db.RefreshTokens.SingleAsync(Ct);

        // A database dump must not hand an attacker usable tokens: what is
        // stored has to be derivable from the token, never the reverse.
        Assert.Equal(RefreshTokenFactory.Hash(session.RefreshToken), stored.TokenHash);
        Assert.DoesNotContain(session.RefreshToken, stored.TokenHash);
    }

    [Fact]
    public async Task An_email_that_differs_only_by_case_is_refused()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, "Parker@Example.com");

        var duplicate = await RegisterAsync(client, "parker@example.com");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.Equal(1, await db.Users.CountAsync(Ct));
    }

    [Theory]
    [InlineData("not-an-email", Password, "Email")]
    [InlineData("parker@example.com", "short", "Password")]
    public async Task Invalid_registration_details_are_rejected_with_the_offending_field(
        string email, string password, string expectedField)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(email, "Parker", password),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains(expectedField, body);
    }

    // --- sign-in ---

    [Fact]
    public async Task Signing_in_with_the_right_password_returns_a_session()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, "parker@example.com");

        var response = await LoginAsync(client, "parker@example.com", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await ReadSessionAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));

        await using var db = postgres.CreateContext();
        var stored = await db.Users.SingleAsync(Ct);
        Assert.NotNull(stored.LastLoginAt);
        Assert.Equal(0, stored.FailedLoginAttempts);
    }

    [Fact]
    public async Task Signing_in_ignores_the_case_of_the_email()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, "parker@example.com");

        var response = await LoginAsync(client, "PARKER@Example.COM", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_indistinguishable_from_an_unknown_account()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, "parker@example.com");

        var wrongPassword = await LoginAsync(client, "parker@example.com", "wrong password entirely");
        var unknownAccount = await LoginAsync(client, "nobody@example.com", Password);

        // Identical, because any difference at all — status, title, even a
        // distinguishing detail string — turns this endpoint into a way to test
        // whether an address has an account.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        Assert.Equal(await ProblemWithoutTraceIdAsync(wrongPassword), await ProblemWithoutTraceIdAsync(unknownAccount));
    }

    /// <summary>
    /// The ProblemDetails body with <c>traceId</c> stripped.
    /// </summary>
    /// <remarks>
    /// Every response carries a different trace id by design, and it is derived
    /// from the request rather than from anything about the account — so it is
    /// the one field that may differ without leaking whether the address exists.
    /// </remarks>
    private static async Task<string> ProblemWithoutTraceIdAsync(HttpResponseMessage response)
    {
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();
        problem.Remove("traceId");

        return problem.ToJsonString();
    }

    [Fact]
    public async Task A_failed_attempt_is_counted_against_the_account()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, "parker@example.com");

        await LoginAsync(client, "parker@example.com", "wrong password entirely");

        await using var db = postgres.CreateContext();
        var stored = await db.Users.SingleAsync(Ct);

        // The counter has to survive the request, or lockout counts nothing.
        Assert.Equal(1, stored.FailedLoginAttempts);
    }

    [Fact]
    public async Task Enough_failures_lock_the_account_against_the_correct_password()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, "parker@example.com");

        for (var attempt = 0; attempt < AuthenticationPolicy.MaxFailedLoginAttempts; attempt++)
        {
            await LoginAsync(client, "parker@example.com", "wrong password entirely");
        }

        var withCorrectPassword = await LoginAsync(client, "parker@example.com", Password);

        // The lockout is checked before the password, so guessing it correctly
        // on the next attempt gains nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.NotNull((await db.Users.SingleAsync(Ct)).LockoutEndsAt);
    }

    [Fact]
    public async Task Each_sign_in_starts_its_own_refresh_token_family()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, "parker@example.com");
        await LoginAsync(client, "parker@example.com", Password);

        await using var db = postgres.CreateContext();
        var families = await db.RefreshTokens.Select(t => t.FamilyId).ToListAsync(Ct);

        // Two independent sessions — a phone and a laptop, say. They must not
        // share a family, or revoking one on suspicion of theft would sign the
        // other out too.
        Assert.Equal(2, families.Count);
        Assert.Equal(2, families.Distinct().Count());
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(email, "Parker", Password),
            Ct);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new LoginUserRequest(email, password), Ct);

    private static async Task<AuthenticationResponse> ReadSessionAsync(HttpResponseMessage response)
    {
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);

        Assert.NotNull(session);
        return session;
    }
}
