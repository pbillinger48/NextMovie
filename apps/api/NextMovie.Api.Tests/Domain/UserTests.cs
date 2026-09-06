using NextMovie.Api.Domain;

namespace NextMovie.Api.Tests.Domain;

/// <summary>
/// Tests the email normalisation invariant.
/// </summary>
/// <remarks>
/// Worth testing because uniqueness is enforced on <c>NormalizedEmail</c> while
/// callers naturally set <c>Email</c>. If those two ever diverge, the database
/// accepts a row that looks fine and silently defeats the unique index — a bug
/// that surfaces as duplicate accounts, long after the code that caused it.
/// </remarks>
public sealed class UserTests
{
    private static User NewUser(string email) =>
        new() { Email = email, DisplayName = "Test" };

    [Fact]
    public void Setting_email_derives_the_normalized_form()
    {
        var user = NewUser("Parker@Example.com");

        Assert.Equal("Parker@Example.com", user.Email);
        Assert.Equal("PARKER@EXAMPLE.COM", user.NormalizedEmail);
    }

    [Theory]
    [InlineData("parker@example.com")]
    [InlineData("PARKER@EXAMPLE.COM")]
    [InlineData("Parker@Example.Com")]
    [InlineData("  parker@example.com  ")]
    public void Case_and_whitespace_variants_normalise_identically(string input)
    {
        // Every one of these must collide on the unique index.
        Assert.Equal("PARKER@EXAMPLE.COM", NewUser(input).NormalizedEmail);
    }

    [Fact]
    public void Email_keeps_the_casing_the_user_typed()
    {
        // Normalisation is for uniqueness, not for rewriting the user's input.
        Assert.Equal("Parker@Example.com", NewUser("Parker@Example.com").Email);
    }

    [Fact]
    public void Email_is_trimmed()
    {
        Assert.Equal("parker@example.com", NewUser("  parker@example.com  ").Email);
    }

    [Fact]
    public void Reassigning_email_updates_the_normalized_form()
    {
        // An email change must not leave the old normalised value behind, or the
        // account becomes unreachable at its new address and still occupies the
        // old one.
        var user = NewUser("old@example.com");
        user.Email = "new@example.com";

        Assert.Equal("NEW@EXAMPLE.COM", user.NormalizedEmail);
    }

    [Fact]
    public void Normalisation_is_culture_invariant()
    {
        // Turkish is the standard counterexample: a culture-sensitive ToUpper
        // maps "i" to "İ", so under a Turkish locale "i@x.com" and "I@x.com"
        // would normalise differently and both could register. Uniqueness must
        // not depend on the server's locale.
        Assert.Equal("I@EXAMPLE.COM", User.NormalizeEmail("i@example.com"));
    }

    [Fact]
    public void A_new_user_has_no_password_until_one_is_set()
    {
        // Null means "no password", which is what makes external-only sign-in
        // possible later without a schema change (ADR-0003).
        Assert.Null(NewUser("parker@example.com").PasswordHash);
    }
}
