namespace NextMovie.Api.Domain.Recommendations;

/// <summary>
/// Ranks candidate films against a taste profile, and says why.
/// </summary>
/// <remarks>
/// The weights below are ours, not the ones in
/// <c>docs/recommendation-engine.md</c>. That model spends 60% of its weight on
/// director similarity, themes, actors and a feedback model — none of which we
/// store (ADR-0008). Rather than keep the shape and quietly zero most of it,
/// these are the weights for the signals we actually have, chosen deliberately.
/// <para>
/// Every reason returned is derived from a component that materially moved the
/// score. An explanation that does not correspond to the ranking is a lie with
/// good manners, and it is the kind of lie users eventually catch.
/// </para>
/// </remarks>
public static class RecommendationScorer
{
    /// <summary>
    /// How much each signal contributes. They sum to 1.
    /// </summary>
    /// <remarks>
    /// Taste is about <em>this person</em>; quality is about the film; the
    /// interaction between them is where most of the signal actually lives.
    /// Popularity is deliberately the smallest: ranking by it would recommend the
    /// same films to everybody.
    /// </remarks>
    public const double TasteWeight = 0.35;
    public const double QualityWeight = 0.15;

    /// <summary>
    /// What a good film in a genre you actually watch is worth, beyond the two
    /// separately.
    /// </summary>
    /// <remarks>
    /// The largest single weight, and the correction that matters most. Added
    /// independently, taste and quality let a well-reviewed film be dragged down
    /// by a genre score and a genre you love be filled with mediocrity.
    /// Multiplied, they say the thing we actually mean: recommend the <em>good
    /// ones</em> from the kinds of film this person watches. It is why a
    /// well-reviewed animated film now ranks where it should for someone with
    /// ninety-seven animated films behind them.
    /// </remarks>
    public const double InteractionWeight = 0.30;

    public const double RuntimeWeight = 0.07;
    public const double EraWeight = 0.08;
    public const double PopularityWeight = 0.05;

    /// <summary>
    /// The affinity above which a genre is worth naming as a reason.
    /// </summary>
    /// <remarks>
    /// Affinity runs 0–1 with a half meaning no opinion, so this is "clearly
    /// above indifference" rather than merely non-negative.
    /// </remarks>
    private const double NotableAffinity = 0.6;

    /// <summary>Ratings needed before a recommendation is called confident.</summary>
    private const int ConfidentRatings = 100;

    private const int ModestlyConfidentRatings = 25;

    /// <summary>Scores one candidate.</summary>
    /// <param name="candidate">The film being considered.</param>
    /// <param name="profile">What this person watches.</param>
    /// <param name="genreNames">For phrasing the reasons.</param>
    /// <param name="genreInformativeness">
    /// How much each genre distinguishes one candidate from another in the pool
    /// being ranked. See <see cref="Informativeness"/>.
    /// </param>
    public static ScoredRecommendation Score(
        RecommendationCandidate candidate,
        TasteProfile profile,
        IReadOnlyDictionary<int, string> genreNames,
        IReadOnlyDictionary<int, double>? genreInformativeness = null)
    {
        var taste = GenreScore(candidate, profile, genreInformativeness);
        var quality = CommunityScore(candidate);
        var runtime = RangeScore(candidate.Runtime, profile.PreferredRuntimes, tolerance: 30);
        var era = RangeScore(candidate.ReleaseYear, profile.PreferredEra, tolerance: 15);
        var popularity = PopularityScore(candidate);

        var score =
            (taste * TasteWeight)
            + (quality * QualityWeight)
            + (taste * quality * InteractionWeight)
            + (runtime * RuntimeWeight)
            + (era * EraWeight)
            + (popularity * PopularityWeight);

        return new ScoredRecommendation(
            candidate.MovieId,
            Math.Clamp(score, 0, 1),
            Reasons(candidate, profile, genreNames, runtime, era),
            ConfidenceFor(profile, candidate));
    }

