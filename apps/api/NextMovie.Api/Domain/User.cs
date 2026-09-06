using NextMovie.Api.Domain.Authentication;

namespace NextMovie.Api.Domain;

/// <summary>
/// An authenticated account.
/// </summary>
/// <remarks>
/// Deviates from <c>docs/database.md</c> in one respect, deliberately: there is
/// no <c>AuthenticationProvider</c> column. A single provider column would make
/// it structurally impossible for one account to have both a password and a
/// linked Google sign-in. Instead <see cref="PasswordHash"/> is nullable — null
/// simply means no password is set — and external sign-ins will arrive later as
/// an additive <c>user_external_logins</c> table, with no migration of existing
/// data and no change to the password flow. See ADR-0003.
/// </remarks>
public class User
{
    private string _email = string.Empty;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// The address exactly as the user typed it, used for display and for
    /// sending mail.
    /// </summary>
    /// <remarks>
    /// Assigning this also derives <see cref="NormalizedEmail"/>. That coupling
    /// is intentional: uniqueness is enforced on the normalised column, so a
    /// caller who set one without the other would create a row that looks valid
    /// and silently defeats the unique index. Making it impossible to forget is
    /// worth the small surprise of a setter with a side effect.
    /// </remarks>
    public required string Email
    {
        get => _email;
        set
        {
            _email = value.Trim();
            NormalizedEmail = NormalizeEmail(value);
        }
    }

    /// <summary>
    /// Upper-cased form of <see cref="Email"/>. Carries the unique index, and is
    /// the column every lookup should query.
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>Name shown in the UI.</summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// PBKDF2 hash produced by <c>PasswordHasher&lt;User&gt;</c>, or null when
    /// this account has no password (external sign-in only).
    /// </summary>
    /// <remarks>
    /// The stored format is versioned, so the iteration count can be raised
    /// later and existing hashes upgraded on next successful sign-in.
    /// </remarks>
    public string? PasswordHash { get; set; }

    public string? ProfileImageUrl { get; set; }

    /// <summary>
    /// Consecutive failed sign-in attempts, reset on success.
    /// </summary>
    /// <remarks>
    /// Lockout state lives on the user row rather than in a cache because it
    /// must survive process restarts — an attacker who could reset the counter
    /// by waiting for a deploy would defeat the point of having it.
    /// </remarks>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>When the current lockout expires, or null when not locked out.</summary>
    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Refresh tokens issued to this user, active and historical.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; init; } = [];

    /// <summary>
    /// Whether sign-in is currently barred for this account.
    /// </summary>
    /// <remarks>
    /// The lockout is read as "ends in the future", not as a flag, so it lapses
    /// on its own with no scheduled job to clear it.
    /// </remarks>
    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt > now;

    /// <summary>
    /// Records a failed sign-in attempt, locking the account once
    /// <see cref="AuthenticationPolicy.MaxFailedLoginAttempts"/> is reached.
    /// </summary>
    /// <remarks>
    /// The counter and the lockout window move together, which is why the
    /// setters are private. A caller that incremented the count and forgot the
    /// lockout would produce an account that can never be locked and never looks
    /// wrong in the database.
    /// </remarks>
    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;
        UpdatedAt = now;

        if (FailedLoginAttempts < AuthenticationPolicy.MaxFailedLoginAttempts)
        {
            return;
        }

        LockoutEndsAt = now + AuthenticationPolicy.LockoutDuration;

        // The counter resets with the lockout, so the next window costs a full
        // five attempts again. Leaving it at the maximum would mean one wrong
        // password after the lockout lapsed re-locked the account immediately,
        // which punishes the legitimate owner far more than an attacker.
        FailedLoginAttempts = 0;
    }

    /// <summary>Records a successful sign-in, clearing any failure state.</summary>
    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// The single definition of email normalisation. Invariant culture, because
    /// culture-sensitive casing would make uniqueness depend on the server's
    /// locale — the classic example being Turkish, where uppercasing "i" does
    /// not produce "I".
    /// </summary>
    public static string NormalizeEmail(string email) =>
        email.Trim().ToUpperInvariant();
}
