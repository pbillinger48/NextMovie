using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Auth;

/// <summary>
/// Exchanges a refresh token for a new session.
/// </summary>
/// <remarks>
/// Named for the operation rather than for the token, because
/// <c>RefreshToken</c> is already the entity and a slice of the same name would
/// make every reference ambiguous.
/// <para>
/// Every refresh rotates: the presented token is revoked and a new one is issued
/// into the same family. Presenting an already-rotated token is treated as theft
/// and revokes the entire family — see ADR-0003. The innocent explanations for a
/// replay (a retry, two requests racing) are indistinguishable from the
/// dangerous one, and signing both holders out is the only safe response when we
/// cannot tell which is the legitimate user.
/// </para>
/// </remarks>
public static class RefreshSession
{
    /// <summary>Registers the refresh endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/refresh", HandleAsync)
            .WithName(nameof(RefreshSession))
            .WithSummary("Refresh a session")
            .WithDescription(
                "Exchanges a refresh token for a new access token and a new refresh token. "
                + "The presented token is revoked; presenting it again ends the session.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<AuthenticationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        RefreshSessionRequest request,
        NextMovieDbContext db,
        SessionIssuer sessions,
        SessionRevoker revoker,
        TimeProvider time,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.RefreshToken)] = ["A refresh token is required."],
            });
        }

        var tokenHash = RefreshTokenFactory.Hash(request.RefreshToken);

        var presented = await db.RefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (presented is null)
        {
            // Nothing to revoke and nothing to learn from: a token we have never
            // issued tells us only that someone guessed 256 bits wrong.
            return InvalidRefreshToken();
        }

        var now = time.GetUtcNow();

        if (presented.RevokedAt is not null)
        {
            logger.LogWarning(
                "Refresh token replay detected for family {FamilyId}; revoking the family",
                presented.FamilyId);

            await revoker.RevokeFamilyAsync(presented.FamilyId, now, cancellationToken);

            return InvalidRefreshToken();
        }

        if (!presented.IsActive(now))
        {
            // Expired rather than replayed. Nothing is revoked: an expired token
            // is already unusable, and treating ordinary expiry as an attack
            // would sign people out for the crime of coming back after a month.
            return InvalidRefreshToken();
        }

        // The read above and the write below are not atomic on their own, so two
        // requests presenting the same token could both reach this point. The
        // transaction plus the conditional update below is what stops them both
        // succeeding and forking the family into two live chains.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var session = sessions.Issue(presented.User, presented.FamilyId);

        var rotated = await db.RefreshTokens
            .Where(token => token.Id == presented.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.ReplacedByTokenId, session.RefreshToken.Id),
                cancellationToken);

        if (rotated == 0)
        {
            // Another request rotated this token between our read and our write.
            // That is the same signal as a replay, and gets the same answer.
            //
            // The new token is detached rather than merely left unsaved, so a
            // later SaveChangesAsync on this context cannot resurrect a session
            // we have decided not to issue.
            db.Entry(session.RefreshToken).State = EntityState.Detached;

            logger.LogWarning(
                "Concurrent refresh detected for family {FamilyId}; revoking the family",
                presented.FamilyId);

            await revoker.RevokeFamilyAsync(presented.FamilyId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return InvalidRefreshToken();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(session.Response);
    }

    /// <remarks>
    /// One response for every failure — unknown, expired, revoked, replayed.
    /// A client cannot act differently on any of them (all of them mean "sign in
    /// again"), and distinguishing them would tell an attacker holding a stolen
    /// token whether it was ever valid and whether their theft has been noticed.
    /// </remarks>
    private static ProblemHttpResult InvalidRefreshToken() => TypedResults.Problem(
        title: "Invalid refresh token",
        detail: "The refresh token is invalid or has expired. Sign in again.",
        statusCode: StatusCodes.Status401Unauthorized);
}

/// <summary>The refresh token to exchange.</summary>
/// <param name="RefreshToken">The refresh token issued by register, login, or a previous refresh.</param>
public sealed record RefreshSessionRequest(string? RefreshToken);
