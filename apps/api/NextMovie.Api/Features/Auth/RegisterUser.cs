using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Authentication;
using NextMovie.Api.Infrastructure.Persistence;
using Npgsql;

namespace NextMovie.Api.Features.Auth;

/// <summary>
/// Creates an account and signs it in.
/// </summary>
/// <remarks>
/// Registration returns a session rather than asking the client to sign in
/// afterwards. The alternative would make the caller hold the plaintext password
/// for a second round trip, which is a worse place for it to be than in the one
/// request that had to carry it anyway.
/// </remarks>
public static class RegisterUser
{
    private const int MaxDisplayNameLength = 100;

    // RFC 5321's practical limit, matching the column width in UserConfiguration.
    private const int MaxEmailLength = 320;

    private static readonly EmailAddressAttribute EmailFormat = new();

    /// <summary>The unique index that decides whether an email is already taken.</summary>
    private const string EmailUniqueIndex = "ix_users_normalized_email";

    /// <summary>Registers the account creation endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/register", HandleAsync)
            .WithName(nameof(RegisterUser))
            .WithSummary("Create an account")
            .WithDescription(
                "Creates a new account with an email and password, and returns an "
                + "access token and refresh token for the new session.")
            // Declared explicitly: TypedResults.Problem carries no status
            // metadata of its own, so without this the conflict is invisible in
            // the OpenAPI document and therefore in the generated TypeScript
            // client that ADR-0002 builds from it.
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Results<Created<AuthenticationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        RegisterUserRequest request,
        NextMovieDbContext db,
        IPasswordHasher<User> passwordHasher,
        SessionIssuer sessions,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var normalizedEmail = User.NormalizeEmail(request.Email!);

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return EmailAlreadyRegistered();
        }

        var user = new User
        {
            Email = request.Email!,
            DisplayName = request.DisplayName!.Trim(),
        };

        // Hashed after construction rather than in the initialiser because the
        // hasher salts per user and takes the entity itself.
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);

        db.Users.Add(user);
        var session = sessions.Issue(user);

        try
        {
            // One save for the account and its first refresh token: a user who
            // exists but whose session was never persisted would be a silent
            // half-registration.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsEmailAlreadyTaken(ex))
        {
            // The check above narrows the window; only the unique index closes
            // it. Two simultaneous registrations for the same address both pass
            // the check, and exactly one reaches this line.
            return EmailAlreadyRegistered();
        }

        // No Location header: there is no endpoint that serves a user by id yet.
        // Pointing at /api/v1/users/me would be wrong — it identifies whoever is
        // calling, not the account just created.
        return TypedResults.Created((string?)null, session.Response);
    }

    private static ProblemHttpResult EmailAlreadyRegistered() => TypedResults.Problem(
        title: "Email already registered",
        detail: "An account already exists for that email address.",
        statusCode: StatusCodes.Status409Conflict);

    /// <remarks>
    /// Matched on the index name so that a unique violation from anywhere else
    /// in the same save — a refresh token hash collision, say — still surfaces as
    /// the unexpected failure it would be, rather than being reported to the
    /// user as a taken email address.
    /// </remarks>
    private static bool IsEmailAlreadyTaken(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EmailUniqueIndex,
        };

    private static Dictionary<string, string[]> Validate(RegisterUserRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors[nameof(request.Email)] = ["An email address is required."];
        }
        else if (request.Email.Trim().Length > MaxEmailLength)
        {
            errors[nameof(request.Email)] = [$"An email address may be at most {MaxEmailLength} characters."];
        }
        else if (!EmailFormat.IsValid(request.Email.Trim()))
        {
            errors[nameof(request.Email)] = ["That is not a valid email address."];
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors[nameof(request.DisplayName)] = ["A display name is required."];
        }
        else if (request.DisplayName.Trim().Length > MaxDisplayNameLength)
        {
            errors[nameof(request.DisplayName)] = [$"A display name may be at most {MaxDisplayNameLength} characters."];
        }

        if (PasswordPolicy.Validate(request.Password) is { } passwordError)
        {
            errors[nameof(request.Password)] = [passwordError];
        }

        return errors;
    }
}

/// <summary>Details needed to create an account.</summary>
/// <param name="Email">Email address. Must be unique, ignoring case.</param>
/// <param name="DisplayName">Name to show in the UI.</param>
/// <param name="Password">Chosen password. See <see cref="PasswordPolicy"/> for the rules.</param>
public sealed record RegisterUserRequest(string? Email, string? DisplayName, string? Password);
