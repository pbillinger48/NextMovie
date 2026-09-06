using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Auth;

/// <summary>
/// Exchanges an email and password for a session.
/// </summary>
/// <remarks>
/// Every failure — unknown address, wrong password, locked-out account — returns
/// the same 401 with the same body. ADR-0003 requires that: a response that
/// distinguished "no such account" from "wrong password" would turn this
/// endpoint into an email-address oracle, which is how credential-stuffing lists
/// get filtered down to accounts worth attacking.
/// </remarks>
public static class LoginUser
{
    /// <summary>
    /// A real hash, verified against when no account matches, so that an unknown
    /// address costs the same PBKDF2 work as a known one.
    /// </summary>
    /// <remarks>
    /// Without this, the identical responses above would still leak: returning
    /// in a millisecond means "no such user", returning in a hundred means "user
    /// exists, wrong password". This equalises the dominant cost. It is not
    /// constant-time in the cryptographic sense — database and network variance
    /// remain — but it removes the difference an attacker can actually measure.
    /// <para>
    /// Computed once at type initialisation. The password is a literal because
    /// nothing ever verifies against it successfully; it exists only to produce
    /// a well-formed hash of the right cost.
    /// </para>
    /// </remarks>
    private static readonly User UnknownUser = new()
    {
        Email = "unknown@nextmovie.invalid",
        DisplayName = "unknown",
    };

    private static readonly string UnknownUserPasswordHash =
        new PasswordHasher<User>().HashPassword(UnknownUser, "there-is-no-account-with-this-password");

    /// <summary>Registers the sign-in endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/login", HandleAsync)
            .WithName(nameof(LoginUser))
            .WithSummary("Sign in")
            .WithDescription(
                "Exchanges an email and password for an access token and refresh token. "
                + "Repeated failures temporarily lock the account.")
            // As in RegisterUser: without this the 401 never reaches the
            // OpenAPI document, and the generated client cannot see the one
            // failure every caller has to handle.
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<AuthenticationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        LoginUserRequest request,
        NextMovieDbContext db,
        IPasswordHasher<User> passwordHasher,
        SessionIssuer sessions,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { Count: > 0 } errors)
        {
            // Validation only reports what is structurally missing. It never
            // reflects anything about the account, so it cannot be used to probe.
            return TypedResults.ValidationProblem(errors);
        }

        var normalizedEmail = User.NormalizeEmail(request.Email!);

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(UnknownUser, UnknownUserPasswordHash, request.Password!);
            return InvalidCredentials();
        }

        var now = time.GetUtcNow();

        if (user.IsLockedOut(now))
        {
            // Checked before verification, so a locked account cannot be signed
            // into even with the correct password — otherwise the lockout would
            // stop only the attacker who has not yet guessed it.
            //
            // The response does not say the account is locked. Saying so would
            // confirm the address exists, undoing the enumeration resistance
            // above. The cost is real and accepted: a locked-out owner is told
            // their credentials are wrong when they are not, and has to wait out
            // a window nothing tells them about. A "check your email" flow is
            // the usual fix and needs a mailer we do not have yet.
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash ?? UnknownUserPasswordHash,
            request.Password!);

        if (verification == PasswordVerificationResult.Failed)
        {
            // This write is the one measurable difference left between a wrong
            // password and an unknown address: the former costs an UPDATE the
            // latter does not. It is a millisecond or two against the ~100ms of
            // PBKDF2 that both paths pay, so it is noise rather than a signal —
            // but it is the residual, and closing it would mean writing a row on
            // behalf of an account that does not exist.
            user.RecordFailedLogin(now);
            await db.SaveChangesAsync(cancellationToken);

            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The stored hash used an older format or a lower work factor. This
            // is the only moment the plaintext is available to upgrade it, which
            // is what makes raising the iteration count later a configuration
            // change rather than a forced password reset for everyone.
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        }

        user.RecordSuccessfulLogin(now);

        var session = sessions.Issue(user);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(session);
    }

    private static ProblemHttpResult InvalidCredentials() => TypedResults.Problem(
        title: "Invalid credentials",
        detail: "The email address or password is incorrect.",
        statusCode: StatusCodes.Status401Unauthorized);

    private static Dictionary<string, string[]> Validate(LoginUserRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(request.Email)] = ["An email address is required."];
        }

        // Deliberately not checked against PasswordPolicy. Sign-in must accept
        // any password an account might already have, including ones predating a
        // rule change — rejecting them here would lock out real users and, worse,
        // would answer "does this account have a policy-compliant password?".
        if (string.IsNullOrEmpty(request.Password))
        {
            errors[nameof(request.Password)] = ["A password is required."];
        }

        return errors;
    }
}

/// <summary>Credentials presented at sign-in.</summary>
/// <param name="Email">Email address the account was registered with. Case-insensitive.</param>
/// <param name="Password">The account's password.</param>
public sealed record LoginUserRequest(string? Email, string? Password);
