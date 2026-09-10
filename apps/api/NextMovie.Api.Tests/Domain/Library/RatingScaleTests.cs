using NextMovie.Api.Domain.Library;

namespace NextMovie.Api.Tests.Domain.Library;

/// <summary>
/// Tests the rating scale at its edges.
/// </summary>
/// <remarks>
/// The interesting cases are the ones just outside it: a zero, which would make
/// "unrated" and "hated it" indistinguishable, and a value between the half-steps,
/// which the database would refuse anyway but should never reach it.
/// </remarks>
public sealed class RatingScaleTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(4.5)]
    [InlineData(5.0)]
    public void Accepts_every_half_star(decimal rating)
    {
        Assert.Null(RatingScale.Validate(rating));
    }

    [Fact]
    public void Rejects_zero()
    {
        // Absence of an opinion is the absence of a row. If zero were allowed it
        // would mean both "no rating" and "the worst film I have seen", and the
        // recommendation engine could not tell them apart.
        Assert.NotNull(RatingScale.Validate(0m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5.5)]
    [InlineData(10)]
    public void Rejects_values_outside_the_scale(decimal rating)
    {
        Assert.NotNull(RatingScale.Validate(rating));
    }

    [Theory]
    [InlineData(4.3)]
    [InlineData(0.7)]
    [InlineData(2.25)]
    public void Rejects_values_between_the_half_steps(decimal rating)
    {
        Assert.NotNull(RatingScale.Validate(rating));
    }

    [Fact]
    public void Rejects_a_missing_rating()
    {
        Assert.NotNull(RatingScale.Validate(null));
    }

    [Fact]
    public void Is_not_tmdbs_scale()
    {
        // TMDb rates 0–10 and that lives on the film, not on a user. If this ever
        // starts accepting 8, someone has confused what the world thinks of a
        // film with what this user thinks.
        Assert.NotNull(RatingScale.Validate(8m));
    }
}
