namespace NextMovie.Api.Domain;

/// <summary>
/// A third-party identity that can sign in as a given user.
/// </summary>
/// <remarks>
/// Additive, exactly as ADR-0003 anticipated: an account may have a password, one
/// or more external logins, or both, and adding this table changed nothing about
/// the password flow.
/// </remarks>
public class UserExternalLogin
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public User User { get; init; } = null!;

    public required ExternalLoginProvider Provider { get; init; }

    /// <summary>
    /// The provider's own identifier for this person — Google's <c>sub</c> claim.
    /// </summary>
    /// <remarks>
    /// This, not the email address, is what an identity is keyed on. Google's
    /// subject is stable for the life of the account while an email address is
    /// not: keying on email would mean someone who changes their Google address
    /// arrives as a stranger, and — far worse — that whoever later acquires their
    /// old address arrives as them. See ADR-0005.
    /// </remarks>
    public required string ProviderSubject { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>An identity provider we accept sign-ins from.</summary>
/// <remarks>
/// Persisted by name rather than by number, so the column is readable in psql and
/// so reordering this enum cannot silently repoint every existing row at a
/// different provider.
/// </remarks>
public enum ExternalLoginProvider
{
    Google = 1,
}
