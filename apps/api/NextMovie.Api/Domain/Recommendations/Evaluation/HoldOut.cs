namespace NextMovie.Api.Domain.Recommendations.Evaluation;

/// <summary>
/// Chooses which of somebody's favourite films to hide from the recommender.
/// </summary>
/// <remarks>
/// Deterministic from a seed (ADR-0012). An unseeded split would make every
/// improvement indistinguishable from a different shuffle: two runs of the same
/// code would disagree, and two runs of different code could not be compared at
/// all.
/// </remarks>
internal static class HoldOut
{
    /// <summary>
    /// Picks a fraction of the loved films to hide.
    /// </summary>
    /// <param name="loved">Every film the user rated highly. Order is irrelevant; it is shuffled.</param>
    /// <param name="fraction">Share to hide, 0 to 1.</param>
    /// <param name="seed">Fixes the shuffle, so the same inputs always give the same split.</param>
    /// <remarks>
    /// Rounds up, so a small library still hides something: with eleven loved
    /// films and a fifth held out, rounding down would hide two and rounding to
    /// nearest would hide two — and the difference matters far more at eleven
    /// films than at eight hundred.
    /// </remarks>
    public static IReadOnlyList<Guid> Choose(IReadOnlyList<Guid> loved, double fraction, int seed)
    {
        if (loved.Count == 0 || fraction <= 0)
        {
            return [];
        }

        var wanted = Math.Min(loved.Count, (int)Math.Ceiling(loved.Count * Math.Min(fraction, 1.0)));

        var shuffled = loved.ToArray();
        var random = new Random(seed);

        // Fisher-Yates over a copy. Ordering by a random key would be shorter and
        // would also give a different answer for the same seed depending on the
        // sort's stability, which defeats the point of seeding it.
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
        }

        return shuffled[..wanted];
    }
}