    /// <summary>
    /// How well the candidate's genres match what this person likes.
    /// </summary>
    /// <remarks>
    /// Averaged across the film's genres rather than taking the best one: a film
    /// that is part science fiction and part something the user dislikes is a
    /// worse bet than one that is wholly science fiction, and taking the maximum
    /// would rate them the same.
    /// </remarks>
    private static double GenreScore(
        RecommendationCandidate candidate,
        TasteProfile profile,
        IReadOnlyDictionary<int, double>? informativeness)
    {
        if (candidate.GenreIds.Count == 0 || profile.GenreAffinity.Count == 0)
        {
            // Nothing known either way. Neutral, not zero — zero would rank an
            // unknown film below one the user actively dislikes.
            return TasteProfile.NoOpinion;
        }

        // A weighted average across the film's genres rather than the best of
        // them: a film that is half something the user avoids is a worse bet than
        // one wholly in a genre they seek out, and a maximum would rate the two
        // the same.
        //
        // The weights are how much each genre distinguishes this film from the
        // others being ranked. When every candidate is a drama, being a drama
        // says nothing, and the genres that differ decide instead.
        var weighted = 0.0;
        var weights = 0.0;

        foreach (var genreId in candidate.GenreIds)
        {
            var weight = informativeness?.GetValueOrDefault(genreId, 1.0) ?? 1.0;

            weighted += profile.AffinityFor(genreId) * weight;
            weights += weight;
        }

        return weights <= 0
            ? TasteProfile.NoOpinion
            : Math.Clamp(weighted / weights, 0, 1);
    }

    /// <summary>
    /// How much each genre distinguishes one candidate from another.
    /// </summary>
    /// <remarks>
    /// A genre shared by nearly every candidate carries almost no information
    /// about which to prefer — the same reason a search engine discounts common
    /// words. Drama is 40% of all films; "you watch a lot of drama" is close to
    /// "you watch films".
    /// <para>
    /// Measured across the pool being ranked rather than the whole catalogue,
    /// which makes it self-correcting: when every candidate happens to be a
    /// drama, drama stops deciding and the genres that actually differ take over.
    /// </para>
    /// </remarks>
    public static Dictionary<int, double> Informativeness(
        IReadOnlyList<RecommendationCandidate> pool)
    {
        if (pool.Count == 0)
        {
            return [];
        }

        var counts = new Dictionary<int, int>();

        foreach (var genreId in pool.SelectMany(candidate => candidate.GenreIds.Distinct()))
        {
            counts[genreId] = counts.GetValueOrDefault(genreId) + 1;
        }

        return counts.ToDictionary(
            entry => entry.Key,

            // Inverse document frequency, floored so a ubiquitous genre is
            // discounted rather than silenced: it still carries a little signal.
            entry => Math.Max(0.1, Math.Log((double)pool.Count / entry.Value) + 0.1));
    }

    private static double CommunityScore(RecommendationCandidate candidate) =>
        // Unrated films sit slightly below the middle: absence of a community
        // verdict is weak evidence of obscurity, not of quality.
        candidate.CommunityRating is { } rating ? Math.Clamp(rating / 10.0, 0, 1) : 0.4;

    /// <summary>
    /// How near a value falls to a preferred range, fading out beyond it.
    /// </summary>
    /// <remarks>
    /// Inside the range scores full marks; outside, it decays linearly over
    /// <paramref name="tolerance"/> rather than dropping to nothing at the edge.
    /// A 145-minute film is not disqualified for someone whose usual range ends
    /// at 140.
    /// </remarks>
    private static double RangeScore(int? value, Range<int>? preferred, int tolerance)
    {
        if (value is not { } actual || preferred is null)
        {
            return 0.5;
        }

        if (preferred.Contains(actual))
        {
            return 1.0;
        }

        var distance = actual < preferred.From ? preferred.From - actual : actual - preferred.To;

        return Math.Clamp(1.0 - ((double)distance / tolerance), 0, 1);
    }

    /// <summary>
    /// Popularity, compressed hard.
    /// </summary>
    /// <remarks>
    /// TMDb popularity is unbounded and extremely skewed — a blockbuster can be
    /// a thousand times a quiet film's number. Taken raw it would swamp every
    /// other signal, so it is log-scaled: the difference between obscure and
    /// known matters, the difference between famous and enormous barely does.
    /// </remarks>
    private static double PopularityScore(RecommendationCandidate candidate) =>
        candidate.Popularity is { } popularity and > 0
            ? Math.Clamp(Math.Log10(popularity + 1) / 3.0, 0, 1)
            : 0.3;

