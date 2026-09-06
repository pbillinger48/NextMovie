using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Features.Auth;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Tests.Infrastructure.Persistence;

namespace NextMovie.Api.Tests.Features.Auth;

/// <summary>
/// Drives refresh rotation, replay detection and sign-out end to end.
/// </summary>
/// <remarks>
/// ADR-0003 accepts owning the refresh protocol on the condition that its sharp
/// edges are tested deliberately rather than assumed. These are those edges:
/// that a rotated token really stops working, that replaying one ends the whole
/// session, that ordinary expiry is not mistaken for theft, and that two racing
/// refreshes cannot both win.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SessionLifecycleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Email = "parker@example.com";

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

    // --- rotation ---

    [Fact]
    public async Task Refreshing_issues_a_new_token_and_retires_the_presented_one()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);

        var refreshed = await ReadSessionAsync(await RefreshAsync(client, original.RefreshToken));

        Assert.NotEqual(original.RefreshToken, refreshed.RefreshToken);

        await using var db = postgres.CreateContext();
        var retired = await FindTokenAsync(db, original.RefreshToken);
        var issued = await FindTokenAsync(db, refreshed.RefreshToken);

        Assert.NotNull(retired.RevokedAt);

        // The link is what makes a stolen token traceable: without it a revoked
        // row says only that it was revoked, not what replaced it.
        Assert.Equal(issued.Id, retired.ReplacedByTokenId);
    }

    [Fact]
    public async Task A_rotated_token_cannot_be_exchanged_twice()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);
        await RefreshAsync(client, original.RefreshToken);

        var reuse = await RefreshAsync(client, original.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    [Fact]
    public async Task Rotation_keeps_the_session_in_a_single_family()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);

        await RefreshAsync(client, original.RefreshToken);

        await using var db = postgres.CreateContext();
        var families = await db.RefreshTokens.Select(token => token.FamilyId).Distinct().ToListAsync(Ct);

        // Two rows, one session. If rotation started a new family, replaying an
        // old token could no longer reach the tokens that succeeded it.
        Assert.Equal(2, await db.RefreshTokens.CountAsync(Ct));
        Assert.Single(families);
    }

    // --- replay detection ---

    [Fact]
    public async Task Replaying_a_rotated_token_ends_the_whole_session()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);
        var refreshed = await ReadSessionAsync(await RefreshAsync(client, original.RefreshToken));

        // Someone presents the old token: either it was stolen before rotation,
        // or the legitimate client retried. We cannot tell, so both holders lose.
        await RefreshAsync(client, original.RefreshToken);

        var afterReplay = await RefreshAsync(client, refreshed.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.True(await db.RefreshTokens.AllAsync(token => token.RevokedAt != null, Ct));
    }

    [Fact]
    public async Task An_expired_token_is_refused_without_revoking_the_family()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);

        await using (var db = postgres.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "update refresh_tokens set expires_at = now() - interval '1 day'", Ct);
        }

        var response = await RefreshAsync(client, original.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var verify = postgres.CreateContext();

        // Coming back after a month is not an attack. Treating expiry as theft
        // would revoke a family for the most ordinary reason a token fails.
        Assert.Null((await verify.RefreshTokens.SingleAsync(Ct)).RevokedAt);
    }

    [Fact]
    public async Task Every_refresh_failure_looks_identical()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);
        await RefreshAsync(client, original.RefreshToken);

        var replayed = await RefreshAsync(client, original.RefreshToken);
        var neverIssued = await RefreshAsync(client, "a-token-that-was-never-issued");

        // A holder of a stolen token must not be able to learn whether it was
        // ever valid, or whether the theft has been noticed.
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, neverIssued.StatusCode);
        Assert.Equal(
            await ProblemBody.WithoutTraceIdAsync(replayed, Ct),
            await ProblemBody.WithoutTraceIdAsync(neverIssued, Ct));
    }

    [Fact]
    public async Task Two_simultaneous_refreshes_cannot_both_succeed()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);

        // Four rather than two: the requests are only as concurrent as the
        // scheduler makes them, and more of them raises the chance that two
        // genuinely read the token before either writes — which is the
        // interleaving the conditional update exists to survive.
        var responses = await Task.WhenAll(Enumerable
            .Range(0, 4)
            .Select(_ => RefreshAsync(client, original.RefreshToken)));

        // Without the transaction and the conditional update, both could rotate
        // the same token and the family would fork into two live chains — which
        // would mean a stolen token could quietly coexist with the real one.
        // The loser is treated as a replay, so in practice this race signs the
        // client out entirely; that is ADR-0003's deliberate trade.
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
    }

    // --- sign-out ---

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using var client = _factory.CreateClient();
        var session = await RegisterAsync(client);

        var logout = await LogoutAsync(client, session.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, session.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_revokes_the_rest_of_the_chain_too()
    {
        using var client = _factory.CreateClient();
        var original = await RegisterAsync(client);
        var refreshed = await ReadSessionAsync(await RefreshAsync(client, original.RefreshToken));

        await LogoutAsync(client, refreshed.RefreshToken);

        await using var db = postgres.CreateContext();

        // Every token in the family, not just the one presented — otherwise an
        // earlier link in the chain could still be exchanged and the logout
        // would not have ended anything.
        Assert.True(await db.RefreshTokens.AllAsync(token => token.RevokedAt != null, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Signing_out_always_succeeds(bool withAKnownToken)
    {
        using var client = _factory.CreateClient();
        var session = await RegisterAsync(client);
        var token = withAKnownToken ? session.RefreshToken : "a-token-that-was-never-issued";

        // Idempotent by nature: a client retrying after a dropped connection
        // must not get an error, and an unknown token must not be distinguishable
        // from a real one.
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(client, token)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_leaves_other_sessions_signed_in()
    {
        using var client = _factory.CreateClient();
        var laptop = await RegisterAsync(client);
        var phone = await ReadSessionAsync(await LoginAsync(client));

        await LogoutAsync(client, laptop.RefreshToken);

        // Signing out on one device must not sign out the others: that is why
        // logout revokes a family rather than every token the user holds.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, phone.RefreshToken)).StatusCode);
    }

    // --- helpers ---

    private static async Task<AuthenticationResponse> RegisterAsync(HttpClient client) =>
        await ReadSessionAsync(await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterUserRequest(Email, "Parker", Password),
            Ct));

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new LoginUserRequest(Email, Password), Ct);

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshSessionRequest(refreshToken), Ct);

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutUserRequest(refreshToken), Ct);

    private static async Task<NextMovie.Api.Domain.RefreshToken> FindTokenAsync(
        NextMovie.Api.Infrastructure.Persistence.NextMovieDbContext db,
        string refreshToken)
    {
        var hash = RefreshTokenFactory.Hash(refreshToken);

        return await db.RefreshTokens.SingleAsync(token => token.TokenHash == hash, Ct);
    }

    private static async Task<AuthenticationResponse> ReadSessionAsync(HttpResponseMessage response)
    {
        var session = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(Ct);

        Assert.NotNull(session);
        return session;
    }
}
