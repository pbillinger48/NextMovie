using NextMovie.Api.Domain.Authentication;

namespace NextMovie.Api.Tests.Domain;

/// <summary>
/// Tests the password rules at their boundaries.
/// </summary>
/// <remarks>
/// Only the edges are interesting: an off-by-one here either rejects a password
/// the policy documents as acceptable, or accepts one it does not.
/// </remarks>
public sealed class PasswordPolicyTests
{
    [Fact]
    public void A_password_at_the_minimum_length_is_accepted()
    {
        Assert.Null(PasswordPolicy.Validate(new string('a', PasswordPolicy.MinLength)));
    }

    [Fact]
    public void A_password_one_character_short_is_rejected()
    {
        Assert.NotNull(PasswordPolicy.Validate(new string('a', PasswordPolicy.MinLength - 1)));
    }

    [Fact]
    public void A_password_at_the_maximum_length_is_accepted()
    {
        Assert.Null(PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength)));
    }

    [Fact]
    public void A_password_one_character_over_is_rejected()
    {
        // The cap protects the hasher from being handed unbounded input on an
        // unauthenticated endpoint.
        Assert.NotNull(PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_password_is_rejected(string? password)
    {
        Assert.NotNull(PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Whitespace_counts_as_password_content()
    {
        // Trimming would silently change the user's password to something they
        // did not choose, and could take it under the minimum length.
        Assert.Null(PasswordPolicy.Validate("            "));
    }

    [Fact]
    public void No_composition_rules_are_imposed()
    {
        // A long all-lowercase passphrase is exactly what NIST SP 800-63B
        // recommends encouraging. If this ever fails, someone has added a
        // "must contain a symbol" rule that the policy deliberately rejects.
        Assert.Null(PasswordPolicy.Validate("correct horse battery staple"));
    }
}