    /// <summary>
    /// The reasons that actually moved this recommendation.
    /// </summary>
    /// <remarks>
    /// Built from the same components as the score, and only where a component
    /// was genuinely favourable. A list padded with "it is a film" would make
    /// every recommendation look identical, which is how explanations stop being
    /// read.
    /// </remarks>
    private static List<string> Reasons(
        RecommendationCandidate candidate,
        TasteProfile profile,
        IReadOnlyDictionary<int, string> genreNames,
        double runtimeScore,
        double eraScore)
    {
        var reasons = new List<string>();

        var strongGenres = candidate.GenreIds
            .Where(genreId => profile.AffinityFor(genreId) >= NotableAffinity)
            .OrderByDescending(profile.AffinityFor)
            .Select(genreId => genreNames.GetValueOrDefault(genreId))
            .OfType<string>()
            .Take(2)
            .ToList();

        if (strongGenres.Count > 0)
        {
            // Phrased as what they watch, not how they score it. The affinity
            // behind this is mostly appetite, and claiming they "rate it highly"
            // would be a reason that does not match the evidence.
            reasons.Add($"You watch a lot of {string.Join(" and ", strongGenres)}");
        }

        if (runtimeScore >= 1.0 && candidate.Runtime is { } runtime)
        {
            reasons.Add($"At {runtime} minutes, about the length you usually enjoy");
        }

        if (eraScore >= 1.0 && candidate.ReleaseYear is { } year)
        {
            reasons.Add($"From {year}, in the period you watch most");
        }

        if (candidate.CommunityRating is >= 7.5)
        {
            reasons.Add($"Rated {candidate.CommunityRating:0.0} by the wider audience");
        }

        return reasons;
    }

    /// <summary>
    /// How much evidence stands behind this particular recommendation.
    /// </summary>
    /// <remarks>
    /// Two things, and the weaker wins. How much the person has rated at all, and
    /// whether they have rated anything in <em>this film's</em> genres — a
    /// thousand ratings say nothing useful about a documentary if none of them
    /// are documentaries.
    /// </remarks>
    private static RecommendationConfidence ConfidenceFor(
        TasteProfile profile,
        RecommendationCandidate candidate)
    {
        var overall = profile.RatedFilms switch
        {
            >= ConfidentRatings => RecommendationConfidence.High,
            >= ModestlyConfidentRatings => RecommendationConfidence.Medium,
            _ => RecommendationConfidence.Low,
        };

        var knowsTheGenres = candidate.GenreIds.Count == 0
            || candidate.GenreIds.Any(profile.GenreAffinity.ContainsKey);

        return knowsTheGenres
            ? overall
            : (RecommendationConfidence)Math.Min((int)overall, (int)RecommendationConfidence.Low);
    }
}

/// <summary>A film that might be recommended.</summary>
/// <param name="MovieId">NextMovie identifier.</param>
/// <param name="GenreIds">TMDb genre identifiers.</param>
/// <param name="Runtime">Minutes, when known.</param>
/// <param name="ReleaseYear">Year, when known.</param>
/// <param name="CommunityRating">TMDb community rating 0–10, when the film has votes.</param>
/// <param name="Popularity">TMDb popularity. Unbounded and heavily skewed.</param>
public sealed record RecommendationCandidate(
    Guid MovieId,
    IReadOnlyList<int> GenreIds,
    int? Runtime,
    int? ReleaseYear,
    double? CommunityRating,
    double? Popularity);

/// <summary>A scored candidate, with the reasons behind the score.</summary>
/// <param name="MovieId">The film.</param>
/// <param name="Score">0–1. Comparable only against other candidates for the same person.</param>
/// <param name="Reasons">Why it ranked where it did. Possibly empty, never invented.</param>
/// <param name="Confidence">How much evidence stands behind the judgement.</param>
public sealed record ScoredRecommendation(
    Guid MovieId,
    double Score,
    IReadOnlyList<string> Reasons,
    RecommendationConfidence Confidence);

/// <summary>How much the engine trusts its own recommendation.</summary>
public enum RecommendationConfidence
{
    Low = 1,
    Medium = 2,
    High = 3,
}
