using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Auth;

/// <summary>
/// Ends one session.
/// </summary>
/// <remarks>
/// Revokes the presented token's whole family, not just the token itself. A
/// family is one session's rotation chain — one device — so this ends exactly
/// that device's session and leaves other devices signed in. Revoking only the
/// presented token would leave earlier links in the chain live, and a logout
/// that does not actually log you out is worse than none.
/// <para>
/// The access token cannot be revoked: it is a stateless JWT, so it stays valid
/// until it expires (at most fifteen minutes). Signing out therefore ends the
/// ability to obtain new access tokens, not the current one. Shortening that
/// window further would mean introducing server-side access-token state, which
/// is the cost stateless tokens exist to avoid.
/// </para>
/// </remarks>
public static class LogoutUser
{
    /// <summary>Registers the sign-out endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/logout", HandleAsync)
            .WithName(nameof(LogoutUser))
            .WithSummary("Sign out")
            .WithDescription(
                "Revokes the session the refresh token belongs to. Always succeeds, "
                + "whether or not the token was still valid.");

        return app;
    }

    private static async Task<Results<NoContent, ValidationProblem>> HandleAsync(
        LogoutUserRequest request,
        NextMovieDbContext db,
        SessionRevoker revoker,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            // The request is structurally wrong, which says nothing about any
            // token — unlike the responses below, which deliberately say nothing
            // about whether the token existed.
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.RefreshToken)] = ["A refresh token is required."],
            });
        }

        var tokenHash = RefreshTokenFactory.Hash(request.RefreshToken);

        var presented = await db.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (presented is not null)
        {
            await revoker.RevokeFamilyAsync(presented.FamilyId, time.GetUtcNow(), cancellationToken);
        }

        // 204 either way, including for a token that never existed or was
        // already revoked. Signing out is idempotent by nature — a client
        // retrying after a dropped connection must not get an error — and any
        // other answer would let an unauthenticated caller test whether a token
        // is real.
        return TypedResults.NoContent();
    }
}

/// <summary>The session to end.</summary>
/// <param name="RefreshToken">The refresh token held by the client signing out.</param>
public sealed record LogoutUserRequest(string? RefreshToken);
