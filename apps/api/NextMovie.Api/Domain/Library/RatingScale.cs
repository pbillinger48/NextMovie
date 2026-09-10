namespace NextMovie.Api.Domain.Library;

/// <summary>
/// The scale NextMovie ratings live on: 0.5 to 5.0, in half-stars.
/// </summary>
/// <remarks>
/// Identical to Letterboxd's, which is why importing is a copy rather than a
/// conversion — see ADR-0006. Deliberately *not* TMDb's 0–10: that is what the
/// world thinks of a film, and it lives on <see cref="Movie.AverageRating"/>.
/// The two are never mixed.
/// </remarks>
public static class RatingScale
{
    public const decimal Minimum = 0.5m;

    public const decimal Maximum = 5.0m;

    /// <summary>The only increment the scale admits.</summary>
    public const decimal Step = 0.5m;

    /// <summary>
    /// Validates a rating.
    /// </summary>
    /// <returns>Null when acceptable, otherwise the message to show the user.</returns>
    /// <remarks>
    /// A zero is rejected rather than treated as "no rating". Absence of an
    /// opinion is the absence of a row, and letting zero mean both would make
    /// "unrated" and "hated it" indistinguishable to the recommendation engine.
    /// </remarks>
    public static string? Validate(decimal? rating) => rating switch
    {
        null => "A rating is required.",
        < Minimum or > Maximum => $"A rating must be between {Minimum:0.0} and {Maximum:0.0}.",

        // Modulo on decimal is exact, unlike on a binary float, which is one of
        // the reasons ratings are stored as numeric rather than double.
        _ when rating % Step != 0m => "A rating must be a whole or half star.",
        _ => null,
    };
}
