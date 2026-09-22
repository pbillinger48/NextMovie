namespace NextMovie.Api.Domain.Recommendations.Evaluation;

/// <summary>How alike two recommendation lists are.</summary>
/// <param name="Cohorts">How many lists were compared.</param>
/// <param name="MeanPairwise">
/// Average Jaccard similarity over every pair. 0 means no two lists share a film;
/// 1 means every list is identical.
/// </param>
/// <param name="Universal">Films that appear in every single list.</param>
/// <param name="ListSize">Films per list, when they are all the same length.</param>
internal sealed record Overlap(
    int Cohorts,
    double MeanPairwise,
    IReadOnlyList<Guid> Universal,
    int ListSize);

/// <summary>
/// Measures whether a recommender is answering the question it was asked.
/// </summary>
/// <remarks>
/// Hold-out recall (ADR-0012) cannot distinguish a recommender that reads
/// somebody's taste from one that returns the same canonical films to everyone —
/// because most people have seen the canon, so returning it scores well either
/// way. This does distinguish them: run the same engine for deliberately
/// different tastes and see whether the answers differ.
/// <para>
/// A high score here is bad. That inversion is deliberate and worth stating
/// wherever the number is printed.
/// </para>
/// </remarks>
internal static class OverlapMetrics
{
    /// <summary>
    /// Shared films as a share of the films in either list.
    /// </summary>
    /// <remarks>
    /// Jaccard rather than a raw count, so lists of different lengths compare
    /// honestly. Two lists of twelve sharing six is a far stronger signal than a
    /// list of twelve and a list of two sharing the same six.
    /// </remarks>
    public static double Jaccard(IReadOnlySet<Guid> left, IReadOnlySet<Guid> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            // Two empty lists are not "identical" in any useful sense. Returning 1
            // would report perfect overlap for a recommender that returned
            // nothing to anybody.
            return 0;
        }

        var shared = left.Count(film => right.Contains(film));

        return (double)shared / (left.Count + right.Count - shared);
    }

    /// <summary>Compares every list to every other and averages.</summary>
    public static Overlap Compare(IReadOnlyList<IReadOnlySet<Guid>> lists)
    {
        if (lists.Count < 2)
        {
            // Nothing to compare against. One list is not evidence either way,
            // and reporting 0 would read as "perfectly personalised".
            return new Overlap(lists.Count, MeanPairwise: 0, Universal: [], ListSize: lists.FirstOrDefault()?.Count ?? 0);
        }

        var pairs = new List<double>();

        for (var left = 0; left < lists.Count; left++)
        {
            for (var right = left + 1; right < lists.Count; right++)
            {
                pairs.Add(Jaccard(lists[left], lists[right]));
            }
        }

        var sizes = lists.Select(list => list.Count).Distinct().ToList();

        return new Overlap(
            Cohorts: lists.Count,
            MeanPairwise: pairs.Average(),
            Universal: Universal(lists),
            ListSize: sizes.Count == 1 ? sizes[0] : 0);
    }

    /// <summary>
    /// Films every list contains.
    /// </summary>
    /// <remarks>
    /// The most legible form of the finding. "Seven of twelve films appear in
    /// every list regardless of taste" says more to a person than any similarity
    /// coefficient does.
    /// </remarks>
    public static IReadOnlyList<Guid> Universal(IReadOnlyList<IReadOnlySet<Guid>> lists)
    {
        if (lists.Count == 0)
        {
            return [];
        }

        return [.. lists[0].Where(film => lists.All(list => list.Contains(film)))];
    }

    /// <summary>How many of one list's films appear in at least one other.</summary>
    public static int SharedWithAny(IReadOnlySet<Guid> list, IReadOnlyList<IReadOnlySet<Guid>> others) =>
        list.Count(film => others.Any(other => !ReferenceEquals(other, list) && other.Contains(film)));
}
