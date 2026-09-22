using NextMovie.Api.Domain.Recommendations.Evaluation;

namespace NextMovie.Api.Tests.Domain.Recommendations.Evaluation;

/// <summary>
/// Tests the split that makes two evaluation runs comparable.
/// </summary>
/// <remarks>
/// Determinism is the whole point. If the same seed gives two different splits,
/// every measured improvement is indistinguishable from a different shuffle.
/// </remarks>
public sealed class HoldOutTests
{
    private static readonly IReadOnlyList<Guid> Library = [.. Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())];

    [Fact]
    public void The_same_seed_always_gives_the_same_split()
    {
        Assert.Equal(
            HoldOut.Choose(Library, 0.2, seed: 42),
            HoldOut.Choose(Library, 0.2, seed: 42));
    }

    [Fact]
    public void A_different_seed_gives_a_different_split()
    {
        // Not a law of the universe — two seeds could agree by chance — but with
        // twenty films drawn from a hundred the odds are vanishing, and a
        // generator that ignored its seed would fail here every time.
        Assert.NotEqual(
            HoldOut.Choose(Library, 0.2, seed: 1),
            HoldOut.Choose(Library, 0.2, seed: 2));
    }

    [Theory]
    [InlineData(0.2, 20)]
    [InlineData(0.5, 50)]
    [InlineData(1.0, 100)]
    public void The_fraction_decides_how_many_are_hidden(double fraction, int expected)
    {
        Assert.Equal(expected, HoldOut.Choose(Library, fraction, seed: 7).Count);
    }

    [Fact]
    public void A_small_library_still_hides_something()
    {
        // Rounding up. A fifth of three films is 0.6, and hiding nothing would
        // make the evaluation silently meaningless for anyone with a short
        // history rather than visibly limited.
        Assert.Single(HoldOut.Choose([.. Library.Take(3)], 0.2, seed: 7));
    }

    [Fact]
    public void It_never_hides_more_than_exists()
    {
        Assert.Equal(5, HoldOut.Choose([.. Library.Take(5)], 2.0, seed: 7).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public void Hiding_nothing_is_allowed(double fraction)
    {
        Assert.Empty(HoldOut.Choose(Library, fraction, seed: 7));
    }

    [Fact]
    public void An_empty_library_yields_an_empty_split()
    {
        Assert.Empty(HoldOut.Choose([], 0.2, seed: 7));
    }

    [Fact]
    public void Every_chosen_film_came_from_the_library_and_appears_once()
    {
        var chosen = HoldOut.Choose(Library, 0.3, seed: 99);

        Assert.Equal(chosen.Count, chosen.Distinct().Count());
        Assert.All(chosen, film => Assert.Contains(film, Library));
    }
}
