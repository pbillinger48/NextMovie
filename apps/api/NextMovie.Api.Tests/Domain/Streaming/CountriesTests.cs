using NextMovie.Api.Domain.Streaming;

namespace NextMovie.Api.Tests.Domain.Streaming;

/// <summary>
/// Tests the list of countries somebody may say they watch from.
/// </summary>
/// <remarks>
/// It comes from the framework's ISO 3166-1 data rather than a list written by
/// hand, which is the right call and also an act of trust in a dependency. These
/// pin down what the rest of the code assumes about the result.
/// </remarks>
public sealed class CountriesTests
{
    [Theory]
    [InlineData("US")]
    [InlineData("GB")]
    [InlineData("DE")]
    [InlineData("JP")]
    [InlineData("AU")]
    public void Recognises_countries_people_actually_live_in(string code)
    {
        Assert.True(Countries.IsKnown(code));
    }

    [Theory]
    [InlineData("us")]
    [InlineData("gB")]
    public void Accepts_whatever_case_it_is_given(string code)
    {
        // Clients send what the user typed. Rejecting "us" would be a validation
        // failure about nothing.
        Assert.True(Countries.IsKnown(code));
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("USA")]
    [InlineData("")]
    [InlineData("1")]
    public void Refuses_anything_that_is_not_a_country(string code)
    {
        Assert.False(Countries.IsKnown(code));
    }

    [Fact]
    public void Every_entry_is_an_alpha_two_code_with_a_name()
    {
        Assert.All(Countries.All, country =>
        {
            // Availability sources speak alpha-2, and the database column is
            // fixed at two characters.
            Assert.Equal(2, country.Code.Length);
            Assert.False(string.IsNullOrWhiteSpace(country.Name));
        });
    }

    [Fact]
    public void Countries_appear_once_each()
    {
        // Several cultures map to one country — en-US and es-US both mean the
        // United States, and a dropdown listing it twice would look broken.
        Assert.Equal(
            Countries.All.Length,
            Countries.All.Select(country => country.Code).Distinct().Count());
    }

    [Fact]
    public void The_list_is_ordered_by_name()
    {
        // Rendered as a dropdown. A hash-based collection would enumerate in
        // whatever order its buckets fell in — stable enough to look intentional
        // and arbitrary enough to be useless.
        Assert.Equal(
            Countries.All.Select(country => country.Name).Order(StringComparer.Ordinal),
            Countries.All.Select(country => country.Name));
    }
}
