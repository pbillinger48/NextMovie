namespace NextMovie.Api.Domain.Authentication;

/// <summary>
/// What counts as an acceptable password.
/// </summary>
/// <remarks>
/// Follows NIST SP 800-63B: length is the requirement, composition is not.
/// Rules demanding a symbol and a digit measurably push people toward
/// predictable shapes like <c>Password1!</c> — they raise the friction and
/// lower the entropy, which is the wrong trade in both directions.
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>
    /// Minimum length, above the NIST floor of 8.
    /// </summary>
    /// <remarks>
    /// Twelve because this is a greenfield product with no existing accounts to
    /// grandfather in. Raising a minimum later only applies to new passwords, so
    /// the cheapest time to set it high is before anyone has registered.
    /// </remarks>
    public const int MinLength = 12;

    /// <summary>
    /// Maximum length.
    /// </summary>
    /// <remarks>
    /// Not a security limit — it is a cost limit. PBKDF2 hashes whatever it is
    /// given, so an unbounded password is a cheap way to make the server do
    /// expensive work on an unauthenticated endpoint. NIST asks for at least 64
    /// characters to be accepted; 128 clears that comfortably.
    /// </remarks>
    public const int MaxLength = 128;

    /// <summary>
    /// Validates a candidate password.
    /// </summary>
    /// <returns>
    /// Null when the password is acceptable, otherwise the message to show the
    /// user.
    /// </returns>
    public static string? Validate(string? password) => password switch
    {
        null or "" => "A password is required.",

        // Length is counted in characters, not bytes, and before any trimming.
        // Leading and trailing spaces are legitimate password characters and
        // trimming them would silently change what the user chose.
        { Length: < MinLength } => $"A password must be at least {MinLength} characters.",
        { Length: > MaxLength } => $"A password may be at most {MaxLength} characters.",
        _ => null,
    };
}
