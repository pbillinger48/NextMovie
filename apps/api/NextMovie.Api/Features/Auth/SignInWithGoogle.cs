using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Auth.Google;
using NextMovie.Api.Infrastructure.Persistence;
using Npgsql;

namespace NextMovie.Api.Features.Auth;

/// <summary>
/// Signs in with a Google ID token, creating or linking an account as needed.
/// </summary>
/// <remarks>
/// Per ADR-0005 the client completes the Google flow itself and presents the
/// resulting ID token here. The API never talks to Google's token endpoint and
/// handles no redirects; what it does is verify the token and then behave exactly
/// like <see cref="LoginUser"/> — the session that comes out is an ordinary
/// NextMovie session, refreshed and revoked like any other.
/// </remarks>
public static class SignInWithGoogle
{
    // Both are the account's own uniqueness rules, and either can lose a race
    // with a concurrent first-time sign-in.
    private const string ExternalLoginUniqueIndex = "ix_user_external_logins_provider_provider_subject";
    private const string EmailUniqueIndex = "ix_users_normalized_email";

    private const int MaxDisplayNameLength = 100;
    private const int MaxProfileImageUrlLength = 2048;

    /// <summary>Registers the Google sign-in endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/google", HandleAsync)
            .WithName(nameof(SignInWithGoogle))
            .WithSummary("Sign in with Google")
            .WithDescription(
                "Verifies a Google ID token and returns a NextMovie session, creating "
                + "an account or linking the Google identity to an existing one.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok<AuthenticationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        SignInWithGoogleRequest request,
        NextMovieDbContext db,
        IGoogleIdTokenVerifier googleTokens,
        SessionIssuer sessions,
        TimeProvider time,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.IdToken)] = ["A Google ID token is required."],
            });
        }

        var identity = await googleTokens.VerifyAsync(request.IdToken, cancellationToken);

        if (identity is null)
        {
            return CouldNotVerify();
        }

        if (!identity.EmailVerified)
        {
            // ADR-0005 makes linking conditional on Google having verified the
            // address, because an unverified one is an unproven claim to own it —
            // and acting on it is how this class of bug becomes an account
            // takeover. Applied to account *creation* as well as linking: an
            // account whose address was never verified by anyone would block the
            // real owner from ever registering it.
            logger.LogWarning("Refused a Google sign-in with an unverified email address");

            return EmailNotVerified();
        }

        try
        {
            return await IssueSessionAsync(db, sessions, identity, time.GetUtcNow(), cancellationToken);
        }
        catch (DbUpdateException exception) when (IsRaceWithAnotherSignIn(exception))
        {
            // Another request created this account, or claimed this address,
            // between our read and our write — a double-clicked sign-in button,
            // usually. The second attempt finds what the first created.
            //
            // The tracker is cleared first because it still holds the entities
            // the failed save tried to insert, and saving them again would fail
            // exactly the same way.
            db.ChangeTracker.Clear();

            return await IssueSessionAsync(db, sessions, identity, time.GetUtcNow(), cancellationToken);
        }
    }

    private static async Task<Results<Ok<AuthenticationResponse>, ValidationProblem, ProblemHttpResult>> IssueSessionAsync(
        NextMovieDbContext db,
        SessionIssuer sessions,
        GoogleIdentity identity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Looked up by subject, never by email: this is what makes a Google
        // address change harmless, and what stops whoever inherits an old address
        // from inheriting the account with it.
        var existingLogin = await db.UserExternalLogins
            .Include(login => login.User)
            .FirstOrDefaultAsync(
                login => login.Provider == ExternalLoginProvider.Google
                    && login.ProviderSubject == identity.Subject,
                cancellationToken);

        var user = existingLogin?.User;

        if (user is null)
        {
            var normalizedEmail = User.NormalizeEmail(identity.Email);

            user = await db.Users
                .FirstOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);

            if (user is null)
            {
                user = CreateAccountFor(identity);
                db.Users.Add(user);
            }

            // Existing account, verified matching address: link rather than
            // refuse, so both routes reach one account and the password — if
            // there is one — keeps working. Nothing about the existing profile is
            // overwritten; a display name the user chose here outranks whatever
            // Google has.
            db.UserExternalLogins.Add(new UserExternalLogin
            {
                UserId = user.Id,
                Provider = ExternalLoginProvider.Google,
                ProviderSubject = identity.Subject,
                CreatedAt = now,
            });
        }

        user.RecordSuccessfulLogin(now);

        var session = sessions.Issue(user);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(session.Response);
    }

    /// <remarks>
    /// No password is set. <c>PasswordHash</c> stays null, which ADR-0003 made
    /// possible precisely so an account could exist without one; setting a
    /// password later is additive.
    /// </remarks>
    private static User CreateAccountFor(GoogleIdentity identity) => new()
    {
        Email = identity.Email,
        DisplayName = DisplayNameFor(identity),

        // Dropped rather than truncated if it is absurdly long: half a URL is not
        // an image, and a broken avatar is worse than none.
        ProfileImageUrl = identity.PictureUrl is { Length: <= MaxProfileImageUrlLength }
            ? identity.PictureUrl
            : null,
    };

    /// <remarks>
    /// Google supplies a name for most accounts but not all, so the address is
    /// the fallback — the part before the @, which is at least recognisable to
    /// its owner. Truncated to the column width, because a name long enough to
    /// overflow it should not fail a sign-in.
    /// </remarks>
    private static string DisplayNameFor(GoogleIdentity identity)
    {
        var name = string.IsNullOrWhiteSpace(identity.Name)
            ? identity.Email.Split('@')[0]
            : identity.Name.Trim();

        return name.Length > MaxDisplayNameLength ? name[..MaxDisplayNameLength] : name;
    }

    private static bool IsRaceWithAnotherSignIn(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ExternalLoginUniqueIndex or EmailUniqueIndex,
        };

    private static ProblemHttpResult CouldNotVerify() => TypedResults.Problem(
        title: "Google sign-in failed",
        detail: "The Google sign-in could not be verified. Please try again.",
        statusCode: StatusCodes.Status401Unauthorized);

    private static ProblemHttpResult EmailNotVerified() => TypedResults.Problem(
        title: "Google email not verified",
        detail: "This Google account's email address is not verified. Verify it with Google and try again.",
        statusCode: StatusCodes.Status403Forbidden);
}

/// <summary>The Google ID token to sign in with.</summary>
/// <param name="IdToken">An ID token obtained by the client from Google.</param>
public sealed record SignInWithGoogleRequest(string? IdToken);
